
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Core.Exceptions;
using Core.Generators;
using Core.Lexer.Tokenization.Models;
using Core.Logging;
using Core.Meta.Extensions;

namespace Core.Meta;
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    DefaultBufferSize = 10,
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    Converters = [typeof(SpanExceptionConverter), typeof(ExceptionConverter), typeof(BebopConfigConverter), typeof(GeneratorContextConverter)])]
[JsonSerializable(typeof(BebopConfig))]
[JsonSerializable(typeof(TempoServices))]
[JsonSerializable(typeof(Severity))]
[JsonSerializable(typeof(GeneratorConfig))]
[JsonSerializable(typeof(WatchOptions))]
[JsonSerializable(typeof(LogFormatter))]
[JsonSerializable(typeof(CompilerOutput))]
[JsonSerializable(typeof(CompilationResult))]
[JsonSerializable(typeof(Artifact))]
[JsonSerializable(typeof(Artifact[]))]
[JsonSerializable(typeof(Exception))]
[JsonSerializable(typeof(SpanException))]
[JsonSerializable(typeof(DiagnosticLogger.Diagnostic))]
[JsonSerializable(typeof(Span))]
[JsonSerializable(typeof(GeneratorContext))]
public partial class JsonContext : JsonSerializerContext
{
}

class SpanExceptionConverter : JsonConverter<SpanException>
{
    public override SpanException Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, SpanException value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("span");
        writer.WriteString("fileName", value.Span.FileName);
        writer.WriteNumber("startLine", value.Span.StartLine);
        writer.WriteNumber("endLine", value.Span.EndLine);
        writer.WriteNumber("startColumn", value.Span.StartColumn);
        writer.WriteNumber("endColumn", value.Span.EndColumn);
        writer.WriteNumber("lines", value.Span.Lines);
        writer.WriteEndObject();
        writer.WriteNumber("errorCode", value.ErrorCode);
        writer.WriteString("severity", value.Severity.ToString().ToCamelCase());
        writer.WriteString("message", value.Message);
        writer.WriteEndObject();
    }
}

class ExceptionConverter : JsonConverter<Exception>
{
    public override Exception Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value is CompilerException compilerException)
        {
            writer.WriteNumber("errorCode", compilerException.ErrorCode);
        }
        writer.WriteString("message", value.Message);
        if (value.InnerException is not null)
        {
            writer.WritePropertyName("innerException");
            if (value.InnerException is SpanException spanException)
            {
                JsonSerializer.Serialize(writer, spanException, JsonContext.Default.SpanException);
            }
            else
            {
                JsonSerializer.Serialize(writer, value.InnerException, JsonContext.Default.Exception);
            }
        }
        writer.WriteEndObject();
    }
}