using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Compiler;
using Core.Logging;
using Core.Meta;
using Microsoft.Extensions.FileSystemGlobbing;
using Spectre.Console;

public class SchemaWatcher
{
    private readonly string _watchDirectory;
    private readonly FileSystemWatcher _filewatcher;
    private List<string> _excludeDirectories = new List<string>();
    private List<string> _excludeFiles = new List<string>();
    private List<string> _trackedFiles = new List<string>();
    private readonly BebopConfig _config;
    private readonly BebopCompiler _compiler;
    private readonly bool _preserveWatchOutput;
    private TaskCompletionSource<int>? _tcs;
    private CancellationToken _cancellationToken;

    private readonly SemaphoreSlim _compileSemaphore = new SemaphoreSlim(1, 1);
    private readonly TimeSpan _recompileTimeout = TimeSpan.FromSeconds(2);

    ///<summary>
    /// Initializes a new instance of the Watcher class.
    ///</summary>
    ///<param name="watchDirectory">The directory to watch.</param>
    ///<param name="trackedFiles">The list of files to track.</param>
    public SchemaWatcher(string watchDirectory, BebopConfig config, BebopCompiler compiler)
    {
        _compiler = compiler;
        _config = config;
        _preserveWatchOutput = config.WatchOptions.PreserveWatchOutput;
        _trackedFiles = config.ResolveIncludes().ToList();
        _watchDirectory = watchDirectory;
        _filewatcher = new FileSystemWatcher();
        _filewatcher.Path = _watchDirectory;
        _filewatcher.IncludeSubdirectories = true;
        _filewatcher.Filter = "*";
        _filewatcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
        _filewatcher.Changed += new FileSystemEventHandler(FileChanged);
        _filewatcher.Created += new FileSystemEventHandler(FileCreated);
        _filewatcher.Deleted += new FileSystemEventHandler(FileDeleted);
        _filewatcher.Renamed += new RenamedEventHandler(FileRenamed);
        _filewatcher.Error += new ErrorEventHandler(OnError);
        _filewatcher.EnableRaisingEvents = true;
        _filewatcher.InternalBufferSize = 65536;
    }

    ///<summary>
    /// Starts the watcher.
    ///</summary>
    ///<returns>Returns a Task representing the watcher operation.</returns>
    public async Task<int> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_tcs is not null) throw new InvalidOperationException("Watcher is already running.");
        _cancellationToken = cancellationToken;
        _tcs = new TaskCompletionSource<int>(_cancellationToken);

        var table = new Table().HeavyBorder().BorderColor(Color.Grey).Title("Watching").RoundedBorder();
        table.AddColumns("[white]Status[/]", "[white]Path[/]");
        table.AddRow(new Text("Watching", new Style(Color.Green)), new TextPath(_watchDirectory));
        table.AddEmptyRow();

        table.AddRow(new Text("Excluded Directories", new Style(Color.Yellow)));
        foreach (var excludedDir in _excludeDirectories)
        {
            table.AddRow(new Text(""), new TextPath(excludedDir));
        }
        table.AddEmptyRow();
        table.AddRow(new Text("Excluded Files", new Style(Color.Yellow)));
        foreach (var excludeFile in _excludeFiles)
        {
            table.AddRow(new Text(""), new TextPath(excludeFile));
        }
        DiagnosticLogger.Instance.WriteTable(table);
        try { return await _tcs.Task; }
        catch (OperationCanceledException)
        {
            // Handle the cancellation
            _filewatcher.EnableRaisingEvents = false;
            _filewatcher.Dispose();
            return 0;
        }
    }

    ///<summary>
    /// Handles the Error event of the FileSystemWatcher.
    ///</summary>
    private void OnError(object sender, ErrorEventArgs e)
    {
        _tcs?.TrySetResult(DiagnosticLogger.Instance.WriteDiagonstic(e.GetException()));
    }

    ///<summary>
    /// Adds a list of directories to exclude.
    ///</summary>
    ///<param name="excludeDirectories">The list of directories to exclude.</param>
    public void AddExcludeDirectories(List<string> excludeDirectories)
    {
        // Get all subdirectories of the watch directory
        var subdirectories = Directory.GetDirectories(_watchDirectory, "*", SearchOption.AllDirectories);

        // Create a matcher to filter the subdirectories
        var matcher = new Matcher();
        matcher.AddIncludePatterns(excludeDirectories);

        // Filter the subdirectories based on the exclude patterns
        var matchingDirectories = subdirectories.Where(d => matcher.Match(Path.GetRelativePath(_watchDirectory, d)).HasMatches);

        // Ensure that each directory is unique and not empty
        _excludeDirectories = matchingDirectories.Where(d => !string.IsNullOrEmpty(d))
                                                   .Distinct()
                                                   .ToList();
    }


    ///<summary>
    /// Adds a list of files to exclude.
    ///</summary>
    ///<param name="excludeFiles">The list of files to exclude.</param>
    public void AddExcludeFiles(List<string> excludeFiles)
    {
        var matcher = new Matcher();
        matcher.AddIncludePatterns(excludeFiles);
        _excludeFiles = matcher.GetResultsInFullPath(_watchDirectory).Where(f => Path.GetExtension(f).Equals(".bop")).ToList();
    }

    ///<summary>
    /// Handles the Renamed event of the FileSystemWatcher.
    ///</summary>
#pragma warning disable VSTHRD100 // Avoid async void methods
    private async void FileRenamed(object sender, RenamedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
    {
        string oldFullPath = Path.GetFullPath(e.OldFullPath);
        string newFullPath = Path.GetFullPath(e.FullPath);

        FileAttributes attrs = File.GetAttributes(newFullPath);
        try
        {
            attrs = File.GetAttributes(newFullPath);
        }
        catch (Exception ex)
        {
            // The file or directory may have been deleted or is inaccessible
            _tcs?.SetResult(DiagnosticLogger.Instance.WriteDiagonstic(ex));
            return;
        }

        for (int i = 0; i < _trackedFiles.Count; i++)
        {
            // If it's a directory
            if (attrs.HasFlag(FileAttributes.Directory))
            {
                // Check if the tracked file is in the renamed directory
                if (_trackedFiles[i].StartsWith(oldFullPath, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Replace the old directory path with the new one
                    _trackedFiles[i] = string.Concat(newFullPath, _trackedFiles[i].AsSpan(oldFullPath.Length));
                }
            }
            // If it's a file
            else
            {
                // Check if the tracked file itself was renamed
                if (_trackedFiles[i].Equals(e.OldFullPath))
                {
                    // If the file was moved to an excluded path, remove it from _trackedFiles
                    if (IsPathExcluded(e.FullPath))
                    {
                        _trackedFiles.RemoveAt(i);
                        i--;
                        LogEvent("[indianred1]Schema moved to an excluded path. No longer watching[/]", e.OldFullPath, e.FullPath);
                    }
                    else
                    {
                        // If the extension is still ".bop", update the file path in _trackedFiles
                        if (Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase))
                        {
                            _trackedFiles[i] = e.FullPath;
                        }
                        else
                        {
                            // If the extension changed, remove the file from _trackedFiles
                            _trackedFiles.RemoveAt(i);
                            i--;
                            LogEvent("[indianred1]Schema extension changed. No longer watching[/]", e.OldFullPath, e.FullPath);
                        }
                    }
                }
                // If the file was moved from an excluded path to a non-excluded path with a ".bop" extension, add it to _trackedFiles
                else if (IsPathExcluded(e.OldFullPath) && !IsPathExcluded(e.FullPath) && Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase))
                {
                    _trackedFiles.Add(e.FullPath);
                }
            }
        }

        if (!Path.GetExtension(e.OldFullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase)
        && Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase))
        {
            // If the new file path is not excluded, add it to _trackedFiles
            if (!IsPathExcluded(e.FullPath))
            {
                _trackedFiles.Add(e.FullPath);
            }
        }
        LogEvent("[orangered1]Schema renamed. Start recompile[/]", e.OldFullPath, e.FullPath);
        await CompileSchemasAsync(_cancellationToken);
    }



    ///<summary>
    /// Handles the Deleted event of the FileSystemWatcher.
    ///</summary>
#pragma warning disable VSTHRD100 // Avoid async void methods
    private async void FileDeleted(object sender, FileSystemEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
    {
        if (!Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase) || IsPathExcluded(e.FullPath))
        {
            return;
        }
        _trackedFiles.Remove(e.FullPath);

        LogEvent("[indianred1]Schema deleted. Starting recompile[/]", e.FullPath);
        await CompileSchemasAsync(_cancellationToken);
    }

    ///<summary>
    /// Handles the Created event of the FileSystemWatcher.
    ///</summary>
    private void FileCreated(object sender, FileSystemEventArgs e)
    {
        if (!Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase) || IsPathExcluded(e.FullPath))
        {
            return;
        }
        if (!_trackedFiles.Contains(e.FullPath))
        {
            _trackedFiles.Add(e.FullPath);
            LogEvent("[green]Schema created. Watching for changes[/]", e.FullPath);
        }
    }



    ///<summary>
    /// Handles the Changed event of the FileSystemWatcher.
    ///</summary>
#pragma warning disable VSTHRD100 // Avoid async void methods
    private async void FileChanged(object sender, FileSystemEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
    {
        if (!Path.GetExtension(e.FullPath).Equals(".bop", StringComparison.InvariantCultureIgnoreCase) || IsPathExcluded(e.FullPath))
        {
            return;
        }

        // Handle file changes
        if (_trackedFiles.Contains(e.FullPath))
        {
            if (_compileSemaphore.Wait(_recompileTimeout, _cancellationToken))
            {
                try
                {
                    LogEvent("[blue]Schema changed. Starting recompile[/]", e.FullPath);
                    var result = await CompileSchemasAsync(_cancellationToken);
                    if (result is BebopCompiler.Ok)
                    {
                        LogEvent("[green]Schema recompilation succeeded. Resuming watch.[/]");
                    }
                }
                finally
                {
                    _compileSemaphore.Release();
                }
            }
            else
            {
                LogEvent("[yellow]Recompile skipped due to rapid file changes. Resuming watch.[/]");
            }
        }
    }



    ///<summary>
    /// Checks if a file path is excluded from being watched based on the exclude lists.
    ///</summary>
    ///<param name="filePath">The file path to check.</param>
    ///<returns>Returns true if the file path is excluded, false otherwise.</returns>
    private bool IsPathExcluded(string filePath)
    {
        foreach (var excludedDir in _excludeDirectories)
        {
            if (filePath.StartsWith(Path.GetFullPath(excludedDir), StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        string fileName = Path.GetFileName(filePath);
        if (_excludeFiles.Contains(fileName))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Logs an event with a formatted message, optional file paths, and a timestamp.
    /// </summary>
    /// <param name="message">The message to display in the log event.</param>
    /// <param name="filePath">The optional file path to display in the log event. If null, no file path will be displayed.</param>
    /// <param name="newFilePath">The optional new file path to display in the log event. If null, no new file path will be displayed.</param>
    private void LogEvent(string message, string? filePath = null, string? newFilePath = null, bool isError = false)
    {
        var console = isError ? DiagnosticLogger.Instance.Error : DiagnosticLogger.Instance.Out;
        console.WriteLine();
        console.Markup("[white]{0}[/][grey]{1}[/][white]{2}[/] {3}{4} ", Markup.Escape("["), Markup.Escape(DateTime.Now.ToString("HH:mm:ss")), Markup.Escape("]"), message, filePath is not null ? ':' : string.Empty);
        if (filePath is not null)
        {
            var path = new TextPath(filePath)
            {
                StemStyle = Style.Parse("white"),
                LeafStyle = Style.Parse("white")
            };
            console.Write(path);
        }
        if (newFilePath is not null)
        {
            console.Write(" -> ");
            console.Write(new TextPath(newFilePath)
            {
                StemStyle = Style.Parse("white"),
                LeafStyle = Style.Parse("white")
            });
        }
        if (filePath is null && newFilePath is null)
        {
            console.WriteLine();
        }
    }

    /// <summary>
    /// Compiles schemas using the specified generators and outputs the results to the specified files.
    /// </summary>
    /// <returns>
    /// An integer representing the compilation result. Returns BebopCompiler.Ok if the compilation is successful,
    /// otherwise, returns BebopCompiler.Err.
    /// </returns>
    public async ValueTask<int> CompileSchemasAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_preserveWatchOutput)
            {
                DiagnosticLogger.Instance.Error.Clear();
            }
            if (_trackedFiles.Count == 0)
            {
                LogEvent("[yellow]Recompile skipped. No schemas being tracked.[/]");
                return BebopCompiler.Ok;
            }
            var schema = _compiler.ParseSchema(_trackedFiles);
            var (warnings, errors) = BebopCompiler.GetSchemaDiagnostics(schema, _config.SupressedWarningCodes);
            if (_config.NoEmit || errors.Count != 0)
            {
                DiagnosticLogger.Instance.WriteSpanDiagonstics([.. warnings, .. errors]);
                return errors.Count != 0 ? BebopCompiler.Err : BebopCompiler.Ok;
            }
            DiagnosticLogger.Instance.WriteSpanDiagonstics(warnings);
            foreach (var generatorConfig in _config.Generators)
            {
                var results = await _compiler.BuildAsync(generatorConfig, schema, _config, cancellationToken);
                BebopCompiler.EmitGeneratedFiles(results, _config);
            }
            return BebopCompiler.Ok;
        }
        catch (Exception ex)
        {
            return DiagnosticLogger.Instance.WriteDiagonstic(ex);
        }

    }
}