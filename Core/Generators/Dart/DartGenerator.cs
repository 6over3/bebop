using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;
using Core.Meta.Extensions;

namespace Core.Generators.Dart
{
    public class DartGenerator : BaseGenerator
    {
        const int indentStep = 2;

        public DartGenerator() : base() { }

        private string FormatDocumentation(string documentation, int spaces)
        {
            var builder = new IndentedStringBuilder();
            builder.Indent(spaces);
            foreach (var line in documentation.GetLines())
            {
                builder.AppendLine($"/// {line}");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Generate the body of the <c>encode</c> function for the given <see cref="FieldsDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated Dart <c>encode</c> function body.</returns>
        public string CompileEncode(FieldsDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileEncodeMessage(d),
                StructDefinition d => CompileEncodeStruct(d),
                _ => throw new InvalidOperationException($"invalid CompileEncode value: {definition}"),
            };
        }

        private string CompileEncodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"final pos = view.reserveMessageLength();");
            builder.AppendLine($"final start = view.length;");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedDecorator != null)
                {
                    continue;
                }
                builder.AppendLine($"final m_{field.Name} = message.{field.Name};");
                builder.AppendLine($"if (m_{field.Name} != null) {{");
                builder.AppendLine($"  view.writeByte({field.ConstantValue});");
                builder.AppendLine($"  {CompileEncodeField(field.Type, $"m_{field.Name}")}");
                builder.AppendLine($"}}");
            }
            builder.AppendLine("view.writeByte(0);");
            builder.AppendLine("final end = view.length;");
            builder.AppendLine("view.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileEncodeField(field.Type, $"message.{field.Name}"));
            }
            return builder.ToString();
        }

        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0, bool isEnum = false)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            var enumSuffix = isEnum ? ".value" : "";
            return type switch
            {
                ArrayType at when at.IsBytes() => $"view.writeBytes({target});",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}final length{depth} = {target}.length;" + nl +
                    $"{tab}view.writeUint32(length{depth});" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{CompileEncodeField(at.MemberType, $"{target}[{i}]", depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"view.writeUint32({target}.length);" + nl +
                    $"for (final e{depth} in {target}.entries) {{" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"e{depth}.key", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"e{depth}.value", depth + 1, indentDepth + 1)}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"view.writeBool({target}{enumSuffix});",
                    BaseType.Byte => $"view.writeByte({target}{enumSuffix});",
                    BaseType.UInt16 => $"view.writeUint16({target}{enumSuffix});",
                    BaseType.Int16 => $"view.writeInt16({target}{enumSuffix});",
                    BaseType.UInt32 => $"view.writeUint32({target}{enumSuffix});",
                    BaseType.Int32 => $"view.writeInt32({target}{enumSuffix});",
                    BaseType.UInt64 => $"view.writeUint64({target}{enumSuffix});",
                    BaseType.Int64 => $"view.writeInt64({target}{enumSuffix});",
                    BaseType.Float32 => $"view.writeFloat32({target}{enumSuffix});",
                    BaseType.Float64 => $"view.writeFloat64({target}{enumSuffix});",
                    BaseType.String => $"view.writeString({target}{enumSuffix});",
                    BaseType.Guid => $"view.writeGuid({target}{enumSuffix});",
                    BaseType.Date => $"view.writeDate({target}{enumSuffix});",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                },
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    CompileEncodeField(ed.ScalarType, target, depth, indentDepth, true),
                DefinedType dt => $"{dt.Name}.encodeInto({target}, view);",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="FieldsDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated Dart <c>decode</c> function body.</returns>
        public string CompileDecode(FieldsDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileDecodeMessage(d),
                StructDefinition d => CompileDecodeStruct(d),
                _ => throw new InvalidOperationException($"invalid CompileDecodevalue: {definition}"),
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
            builder.AppendLine($"var message = {definition.Name}();");
            builder.AppendLine("final length = view.readMessageLength();");
            builder.AppendLine("final end = view.index + length;");
            builder.AppendLine("while (true) {");
            builder.Indent(2);
            builder.AppendLine("switch (view.readByte()) {");
            builder.AppendLine("  case 0:");
            builder.AppendLine("    return message;");
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"  case {field.ConstantValue}:");
                builder.AppendLine($"    {CompileDecodeField(field.Type, $"message.{field.Name}")}");
                builder.AppendLine("    break;");
            }
            builder.AppendLine("  default:");
            builder.AppendLine("    view.index = end;");
            builder.AppendLine("    return message;");
            builder.AppendLine("}");
            builder.Dedent(2);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string CompileDecodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            int i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"{TypeName(field.Type)} field{i};");
                builder.AppendLine(CompileDecodeField(field.Type, $"field{i}"));
                i++;
            }
            var args = string.Join(", ", definition.Fields.Select((field, i) => $"{field.Name}: field{i}"));
            builder.AppendLine($"return {definition.Name}({args});");
            return builder.ToString();
        }

        private string ReadBaseType(BaseType baseType) {
            return baseType switch
            {
                BaseType.Bool => "view.readBool()",
                BaseType.Byte => "view.readByte()",
                BaseType.UInt16 => "view.readUint16()",
                BaseType.Int16 => "view.readInt16()",
                BaseType.UInt32 => "view.readUint32()",
                BaseType.Int32 => "view.readInt32()",
                BaseType.UInt64 => "view.readUint64()",
                BaseType.Int64 => "view.readInt64()",
                BaseType.Float32 => "view.readFloat32()",
                BaseType.Float64 => "view.readFloat64()",
                BaseType.String => "view.readString()",
                BaseType.Guid => "view.readGuid()",
                BaseType.Date => "view.readDate()",
                _ => throw new ArgumentOutOfRangeException(baseType.ToString())
            };
        }

        private string CompileDecodeField(TypeBase type, string target, int depth = 0)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', depth * 2 * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = view.readBytes();",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}var length{depth} = view.readUint32();" + nl +
                    $"{tab}var array{depth} = <{TypeName(at.MemberType)}>[];" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(at.MemberType)} x{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}array{depth}.add(x{depth});" + nl +
                    $"{tab}}}" + nl +
                    $"{tab}{target} = array{depth};" + nl +
                    $"}}",
                MapType mt =>
                    $"{{" + nl +
                    $"{tab}var length{depth} = view.readUint32();" + nl +
                    $"{tab}var map{depth} = {TypeName(mt)}();" + nl +
                    $"{tab}for (var {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(mt.KeyType)} k{depth};" + nl +
                    $"{tab}{tab}{TypeName(mt.ValueType)} v{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}map{depth}[k{depth}] = v{depth};" + nl +
                    $"{tab}}}" + nl +
                    $"{tab}{target} = map{depth};" + nl +
                    $"}}",
                ScalarType st => $"{target} = {ReadBaseType(st.BaseType)};",
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    $"{target} = {dt.Name}.fromRawValue({ReadBaseType(ed.BaseType)});",
                DefinedType dt => $"{target} = {dt.Name}.readFrom(view);",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        /// Generate a Dart type name for the given <see cref="TypeBase"/>.
        /// </summary>
        /// <param name="type">The field type to generate code for.</param>
        /// <returns>The Dart type name.</returns>
        private string TypeName(in TypeBase type)
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "bool",
                        BaseType.Byte or BaseType.UInt16 or BaseType.Int16 or BaseType.UInt32 or BaseType.Int32 or BaseType.UInt64 or BaseType.Int64 => "int",
                        BaseType.Float32 or BaseType.Float64 => "double",
                        BaseType.String or BaseType.Guid => "String",
                        BaseType.Date => "DateTime",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                case ArrayType at when at.IsBytes():
                    return "Uint8List";
                case ArrayType at:
                    return $"List<{TypeName(at.MemberType)}>";
                case MapType mt:
                    return $"Map<{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}>";
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
                BoolLiteral bl => bl.Value ? "true" : "false",
                IntegerLiteral il => il.Value,
                FloatLiteral fl when fl.Value == "inf" => $"{TypeName(literal.Type)}.infinity",
                FloatLiteral fl when fl.Value == "-inf" => $"{TypeName(literal.Type)}.negativeInfinity",
                FloatLiteral fl when fl.Value == "nan" => $"{TypeName(literal.Type)}.nan",
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
        public override ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config, CancellationToken cancellationToken = default)
        {
            var artifacts = new List<Artifact>();
            Schema = schema;
            Config = config;
            var builder = new IndentedStringBuilder();
            builder.AppendLine("import 'dart:typed_data';");
            builder.AppendLine("import 'package:meta/meta.dart';");
            builder.AppendLine("import 'package:bebop_dart/bebop_dart.dart';");
            builder.AppendLine("");

            foreach (var definition in Schema.Definitions.Values)
            {
                if (!string.IsNullOrWhiteSpace(definition.Documentation))
                {
                    builder.AppendLine(FormatDocumentation(definition.Documentation, 2));
                }
                switch (definition)
                {
                    case EnumDefinition ed:
                        builder.AppendLine($"class {ed.Name} {{");
                        builder.AppendLine($"  final int value;");
                        builder.AppendLine($"  const {ed.Name}.fromRawValue(this.value);");
                        builder.AppendLine($"  @override bool operator ==(o) => o is {ed.Name} && o.value == value;");
                        for (var i = 0; i < ed.Members.Count; i++)
                        {
                            var field = ed.Members.ElementAt(i);
                            if (!string.IsNullOrWhiteSpace(field.Documentation))
                            {
                                builder.AppendLine(FormatDocumentation(field.Documentation, 2));
                            }
                            if (field.DeprecatedDecorator is not null && field.DeprecatedDecorator.TryGetValue("reason", out var reason))
                            {
                                builder.AppendLine($"  /// @deprecated {reason}");
                            }
                            builder.AppendLine($"  static const {field.Name} = {ed.Name}.fromRawValue({field.ConstantValue});");
                        }
                        builder.AppendLine($"}}");
                        break;
                    case FieldsDefinition fd:
                        builder.AppendLine($"class {fd.Name} {{");
                        for (var i = 0; i < fd.Fields.Count; i++)
                        {
                            var field = fd.Fields.ElementAt(i);
                            var type = TypeName(field.Type);
                            if (!string.IsNullOrWhiteSpace(field.Documentation))
                            {
                                builder.AppendLine(FormatDocumentation(field.Documentation, 2));
                            }
                            if (field.DeprecatedDecorator is not null && field.DeprecatedDecorator.TryGetValue("reason", out var reason))
                            {
                                builder.AppendLine($"  /// @deprecated {reason}");
                            }
                            var final = fd is StructDefinition { IsMutable: false } ? "final " : "";
                            var optional = fd is MessageDefinition ? "?" : "";
                            builder.AppendLine($"  {final}{type}{optional} {field.Name};");
                        }
                        if (fd is MessageDefinition)
                        {
                            builder.AppendLine($"  {fd.Name}();");
                        }
                        else
                        {
                            builder.AppendLine($"  {(fd is StructDefinition { IsMutable: false } ? "const " : "")}{fd.Name}({{");
                            foreach (var field in fd.Fields)
                            {
                                builder.AppendLine($"    required this.{field.Name},");
                            }
                            builder.AppendLine("  });");
                        }
                        builder.AppendLine("");
                        if (fd.OpcodeDecorator is not null && fd.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                        {
                            builder.AppendLine($"  static const int opcode = {fourcc};");
                            builder.AppendLine("");
                        }
                        builder.AppendLine($"  static Uint8List encode({fd.Name} message) {{");
                        builder.AppendLine("    final writer = BebopWriter();");
                        builder.AppendLine($"    {fd.Name}.encodeInto(message, writer);");
                        builder.AppendLine("    return writer.toList();");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static int encodeInto({fd.Name} message, BebopWriter view) {{");
                        builder.AppendLine("    final before = view.length;");
                        builder.Append(CompileEncode(fd));
                        builder.AppendLine("    final after = view.length;");
                        builder.AppendLine("    return after - before;");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static {fd.Name} decode(Uint8List buffer) => {fd.Name}.readFrom(BebopReader(buffer));");
                        builder.AppendLine("");
                        builder.AppendLine($"  static {fd.Name} readFrom(BebopReader view) {{");
                        builder.Append(CompileDecode(fd));
                        builder.AppendLine("  }");
                        builder.AppendLine("}");
                        builder.AppendLine("");
                        break;
                    case ConstDefinition cd:
                        builder.AppendLine($"final {TypeName(cd.Value.Type)} {cd.Name} = {EmitLiteral(cd.Value)};");
                        builder.AppendLine("");
                        break;
                    case ServiceDefinition:
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported definition {definition}");
                }
            }

            artifacts.Add(new Artifact(config.OutFile, builder.Encode()));
            return ValueTask.FromResult(artifacts.ToArray());
        }


        public override string Alias { get => "dart"; set => throw new NotImplementedException(); }
        public override string Name { get => "Dart"; set => throw new NotImplementedException(); }
    }
}
