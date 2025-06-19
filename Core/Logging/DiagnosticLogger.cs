using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Core.Exceptions;
using Spectre.Console;
using Core.Meta;

namespace Core.Logging;

public partial class DiagnosticLogger
{
    private static DiagnosticLogger? _instance;
    private LogFormatter _formatter;
    private bool _traceEnabled;
    private readonly IAnsiConsole _out;
    private readonly IAnsiConsole _err;
    public IAnsiConsole Out => _out;
    public IAnsiConsole Error => _err;

    public bool TraceEnabled => _traceEnabled;

    #region Static Methods
    private DiagnosticLogger(LogFormatter formatter)
    {
        _formatter = formatter;
        var isWasm = RuntimeInformation.OSArchitecture is Architecture.Wasm;
        if (isWasm)
        {
            _out = VirtualTerminal.Create(Console.Out);
            _err = VirtualTerminal.Create(Console.Error);
        }
        else
        {
            _out = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Detect,
                ColorSystem = ColorSystemSupport.Detect,
                Out = new AnsiConsoleOutput(Console.Out),
            });
            _err = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Detect,
                ColorSystem = ColorSystemSupport.Detect,
                Out = new AnsiConsoleOutput(Console.Error),
            });
        }
    }

    public void SetFormatter(LogFormatter formatter)
    {
        _formatter = formatter;
    }

    public void EnableTrace()
    {
        _traceEnabled = true;
    }

    public static void Initialize(LogFormatter formatter)
    {
        if (!Enum.IsDefined(typeof(LogFormatter), formatter))
        {
            throw new ArgumentOutOfRangeException(nameof(formatter),
                "Value should be defined in the Formatter enum.");
        }
        if (_instance is not null)
        {
            return;
        }
        _instance = new DiagnosticLogger(formatter);
    }

    public static DiagnosticLogger Instance
    {
        get
        {
            if (_instance is null)
            {
                throw new CompilerException("The diagonstic logger has not been initialized.");
            }
            return _instance;
        }
    }

    #endregion


    public int WriteDiagonstic(Exception exception)
    {
        switch (exception)
        {
            case SpanException span:
                WriteSpanDiagonstics(new List<SpanException>() { span });
                return 1;
            case FileNotFoundException file:
                WriteFileNotFoundDiagonstic(file);
                return 1;
            case CompilerException compiler:
                WriteCompilerDiagonstic(compiler);
                return 1;
            default:
                WriteBaseDiagonstic(exception);
                return 1;
        }
    }

    public void WriteSpanDiagonstics(List<SpanException> exceptions)
    {
        if (_formatter is LogFormatter.Enhanced)
        {
            RenderEnhancedSpanErrors(exceptions);
            WriteErrorLine(string.Empty);
            return;
        }
        if (_formatter is LogFormatter.JSON)
        {
            var warnings = exceptions.Where(e => e.Severity == Severity.Warning).ToList();
            var errors = exceptions.Where(e => e.Severity == Severity.Error).ToList();
            ErrorCompilerOutput(new CompilerOutput(warnings, errors, null));
            return;
        }
        var messages = exceptions.Select(FormatSpanError);
        if (messages is null)
        {
            return;
        }
        var joined = _formatter switch
        {
            LogFormatter.JSON => "[" + string.Join(",\n", messages) + "]",
            _ => string.Join("\n", messages),
        };
        if (string.IsNullOrWhiteSpace(joined))
        {
            // Don't print a single blank line.
            return;
        }
        _err.WriteLine(joined);
    }

    private void WriteFileNotFoundDiagonstic(FileNotFoundException ex)
    {
        if (_formatter is LogFormatter.Enhanced)
        {
            RenderEnhancedException(ex, FileNotFound);
            return;
        }
        _err.WriteLine(FormatDiagnostic(new(Severity.Error, "Unable to open file: " + ex?.FileName, FileNotFound, null)));
    }

    private void WriteCompilerDiagonstic(CompilerException ex)
    {
        if (_formatter is LogFormatter.Enhanced)
        {
            RenderEnhancedException(ex, ex.ErrorCode);
            return;
        }
        _err.WriteLine(FormatDiagnostic(new(Severity.Error, ex.Message, ex.ErrorCode, null)));
    }

    private void WriteBaseDiagonstic(Exception ex)
    {
        if (_formatter is LogFormatter.Enhanced)
        {
            RenderEnhancedException(ex, Unknown);
            return;
        }
        _err.WriteLine(FormatDiagnostic(new(Severity.Error, ex.Message, Unknown, null)));
    }


    public void ErrorCompilerOutput(CompilerOutput output)
    {
        if (output.Errors.Count > 0 || output.Warnings.Count > 0)
        {
            _err.WriteLine(FormatCompilerOutput(output));
            _err.WriteLine(string.Empty);
        }
    }

    public void PrintCompilerOutput(CompilerOutput output)
    {
        if (output.Artifacts is { Length: > 0 })
        {
            _out.WriteLine(FormatCompilerOutput(output));
            _out.WriteLine(string.Empty);
        }
    }

    public void WriteLine(string message)
    {
        _out.WriteLine(message);
    }
    public void Write(string message)
    {
        _out.Write(message);
    }

    public void WriteError(string message)
    {
        _err.Write(message);
    }
    public void WriteErrorLine(string message)
    {
        _err.WriteLine(message);
    }
}
