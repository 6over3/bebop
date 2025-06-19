using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Core.Exceptions;
using Core.Logging;
using Core.Meta;

namespace Compiler.Commands;

public class BuildCommand : CliCommand
{
    public BuildCommand() : base(CliStrings.BuildCommand, "Build schemas into one or more target languages.")
    {
        SetAction(HandleCommandAsync);
    }

    private async Task<int> HandleCommandAsync(ParseResult result, CancellationToken cancellationToken)
    {
        var config = result.GetValue<BebopConfig>(CliStrings.ConfigFlag)!;

        config.Validate();

        using var host = CompilerHost.CompilerHostBuilder.Create(config.WorkingDirectory)
        .WithDefaults()
#if !WASI_WASM_BUILD && !WIN_ARM64
        .WithExtensions(config.Extensions)
#endif
        .Build();

        var compiler = new BebopCompiler(host);
        Helpers.WriteHostInfo(host);
        BebopSchema schema = default;
        string? tempFilePath = null;
        try
        {
            // if input is redirected, read from stdin, ignore includes.
            // this is a temporary flag as it appears there is a bug in wasi builds
            // that always returns true for Console.IsInputRedirected
            if (result.GetValue<bool>(CliStrings.StandardInputFlag))
            {
                // we should write the textual stream to a temp file and then parse it.
                tempFilePath = Helpers.GetTempFileName();
                using var fs = File.Open(tempFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                using var standardInput = Console.OpenStandardInput();
                // dont use async as wasi currently has threading issues
#if !WASI_WASM_BUILD
                await standardInput.CopyToAsync(fs, cancellationToken);
#else
                 standardInput.CopyTo(fs);
#endif

                fs.Seek(0, SeekOrigin.Begin);
                schema = compiler.ParseSchema([tempFilePath]);
            }
            else
            {
                var resolvedSchemas = config.ResolveIncludes();
                if (!resolvedSchemas.Any())
                {
                    return DiagnosticLogger.Instance.WriteDiagonstic(new CompilerException("No input files specified."));
                }
                schema = compiler.ParseSchema(resolvedSchemas);
            }

            if (config is { Generators.Length: <= 0 } && !config.NoEmit)
            {
                return DiagnosticLogger.Instance.WriteDiagonstic(new CompilerException("No code generators specified."));
            }
            var isStandardOut = result.GetValue<bool>(CliStrings.StandardOutputFlag);
            var (Warnings, Errors) = BebopCompiler.GetSchemaDiagnostics(schema, config.SupressedWarningCodes);
            if (config.NoEmit || Errors.Count != 0)
            {
                DiagnosticLogger.Instance.WriteSpanDiagonstics([.. Warnings, .. Errors]);
                return Errors.Count != 0 ? BebopCompiler.Err : BebopCompiler.Ok;
            }
            // we only want to write the warnings if we are not writing results to stdout
            // as we concat the outputs of all generators into one stream
            if (!isStandardOut && Warnings.Count != 0)
            {
                DiagnosticLogger.Instance.WriteSpanDiagonstics(Warnings);
            }
            if (!isStandardOut)
            {
                foreach (var generator in config.Generators)
                {
                    BebopCompiler.EmitGeneratedFiles(await compiler.BuildAsync(generator, schema, config, cancellationToken), config);
                }
            }
            else
            {
                var results = new List<Artifact>();
                foreach (var generatorConfig in config.Generators)
                {
                    results.AddRange((await compiler.BuildAsync(generatorConfig, schema, config, cancellationToken)).Artifacts);
                }
                DiagnosticLogger.Instance.PrintCompilerOutput(new CompilerOutput(Warnings, Errors, [.. results]));
            }
            return BebopCompiler.Ok;
        }
        catch (Exception ex)
        {
            return DiagnosticLogger.Instance.WriteDiagonstic(ex);
        }
        finally
        {
            if (tempFilePath is not null)
            {
                File.Delete(tempFilePath);
            }
        }
    }
}