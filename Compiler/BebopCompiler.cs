using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Core.Meta;
using Core.Exceptions;
using Core.Parser;
using Core.Generators;
using System.IO;
using Core;

namespace Compiler;

public class BebopCompiler(CompilerHost Host)
{
    public const int Ok = 0;
    public const int Err = 1;


    public BebopSchema ParseSchema(IEnumerable<string> schemaPaths)
    {
        var parser = new SchemaParser(schemaPaths, Host);
        var schema = parser.Parse();
        schema.Validate();
        return schema;
    }

    public static void EmitGeneratedFiles(CompilationResult results, BebopConfig config)
    {
        foreach (var artifact in results.Artifacts)
        {


            if (!Directory.Exists(results.OutputDirectory))
            {
                Directory.CreateDirectory(results.OutputDirectory);
            }
            var outPath =  Path.GetFullPath(Path.Combine(results.OutputDirectory, Path.GetFileName(artifact.Name)));

            File.WriteAllBytes(outPath, artifact.Content);
        }
    }


    public async ValueTask<CompilationResult> BuildAsync(GeneratorConfig generatorConfig, BebopSchema schema, BebopConfig config, CancellationToken cancellationToken)
    {
        if (!Host.TryGetGenerator(generatorConfig.Alias, out var generator))
        {
            throw new CompilerException($"Could not find generator with alias '{generatorConfig.Alias}'.");
        }

        var artifacts = await generator.Compile(schema, generatorConfig, cancellationToken);
        var baseOutputDirectory = Path.GetFullPath(config.WorkingDirectory);
        var outFile = generatorConfig.OutFile;
        if (!Path.IsPathRooted(outFile))
        {
            outFile = Path.GetFullPath(Path.Combine(baseOutputDirectory, outFile));
        }
        var outDirectory = Path.GetDirectoryName(outFile) ?? throw new CompilerException("Could not determine output directory.");
        return new CompilationResult(artifacts, outDirectory);
    }

    public static (List<SpanException> Warnings, List<SpanException> Errors) GetSchemaDiagnostics(BebopSchema schema, int[] supressWarningCodes)
    {
        var noWarn = supressWarningCodes;
        var loudWarnings = schema.Warnings.Where(x => !noWarn.Contains(x.ErrorCode)).ToList();
        return (loudWarnings, schema.Errors);
    }
}