using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Core.Meta;
using Core.Meta.Extensions;
using Core.Meta.Decorators;
using System.Threading.Tasks;
using System.Threading;

namespace Core.Generators.Python
{
    public class PythonGenerator : BaseGenerator
    {
        const int indentStep = 4;

        public PythonGenerator() : base() { }

        private string FormatDocumentation(string documentation, string? deprecated)
        {
            var builder = new StringBuilder();
            builder.AppendLine("\"\"\"");
            foreach (var line in documentation.GetLines())
            {
                builder.AppendLine(line);
            }
            if (deprecated is not null)
            {
                builder.AppendLine($"@deprecated {deprecated}");
            }
            builder.AppendLine("\"\"\"");
            return builder.ToString();
        }

        /// <summary>
        /// Generate the body of the <c>encode</c> function for the given <see cref="FieldsDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated Python <c>encode</c> function body.</returns>
        public string CompileEncode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileEncodeMessage(d),
                StructDefinition d => CompileEncodeStruct(d),
                UnionDefinition d => CompileEncodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileEncode value: {definition}"),
            };
        }

        private string CompileEncodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"pos = writer.reserve_message_length()");
            builder.AppendLine($"start = writer.length");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedDecorator != null)
                {
                    continue;
                }
                builder.AppendLine($"if hasattr(message, \"{field.Name}\"):");
                builder.AppendLine($"    writer.write_byte({field.ConstantValue})");
                builder.AppendLine($"    {CompileEncodeField(field.Type, $"message.{field.Name}", 0, 1)}");
            }
            builder.AppendLine("writer.write_byte(0)");
            builder.AppendLine("end = writer.length");
            builder.AppendLine("writer.fill_message_length(pos, end - start)");
            return builder.ToString();
        }

        private string CompileEncodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileEncodeField(field.Type, $"message.{field.Name}"));
                builder.AppendLine("");
            }
            if (definition.Fields.Count == 0)
            {
                builder.AppendLine("pass");
            }
            return builder.ToString();
        }

        private string CompileEncodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"pos = writer.reserve_message_length()");
            builder.AppendLine($"start = writer.length + 1");
            builder.AppendLine($"writer.write_byte(message.data.discriminator)");
            builder.AppendLine("discriminator = message.data.discriminator");
            builder.AppendLine($"if discriminator == 1:");
            builder.AppendLine($"    {definition.Branches.ElementAt(0).ClassName()}.encode_into(message.data.value, writer)");
            foreach (var branch in definition.Branches.Skip(1))
            {
                builder.AppendLine($"elif discriminator == {branch.Discriminator}:");
                builder.AppendLine($"    {branch.ClassName()}.encode_into(message.data.value, writer)");
            }
            builder.AppendLine("end = writer.length");
            builder.AppendLine("writer.fill_message_length(pos, end - start)");
            return builder.ToString();
        }

        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0, bool isEnum = false)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            var enumAppendix = isEnum ? ".value" : "";
            return type switch
            {
                ArrayType at when at.IsBytes() => $"writer.write_bytes({target})",
                ArrayType at =>
                    $"length{depth} = len({target})" + nl +
                    $"writer.write_uint32(length{depth})" + nl +
                    $"for {i} in range(length{depth}):" + nl +
                    $"{tab}{CompileEncodeField(at.MemberType, $"{target}[{i}]", depth + 1, indentDepth + 1)}",
                MapType mt =>
                    $"writer.write_uint32(len({target}))" + nl +
                    $"for key{depth}, val{depth} in {target}.items():" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"key{depth}", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"val{depth}", depth + 1, indentDepth + 1)}" + nl,
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"writer.write_bool({target}{enumAppendix})",
                    BaseType.Byte => $"writer.write_byte({target}{enumAppendix})",
                    BaseType.UInt16 => $"writer.write_uint16({target}{enumAppendix})",
                    BaseType.Int16 => $"writer.write_int16({target}{enumAppendix})",
                    BaseType.UInt32 => $"writer.write_uint32({target}{enumAppendix})",
                    BaseType.Int32 => $"writer.write_int32({target}{enumAppendix})",
                    BaseType.UInt64 => $"writer.write_uint64({target}{enumAppendix})",
                    BaseType.Int64 => $"writer.write_int64({target}{enumAppendix})",
                    BaseType.Float32 => $"writer.write_float32({target}{enumAppendix})",
                    BaseType.Float64 => $"writer.write_float64({target}{enumAppendix})",
                    BaseType.String => $"writer.write_string({target}{enumAppendix})",
                    BaseType.Guid => $"writer.write_guid({target}{enumAppendix})",
                    BaseType.Date => $"writer.write_date({target}{enumAppendix})",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                },
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    CompileEncodeField(ed.ScalarType, target, depth, indentDepth, true),
                DefinedType dt => $"{dt.Name}.encode_into({target}, writer)",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="FieldsDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated Dart <c>decode</c> function body.</returns>
        public string CompileDecode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileDecodeMessage(d),
                StructDefinition d => CompileDecodeStruct(d),
                UnionDefinition d => CompileDecodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileDecode value: {definition}"),
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="MessageDefinition"/>.
        /// </summary>
        /// <param name="definition">The message definition to generate code for.</param>
        /// <returns>The generated Dart <c>decode</c> function body.</returns>
        private string CompileDecodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"message = {definition.Name}()");
            builder.AppendLine("length = reader.read_message_length()");
            builder.AppendLine("end = reader.index + length");
            builder.AppendLine("while True:");
            builder.Indent(2);
            builder.AppendLine("byte = reader.read_byte()");
            builder.AppendLine("if byte == 0:");
            builder.AppendLine("    return message");
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"elif byte == {field.ConstantValue}:");
                builder.AppendLine($"    {CompileDecodeField(field.Type, $"message.{field.Name}", 1)}");
            }
            builder.AppendLine("else:");
            builder.AppendLine("    reader.index = end");
            builder.AppendLine("    return message");
            builder.Dedent(2);
            return builder.ToString();
        }

        private string CompileDecodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            int i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileDecodeField(field.Type, $"field{i}"));
                builder.AppendLine("");
                i++;
            }
            var args = string.Join(", ", definition.Fields.Select((field, i) => $"{field.Name}=field{i}"));
            builder.AppendLine($"return {definition.Name}({args})");
            return builder.ToString();
        }

        private string CompileDecodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine("length = reader.read_message_length()");
            builder.AppendLine("end = reader.index + 1 + length");
            builder.AppendLine("discriminator = reader.read_byte()");
            builder.AppendLine("if discriminator == 1:");
            builder.AppendLine($"    return {definition.ClassName()}.from{definition.Branches.ElementAt(0).Definition.ClassName()}({definition.Branches.ElementAt(0).Definition.ClassName()}.read_from(reader))");
            foreach (var branch in definition.Branches.Skip(1))
            {
                builder.AppendLine($"elif discriminator == {branch.Discriminator}:");
                builder.AppendLine($"    return {definition.ClassName()}.from{branch.Definition.ClassName()}({branch.Definition.ClassName()}.read_from(reader))");
            }
            builder.AppendLine("else:");
            builder.AppendLine("    reader.index = end");
            builder.AppendLine($"    raise Exception(\"Unrecognized discriminator while decoding {definition.ClassName()}\")");
            return builder.ToString();
        }

        private string ReadBaseType(BaseType baseType)
        {
            return baseType switch
            {
                BaseType.Bool => "reader.read_bool()",
                BaseType.Byte => "reader.read_byte()",
                BaseType.UInt16 => "reader.read_uint16()",
                BaseType.Int16 => "reader.read_int16()",
                BaseType.UInt32 => "reader.read_uint32()",
                BaseType.Int32 => "reader.read_int32()",
                BaseType.UInt64 => "reader.read_uint64()",
                BaseType.Int64 => "reader.read_int64()",
                BaseType.Float32 => "reader.read_float32()",
                BaseType.Float64 => "reader.read_float64()",
                BaseType.String => "reader.read_string()",
                BaseType.Guid => "reader.read_guid()",
                BaseType.Date => "reader.read_date()",
                _ => throw new ArgumentOutOfRangeException(baseType.ToString())
            };
        }

        private string CompileDecodeField(TypeBase type, string target, int depth = 0)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', depth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = reader.read_bytes()",
                ArrayType at =>
                    $"length{depth} = reader.read_uint32()" + nl +
                    $"{target} = []" + nl +
                    $"for {i} in range(length{depth}):" + nl +
                    $"{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1)}" + nl +
                    $"{tab}{target}.append(x{depth})" + nl,
                MapType mt =>
                    $"length{depth} = reader.read_uint32()" + nl +
                    $"{target} = {{}}" + nl +
                    $"for {i} in range(length{depth}):" + nl +
                    $"{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1)}" + nl +
                    $"{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1)}" + nl +
                    $"{tab}{target}[k{depth}] = v{depth}" + nl,
                ScalarType st => $"{target} = {ReadBaseType(st.BaseType)}",
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    $"{target} = {dt.Name}({ReadBaseType(ed.BaseType)})",
                DefinedType dt => $"{target} = {dt.Name}.read_from(reader)",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        /// Generate a Python type name for the given <see cref="TypeBase"/>.
        /// </summary>
        /// <param name="type">The field type to generate code for.</param>
        /// <returns>The Python type name.</returns>
        private string TypeName(in TypeBase type)
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "bool",
                        BaseType.Byte or BaseType.UInt16 or BaseType.Int16 or BaseType.UInt32 or BaseType.Int32 or BaseType.UInt64 or BaseType.Int64 => "int",
                        BaseType.Float32 or BaseType.Float64 => "float",
                        BaseType.String => "str",
                        BaseType.Guid => "UUID",
                        BaseType.Date => "datetime",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                case ArrayType at when at.IsBytes():
                    return "bytearray";
                case ArrayType at:
                    return $"list[{TypeName(at.MemberType)}]";
                case MapType mt:
                    return $"dict[{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}]";
                case DefinedType dt:
                    var isEnum = Schema.Definitions[dt.Name] is EnumDefinition;
                    return dt.Name;
            }
            throw new InvalidOperationException($"GetTypeName: {type}");
        }

        private static string EscapeStringLiteral(string value)
        {
            return $@"""{value.EscapeString()}""";
        }

        private string EmitLiteral(Literal literal)
        {
            return literal switch
            {
                BoolLiteral bl => bl.Value ? "True" : "False",
                IntegerLiteral il => il.Value,
                FloatLiteral fl when fl.Value == "inf" => $"math.inf",
                FloatLiteral fl when fl.Value == "-inf" => $"-math.inf",
                FloatLiteral fl when fl.Value == "nan" => $"math.nan",
                FloatLiteral fl => fl.Value,
                StringLiteral sl => EscapeStringLiteral(sl.Value),
                GuidLiteral gl => EscapeStringLiteral(gl.Value.ToString("D")),
                _ => throw new ArgumentOutOfRangeException(literal.ToString()),
            };
        }

        /// <summary>
        /// Generate code for a Bebop schema.
        /// </summary>
        /// <returns>The generated code.</returns>
        public override ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config,
            CancellationToken cancellationToken = default)
        {
            var artifacts = new List<Artifact>();
            Schema = schema;
            Config = config;
            var builder = new IndentedStringBuilder();
            builder.AppendLine("from enum import Enum");
            builder.AppendLine("from python_bebop import BebopWriter, BebopReader, UnionType, UnionDefinition");
            builder.AppendLine("from uuid import UUID");
            builder.AppendLine("import math");
            builder.AppendLine("import json");
            builder.AppendLine("from datetime import datetime");
            builder.AppendLine("");

            foreach (var definition in Schema.Definitions.Values)
            {
                switch (definition)
                {
                    case EnumDefinition ed:
                        builder.AppendLine($"class {ed.Name}(Enum):");
                        builder.Indent(indentStep);
                        if (!string.IsNullOrWhiteSpace(definition.Documentation))
                        {
                            builder.AppendLine(FormatDocumentation(definition.Documentation, null));
                            builder.AppendLine();
                        }

                        for (var i = 0; i < ed.Members.Count; i++)
                        {
                            var field = ed.Members.ElementAt(i);
                            builder.AppendLine($"{field.Name.ToUpper()} = {field.ConstantValue}");
                            if (!string.IsNullOrWhiteSpace(field.Documentation))
                            {
                                var deprecatedReason =
                                    field.DeprecatedDecorator?.TryGetValue("reason", out var reason) ?? false
                                        ? reason
                                        : null;
                                builder.AppendLine(FormatDocumentation(field.Documentation, deprecatedReason));
                                builder.AppendLine("");
                            }
                        }

                        builder.AppendLine("");
                        builder.Dedent(indentStep);
                        break;
                    case RecordDefinition rd:
                        if (rd is FieldsDefinition fd)
                        {
                            builder.AppendLine($"class {fd.Name}:");
                            builder.Indent(indentStep);
                            if (!string.IsNullOrWhiteSpace(definition.Documentation))
                            {
                                builder.AppendLine(FormatDocumentation(definition.Documentation, null));
                                builder.AppendLine();
                            }

                            var isImmutableStruct = rd is StructDefinition sd && !sd.IsMutable;
                            var fieldPrepend = isImmutableStruct ? "_" : "";
                            for (var i = 0; i < fd.Fields.Count; i++)
                            {
                                var field = fd.Fields.ElementAt(i);
                                var type = TypeName(field.Type);
                                builder.AppendLine($"{fieldPrepend}{field.Name}: {type}");
                                if (!string.IsNullOrWhiteSpace(field.Documentation))
                                {
                                    builder.AppendLine(FormatDocumentation(field.Documentation,
                                        field.DeprecatedDecorator?.Arguments?["reason"]));
                                }

                                builder.AppendLine();
                            }

                            if (rd.OpcodeDecorator != null)
                            {
                                builder.AppendLine($"opcode = {rd.OpcodeDecorator.Arguments["fourcc"]}");
                                builder.AppendLine("");
                            }

                            builder.AppendLine("");
                            if (!(fd is MessageDefinition))
                            {
                                List<string> fields = new List<string>();
                                foreach (var field in fd.Fields)
                                {
                                    fields.Add($" {field.Name}: {TypeName(field.Type)}");
                                }

                                if (fields.Count != 0)
                                {
                                    builder.Append("def __init__(self, ");
                                    builder.Append(string.Join(",", fields));
                                    builder.AppendLine("):");
                                    builder.Indent(indentStep);
                                    builder.AppendLine("self.encode = self._encode");
                                    foreach (var field in fd.Fields)
                                    {
                                        builder.AppendLine($"self.{fieldPrepend}{field.Name} = {field.Name}");
                                    }

                                    builder.Dedent(indentStep);
                                }
                                else
                                {
                                    builder.AppendLine("def __init__(self):");
                                    builder.AppendLine("   self.encode = self._encode");
                                }

                                builder.AppendLine();
                            }
                            else
                            {
                                builder.CodeBlock("def __init__(self):", indentStep, () =>
                                {
                                    builder.AppendLine("self.encode = self._encode");
                                }, open: string.Empty, close: string.Empty);
                            }

                            if (isImmutableStruct)
                            {
                                for (var i = 0; i < fd.Fields.Count; i++)
                                {
                                    var field = fd.Fields.ElementAt(i);
                                    builder.AppendLine("@property");
                                    builder.CodeBlock($"def {field.Name}(self):", indentStep, () =>
                                    {
                                        builder.AppendLine($"return self._{field.Name}");
                                    }, close: string.Empty, open: string.Empty);
                                }
                            }
                        }
                        else if (rd is UnionDefinition ud)
                        {
                            builder.CodeBlock($"class {ud.ClassName()}:", indentStep, () =>
                            {
                                builder.AppendLine();
                                if (!string.IsNullOrWhiteSpace(definition.Documentation))
                                {
                                    builder.AppendLine(FormatDocumentation(definition.Documentation, null));
                                }

                                if (rd.OpcodeDecorator is not null &&
                                    rd.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                                {
                                    builder.AppendLine($"opcode = {fourcc}");
                                    builder.AppendLine("");
                                }

                                builder.AppendLine($"data: UnionType");
                                builder.AppendLine();
                                builder.CodeBlock($"def __init__(self, data: UnionType):", indentStep, () =>
                                {
                                    builder.AppendLine("self.encode = self._encode");
                                    builder.AppendLine($"self.data = data");
                                }, open: string.Empty, close: string.Empty);
                                builder.AppendLine("@property");
                                builder.CodeBlock($"def discriminator(self):", indentStep, () =>
                                {
                                    builder.AppendLine($"return self.data.discriminator");
                                }, open: string.Empty, close: string.Empty);
                                builder.AppendLine("@property");
                                builder.CodeBlock($"def value(self):", indentStep, () =>
                                {
                                    builder.AppendLine($"return self.data.value");
                                }, open: string.Empty, close: string.Empty);
                                foreach (var b in ud.Branches)
                                {
                                    builder.AppendLine("@staticmethod");
                                    builder.CodeBlock($"def from{b.ClassName()}(value: {b.ClassName()}):", indentStep,
                                        () =>
                                        {
                                            builder.AppendLine(
                                                $"return {definition.ClassName()}(UnionDefinition({b.Discriminator}, value))");
                                        }, open: string.Empty, close: string.Empty);
                                    builder.CodeBlock($"def is{b.ClassName()}(self):", indentStep, () =>
                                    {
                                        builder.AppendLine($"return isinstance(self.value, {b.ClassName()})");
                                    }, open: string.Empty, close: string.Empty);
                                }
                            }, close: string.Empty, open: string.Empty);
                            builder.Indent(indentStep);
                        }

                        builder.AppendLine($"def _encode(self):");
                        builder.Indent(indentStep);
                        builder.AppendLine("\"\"\"Fake class method for allowing instance encode\"\"\"");
                        builder.AppendLine("writer = BebopWriter()");
                        builder.AppendLine($"{rd.Name}.encode_into(self, writer)");
                        builder.AppendLine("return writer.to_list()");
                        builder.Dedent(indentStep);
                        builder.AppendLine("");
                        builder.AppendLine("");

                        builder.AppendLine("@staticmethod");
                        builder.AppendLine($"def encode(message: \"{rd.Name}\"):");
                        builder.Indent(indentStep);
                        builder.AppendLine("writer = BebopWriter()");
                        builder.AppendLine($"{rd.Name}.encode_into(message, writer)");
                        builder.AppendLine("return writer.to_list()");
                        builder.Dedent(indentStep);
                        builder.AppendLine("");
                        builder.AppendLine("");

                        builder.AppendLine("@staticmethod");
                        builder.AppendLine($"def encode_into(message: \"{rd.Name}\", writer: BebopWriter):");
                        builder.Append(CompileEncode(rd));
                        builder.AppendLine("");
                        builder.AppendLine("");

                        builder.AppendLine("@classmethod");
                        builder.AppendLine($"def read_from(cls, reader: BebopReader):");
                        builder.Append(CompileDecode(rd));
                        builder.AppendLine("");
                        builder.AppendLine("");

                        builder.AppendLine("@staticmethod");
                        builder.AppendLine($"def decode(buffer) -> \"{rd.Name}\":");
                        builder.Indent(indentStep);
                        builder.AppendLine($"return {rd.Name}.read_from(BebopReader(buffer))");
                        builder.Dedent(indentStep);
                        builder.AppendLine("");
                        // representation
                        builder.CodeBlock($"def __repr__(self):", indentStep, () =>
                        {
                            builder.AppendLine(
                                $"return json.dumps(self, default=lambda o: o.value if isinstance(o, Enum) else dict(sorted(o.__dict__.items())) if hasattr(o, \"__dict__\") else str(o))");
                        }, open: string.Empty, close: string.Empty);
                        builder.AppendLine("");
                        builder.AppendLine("");

                        builder.Dedent(indentStep);
                        break;
                    case ConstDefinition cd:
                        builder.AppendLine($"{cd.Name} = {EmitLiteral(cd.Value)}");
                        builder.AppendLine("");
                        break;
                    case ServiceDefinition:
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported definition {definition}");
                }
            }

            artifacts.Add(new(config.OutFile, builder.Encode()));

            return ValueTask.FromResult(artifacts.ToArray());
        }

        public override string Alias { get => "py"; set => throw new NotImplementedException(); }
        public override string Name { get => "Python"; set => throw new NotImplementedException(); }
    }
}
