using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Chord.Common;
using Chord.Runtime;
using Core.Exceptions;
using Core.Meta;
using Core.Meta.Extensions;

namespace Core.Generators;

public class ContributedGenerator : BaseGenerator
{
    private readonly Extension _extension;

    public ContributedGenerator(Extension extension)
    {
        _extension = extension;
        if (extension.Contributions is not ChordGenerator chordGenerator)
        {
            throw new InvalidOperationException($"Extension {extension.Name} does not contribute a ChordGenerator.");
        }
        Alias = chordGenerator.Alias;
        Name = chordGenerator.Name;
    }

    public override async ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config, CancellationToken cancellationToken)
    {
        var artifacts = new List<Artifact>();
        var context = new GeneratorContext(schema, config);
        var output = await _extension.ChordCompileAsync(context.ToString(), cancellationToken);

        artifacts.Add(new Artifact(config.OutFile, Encoding.UTF8.GetBytes(output)));
        if (_extension.PackedFiles is not { Count: > 0 })
        {
            if (_extension.PackedFiles.FirstOrDefault(f => f.Alias == Alias) is { } packedFile)
            {
                artifacts.Add(new Artifact(packedFile.Name, packedFile.Data));
            }

        }
        return artifacts.ToArray();
    }

    public override string Alias { get; set; }
    public override string Name { get; set; }
}