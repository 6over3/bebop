using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;
using Core.Meta.Extensions;

namespace Core.Generators.CPlusPlus
{
    public class CPlusPlusGenerator : BaseGenerator
    {
        const int indentStep = 2;

        public CPlusPlusGenerator() : base() { }

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
        /// Generate the body of the <c>encode</c> function for the given <see cref="RecordDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>encode</c> function body.</returns>
        public string CompileEncode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileEncodeMessage(d),
                StructDefinition d => CompileEncodeStruct(d),
                UnionDefinition d => CompileEncodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileEncode kind: {definition}"),
            };
        }

        private string CompileEncodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"const auto pos = writer.reserveMessageLength();");
            builder.AppendLine($"const auto start = writer.length();");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedDecorator != null)
                {
                    continue;
                }
                builder.AppendLine($"if (message.{field.Name}.has_value()) {{");
                builder.AppendLine($"  writer.writeByte({field.ConstantValue});");
                builder.AppendLine($"  {CompileEncodeField(field.Type, $"message.{field.Name}.value()", 0, 1)}");
                builder.AppendLine($"}}");
            }
            builder.AppendLine("writer.writeByte(0);");
            builder.AppendLine("const auto end = writer.length();");
            builder.AppendLine("writer.fillMessageLength(pos, end - start);");
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

        private string CompileEncodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine($"const auto pos = writer.reserveMessageLength();");
            builder.AppendLine($"const uint8_t discriminator = message.variant.index() + 1;");
            builder.AppendLine($"writer.writeByte(discriminator);");
            builder.AppendLine($"const auto start = writer.length();");
            builder.AppendLine($"switch (discriminator) {{");
            int i = 0;
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($" case {branch.Discriminator}:");
                builder.AppendLine($"  {branch.Definition.Name}::encodeInto(std::get<{i++}>(message.variant), writer);");
                builder.AppendLine($"  break;");
            }
            builder.AppendLine($"}}");
            builder.AppendLine("const auto end = writer.length();");
            builder.AppendLine("writer.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"writer.writeBytes({target});",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = {target}.size();" + nl +
                    $"{tab}writer.writeUint32(length{depth});" + nl +
                    $"{tab}for (const auto& {i} : {target}) {{" + nl +
                    $"{tab}{tab}{CompileEncodeField(at.MemberType, i, depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"writer.writeUint32({target}.size());" + nl +
                    $"for (const auto& e{depth} : {target}) {{" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"e{depth}.first", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"e{depth}.second", depth + 1, indentDepth + 1)}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"writer.writeBool({target});",
                    BaseType.Byte => $"writer.writeByte({target});",
                    BaseType.UInt16 => $"writer.writeUint16({target});",
                    BaseType.Int16 => $"writer.writeInt16({target});",
                    BaseType.UInt32 => $"writer.writeUint32({target});",
                    BaseType.Int32 => $"writer.writeInt32({target});",
                    BaseType.UInt64 => $"writer.writeUint64({target});",
                    BaseType.Int64 => $"writer.writeInt64({target});",
                    BaseType.Float32 => $"writer.writeFloat32({target});",
                    BaseType.Float64 => $"writer.writeFloat64({target});",
                    BaseType.String => $"writer.writeString({target});",
                    BaseType.Guid => $"writer.writeGuid({target});",
                    BaseType.Date => $"writer.writeDate({target});",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                },
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    CompileEncodeField(ed.ScalarType, $"static_cast<{TypeName(ed.ScalarType)}>({target})", depth, indentDepth),
                DefinedType dt => $"{dt.Name}::encodeInto({target}, writer);",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="RecordDefinition"/>.
        /// </summary>
        /// <param name="definition">The definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>decode</c> function body.</returns>
        public string CompileDecode(RecordDefinition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileDecodeMessage(d),
                StructDefinition d => CompileDecodeStruct(d),
                UnionDefinition d => CompileDecodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileDecode kind: {definition}"),
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="MessageDefinition"/>.
        /// </summary>
        /// <param name="definition">The message definition to generate code for.</param>
        /// <returns>The generated CPlusPlus <c>decode</c> function body.</returns>
        private string CompileDecodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine("const auto length = reader.readLengthPrefix();");
            builder.AppendLine("const auto end = reader.pointer() + length;");
            builder.AppendLine("while (true) {");
            builder.Indent(2);
            builder.AppendLine("switch (reader.readByte()) {");
            builder.AppendLine("  case 0:");
            builder.AppendLine("    return reader.bytesRead();");
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"  case {field.ConstantValue}:");
                builder.AppendLine($"    {CompileDecodeField(field.Type, $"target.{field.Name}", 0, 2, true)}");
                builder.AppendLine("    break;");
            }
            builder.AppendLine("  default:");
            builder.AppendLine("    reader.seek(end);");
            builder.AppendLine("    return reader.bytesRead();");
            builder.AppendLine("}");
            builder.Dedent(2);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string CompileDecodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileDecodeField(field.Type, $"target.{field.Name}"));
            }
            return builder.ToString();
        }

        private string CompileDecodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder(4);
            builder.AppendLine("const auto length = reader.readLengthPrefix();");
            builder.AppendLine("const auto end = reader.pointer() + length + 1;");
            builder.AppendLine("switch (reader.readByte()) {");
            int i = 0;
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($"  case {branch.Discriminator}:");
                builder.AppendLine($"    target.variant.emplace<{i}>();");
                builder.AppendLine($"    {branch.Definition.Name}::decodeInto(reader, std::get<{i}>(target.variant));");
                builder.AppendLine("    break;");
                i++;
            }
            builder.AppendLine("  default:");
            builder.AppendLine("    reader.seek(end); // do nothing?");
            builder.AppendLine("    return reader.bytesRead();");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string ReadBaseType(BaseType baseType)
        {
            return baseType switch
            {
                BaseType.Bool => "reader.readBool()",
                BaseType.Byte => "reader.readByte()",
                BaseType.UInt32 => "reader.readUint32()",
                BaseType.Int32 => "reader.readInt32()",
                BaseType.Float32 => "reader.readFloat32()",
                BaseType.String => "reader.readString()",
                BaseType.Guid => "reader.readGuid()",
                BaseType.UInt16 => "reader.readUint16()",
                BaseType.Int16 => "reader.readInt16()",
                BaseType.UInt64 => "reader.readUint64()",
                BaseType.Int64 => "reader.readInt64()",
                BaseType.Float64 => "reader.readFloat64()",
                BaseType.Date => "reader.readDate()",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private string CompileDecodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0, bool isOptional = false)
        {
            var tab = new string(' ', indentStep);
            var nl = "\n" + new string(' ', indentDepth * indentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            var dot = isOptional ? "->" : ".";
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = reader.readBytes();",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = reader.readUint32();" + nl +
                    $"{tab}{target} = {TypeName(at)}();" + nl +
                    $"{tab}{target}{dot}reserve(length{depth});" + nl +
                    $"{tab}for (size_t {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(at.MemberType)} x{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}{tab}{target}{dot}push_back(x{depth});" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"{{" + nl +
                    $"{tab}const auto length{depth} = reader.readUint32();" + nl +
                    $"{tab}{target} = {TypeName(mt)}();" + nl +
                    $"{tab}for (size_t {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{TypeName(mt.KeyType)} k{depth};" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}{tab}{TypeName(mt.ValueType)}& v{depth} = {target}{dot}operator[](k{depth});" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1, indentDepth + 2)}" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                ScalarType st => $"{target} = {ReadBaseType(st.BaseType)};",
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    $"{target} = static_cast<{dt.Name}>({ReadBaseType(ed.BaseType)});",
                DefinedType dt when isOptional => $"{target}.emplace({dt.Name}::decode(reader));",
                DefinedType dt => $"{dt.Name}::decodeInto(reader, {target});",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        /// Generate a CPlusPlus type name for the given <see cref="TypeBase"/>.
        /// </summary>
        /// <param name="type">The field type to generate code for.</param>
        /// <returns>The CPlusPlus type name.</returns>
        private string TypeName(in TypeBase type)
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "bool",
                        BaseType.Byte => "uint8_t",
                        BaseType.UInt16 => "uint16_t",
                        BaseType.Int16 => "int16_t",
                        BaseType.UInt32 => "uint32_t",
                        BaseType.Int32 => "int32_t",
                        BaseType.UInt64 => "uint64_t",
                        BaseType.Int64 => "int64_t",
                        BaseType.Float32 => "float",
                        BaseType.Float64 => "double",
                        BaseType.String => "std::string",
                        BaseType.Guid => "::bebop::Guid",
                        BaseType.Date => "::bebop::TickDuration",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                // case ArrayType at when at.IsBytes():
                //     return "std::vector<uint8_t>";
                case ArrayType at:
                    return $"std::vector<{TypeName(at.MemberType)}>";
                case MapType mt:
                    return $"std::map<{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}>";
                case DefinedType dt:
                    return dt.Name;
            }
            throw new InvalidOperationException($"GetTypeName: {type}");
        }

        private static string Optional(string type) => "std::optional<" + type + ">";

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
                FloatLiteral fl when fl.Value == "inf" => $"std::numeric_limits<{TypeName(literal.Type)}>::infinity()",
                FloatLiteral fl when fl.Value == "-inf" => $"-std::numeric_limits<{TypeName(literal.Type)}>::infinity()",
                FloatLiteral fl when fl.Value == "nan" => $"std::numeric_limits<{TypeName(literal.Type)}>::quiet_NaN()",
                FloatLiteral fl => fl.Value,
                StringLiteral sl => EscapeStringLiteral(sl.Value),
                GuidLiteral gl => $"::bebop::Guid::fromString(\"{gl.Value}\")",
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
            if (Config.EmitNotice)
            {
                builder.AppendLine(GeneratorUtils.GetXmlAutoGeneratedNotice());
            }
            builder.AppendLine("#pragma once");
            builder.AppendLine("#include <cstddef>");
            builder.AppendLine("#include <cstdint>");
            builder.AppendLine("#include <map>");
            builder.AppendLine("#include <memory>");
            builder.AppendLine("#include <optional>");
            builder.AppendLine("#include <string>");
            builder.AppendLine("#include <variant>");
            builder.AppendLine("#include <vector>");
            builder.AppendLine("#include \"bebop.hpp\"");
            builder.AppendLine("");

            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.AppendLine($"namespace {Config.Namespace} {{");
                builder.AppendLine("");
            }

            foreach (var definition in Schema.SortedDefinitions())
            {
                if (!string.IsNullOrWhiteSpace(definition.Documentation))
                {
                    builder.AppendLine(FormatDocumentation(definition.Documentation, 0));
                }
                switch (definition)
                {
                    case EnumDefinition ed:
                        builder.AppendLine($"enum class {definition.Name} : {TypeName(ed.ScalarType)} {{");
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
                            builder.AppendLine($"  {field.Name} = {field.ConstantValue},");
                        }
                        builder.AppendLine("};");
                        builder.AppendLine("");
                        break;
                    case RecordDefinition td:
                        builder.AppendLine($"struct {td.Name} {{");
                        builder.AppendLine($"  static const size_t minimalEncodedSize = {td.MinimalEncodedSize(Schema)};");
                        if (td.OpcodeDecorator is not null && td.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                        {
                            builder.AppendLine($"  static const uint32_t opcode = {fourcc};");
                            builder.AppendLine("");
                        }

                        if (td is FieldsDefinition fd)
                        {
                            var isMessage = fd is MessageDefinition;
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
                                builder.AppendLine($"  {(isMessage ? Optional(type) : type)} {field.Name};");
                            }
                            builder.AppendLine("");
                        }
                        else if (td is UnionDefinition ud)
                        {
                            var types = string.Join(", ", ud.Branches.Select(b => b.Definition.Name));
                            builder.AppendLine($"  std::variant<{types}> variant;");
                        }
                        else
                        {
                            throw new InvalidOperationException($"unsupported definition {td}");
                        }

                        builder.AppendLine($"  static size_t encodeInto(const {td.Name}& message, std::vector<uint8_t>& targetBuffer) {{");
                        builder.AppendLine("    ::bebop::Writer writer{targetBuffer};");
                        builder.AppendLine($"    return {td.Name}::encodeInto(message, writer);");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  template<typename T = ::bebop::Writer> static size_t encodeInto(const {td.Name}& message, T& writer) {{");
                        builder.AppendLine("    size_t before = writer.length();");
                        builder.Append(CompileEncode(td));
                        builder.AppendLine("    size_t after = writer.length();");
                        builder.AppendLine("    return after - before;");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  size_t encodeInto(std::vector<uint8_t>& targetBuffer) const {{ return {td.Name}::encodeInto(*this, targetBuffer); }}");
                        builder.AppendLine($"  size_t encodeInto(::bebop::Writer& writer) const {{ return {td.Name}::encodeInto(*this, writer); }}");
                        builder.AppendLine("");
                        builder.AppendLine($"  static {td.Name} decode(const uint8_t* sourceBuffer, size_t sourceBufferSize) {{");
                        builder.AppendLine($"    {td.Name} result;");
                        builder.AppendLine($"    {td.Name}::decodeInto(sourceBuffer, sourceBufferSize, result);");
                        builder.AppendLine($"    return result;");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static {td.Name} decode(const std::vector<uint8_t>& sourceBuffer) {{");
                        builder.AppendLine($"    return {td.Name}::decode(sourceBuffer.data(), sourceBuffer.size());");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static {td.Name} decode(::bebop::Reader& reader) {{");
                        builder.AppendLine($"    {td.Name} result;");
                        builder.AppendLine($"    {td.Name}::decodeInto(reader, result);");
                        builder.AppendLine($"    return result;");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static size_t decodeInto(const uint8_t* sourceBuffer, size_t sourceBufferSize, {td.Name}& target) {{");
                        builder.AppendLine("    ::bebop::Reader reader{sourceBuffer, sourceBufferSize};");
                        builder.AppendLine($"    return {td.Name}::decodeInto(reader, target);");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static size_t decodeInto(const std::vector<uint8_t>& sourceBuffer, {td.Name}& target) {{");
                        builder.AppendLine($"    return {td.Name}::decodeInto(sourceBuffer.data(), sourceBuffer.size(), target);");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine($"  static size_t decodeInto(::bebop::Reader& reader, {td.Name}& target) {{");
                        builder.Append(CompileDecode(td));
                        builder.AppendLine("    return reader.bytesRead();");
                        builder.AppendLine("  }");
                        builder.AppendLine("");
                        builder.AppendLine("  size_t byteCount() {");
                        builder.AppendLine("    ::bebop::ByteCounter counter{};");
                        builder.AppendLine($"    {td.Name}::encodeInto<::bebop::ByteCounter>(*this, counter);");
                        builder.AppendLine("    return counter.length();");
                        builder.AppendLine("  }");
                        builder.AppendLine("};");
                        builder.AppendLine("");
                        break;
                    case ConstDefinition cd:
                        builder.AppendLine($"const {TypeName(cd.Value.Type)} {cd.Name} = {EmitLiteral(cd.Value)};");
                        builder.AppendLine("");
                        break;
                    case ServiceDefinition:
                        break;
                    default:
                        throw new InvalidOperationException($"unsupported definition {definition}");
                }
            }

            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.AppendLine($"}} // namespace {Config.Namespace}");
                builder.AppendLine("");
            }

            artifacts.Add(new Artifact(config.OutFile, builder.Encode()));
            if (GetRuntime() is { } runtime)
            {
                artifacts.Add(runtime);
            }

            return ValueTask.FromResult(artifacts.ToArray());
        }

        private const string _bytePattern = @"(?:"")?(\b(1?[0-9]{1,2}|2[0-4][0-9]|25[0-5])\b)(?:"")?";
        private static readonly Regex _majorRegex = new Regex($@"(?<=BEBOPC_VER_MAJOR\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _minorRegex = new Regex($@"(?<=BEBOPC_VER_MINOR\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _patchRegex = new Regex($@"(?<=BEBOPC_VER_PATCH\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex _informationalRegex = new Regex($@"(?<=BEBOPC_VER_INFO\s){_bytePattern}", RegexOptions.Compiled | RegexOptions.Singleline);


        public Artifact? GetRuntime()
        {
            var assembly = Assembly.GetEntryAssembly()!;
            var runtime = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Equals("Compiler.bebop.hpp"));
            if (string.IsNullOrWhiteSpace(runtime)) return null;

            using var stream = assembly.GetManifestResourceStream(runtime)!;
            using var reader = new StreamReader(stream);
            var builder = new IndentedStringBuilder();
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is { } line)
                {
                    if (_majorRegex.IsMatch(line)) line = _majorRegex.Replace(line, DotEnv.Generated.Environment.Major.ToString());
                    if (_minorRegex.IsMatch(line)) line = _minorRegex.Replace(line, DotEnv.Generated.Environment.Minor.ToString());
                    if (_patchRegex.IsMatch(line)) line = _patchRegex.Replace(line, DotEnv.Generated.Environment.Patch.ToString());
                    if (_informationalRegex.IsMatch(line)) line = _informationalRegex.Replace(line, $"\"{DotEnv.Generated.Environment.Version}\"");
                    builder.AppendLine(line);

                }
            }
            return new Artifact("bebop.hpp", builder.Encode());
        }

        public override string Alias { get => "cpp"; set => throw new NotImplementedException(); }
        public override string Name { get => "C++"; set => throw new NotImplementedException(); }
    }
}
