namespace Core.Meta;
using Core.Logging;
using System.Collections.Generic;
using Core.Exceptions;
using System.Text.Json.Serialization;


public record Artifact(string Name, byte[] Content);
public record CompilationResult(Artifact[] Artifacts, string OutputDirectory);
public record CompilerOutput(List<SpanException> Warnings, List<SpanException> Errors, Artifact[]? Artifacts) { }