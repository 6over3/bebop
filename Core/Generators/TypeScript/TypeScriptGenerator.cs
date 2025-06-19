using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;
using Core.Meta.Decorators;
using Core.Meta.Extensions;
using Core.Parser;

namespace Core.Generators.TypeScript
{
    public class TypeScriptGenerator : BaseGenerator
    {
        const int IndentStep = 2;

        public TypeScriptGenerator() : base() { }

        private static string FormatDocumentation(string documentation, SchemaDecorator? deprecatedDecorator,
            int spaces)
        {
            if (string.IsNullOrWhiteSpace(documentation) && deprecatedDecorator is null)
            {
                return string.Empty;
            }

            var builder = new IndentedStringBuilder();
            builder.Indent(spaces);
            builder.AppendLine("/**");
            builder.Indent(1);
            if (!string.IsNullOrWhiteSpace(documentation))
            {
                foreach (var line in documentation.GetLines())
                {
                    builder.AppendLine($"* {line}");
                }
            }

            if (deprecatedDecorator?.TryGetValue("reason", out var deprecationReason) == true)
            {
                builder.AppendLine($"* @deprecated {deprecationReason}");
            }

            builder.AppendLine("*/");
            return builder.ToString();
        }

        /// <summary>
        /// Formats a hexadecimal opcode value with underscores for better readability.
        /// Converts "0x68616579" to "0x68_61_65_79" format.
        /// </summary>
        private static string FormatOpcodeWithUnderscores(string opcodeValue)
        {
            if (string.IsNullOrEmpty(opcodeValue))
                return opcodeValue;

            // Remove the "0x" prefix if present
            var hexPart = opcodeValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? opcodeValue.Substring(2)
                : opcodeValue;

            // Insert underscores every 2 characters
            var formattedHex = new StringBuilder();
            for (int i = 0; i < hexPart.Length; i += 2)
            {
                if (i > 0)
                    formattedHex.Append('_');

                // Take 2 characters or remaining characters if less than 2
                var chunk = hexPart.Substring(i, Math.Min(2, hexPart.Length - i));
                formattedHex.Append(chunk);
            }

            return $"0x{formattedHex}";
        }

        /// <summary>
        /// Generate the body of the <c>encode</c> function for the given <see cref="Definition"/>.
        /// </summary>
        private string CompileEncode(Definition definition)
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
            var builder = new IndentedStringBuilder();
            builder.AppendLine($"const pos = view.reserveMessageLength();");
            builder.AppendLine($"const start = view.length;");
            foreach (var field in definition.Fields)
            {
                if (field.DeprecatedDecorator != null)
                {
                    continue;
                }

                builder.AppendLine($"if (record.{field.NameCamelCase} !== undefined) {{");
                builder.AppendLine($"  view.writeByte({field.ConstantValue});");
                builder.AppendLine($"  {CompileEncodeField(field.Type, $"record.{field.NameCamelCase}")}");
                builder.AppendLine($"}}");
            }

            builder.AppendLine("view.writeByte(0);");
            builder.AppendLine("const end = view.length;");
            builder.AppendLine("view.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            foreach (var field in definition.Fields)
            {
                builder.AppendLine(CompileEncodeField(field.Type, $"record.{field.NameCamelCase}"));
            }

            return builder.ToString();
        }

        private string CompileEncodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine($"const pos = view.reserveMessageLength();");
            builder.AppendLine($"const start = view.length + 1;");
            builder.AppendLine($"view.writeByte(record.tag);");
            builder.AppendLine($"switch (record.tag) {{");
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($"  case {branch.Discriminator}:");
                builder.AppendLine($"    {branch.ClassName()}.encodeInto(record.value, view);");
                builder.AppendLine($"    break;");
            }

            builder.AppendLine("}");
            builder.AppendLine("const end = view.length;");
            builder.AppendLine("view.fillMessageLength(pos, end - start);");
            return builder.ToString();
        }

        private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0)
        {
            var tab = new string(' ', IndentStep);
            var nl = "\n" + new string(' ', indentDepth * IndentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"view.writeBytes({target});",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const length{depth} = {target}.length;" + nl +
                    $"{tab}view.writeUint32(length{depth});" + nl +
                    $"{tab}for (let {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}{CompileEncodeField(at.MemberType, $"{target}[{i}]", depth + 1, indentDepth + 2)}" +
                    nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"view.writeUint32({target}.size);" + nl +
                    $"for (const [k{depth}, v{depth}] of {target}) {{" + nl +
                    $"{tab}{CompileEncodeField(mt.KeyType, $"k{depth}", depth + 1, indentDepth + 1)}" + nl +
                    $"{tab}{CompileEncodeField(mt.ValueType, $"v{depth}", depth + 1, indentDepth + 1)}" + nl +
                    $"}}",
                ScalarType st => st.BaseType switch
                {
                    BaseType.Bool => $"view.writeByte(Number({target}));",
                    BaseType.Byte => $"view.writeByte({target});",
                    BaseType.UInt16 => $"view.writeUint16({target});",
                    BaseType.Int16 => $"view.writeInt16({target});",
                    BaseType.UInt32 => $"view.writeUint32({target});",
                    BaseType.Int32 => $"view.writeInt32({target});",
                    BaseType.UInt64 => $"view.writeUint64({target});",
                    BaseType.Int64 => $"view.writeInt64({target});",
                    BaseType.Float32 => $"view.writeFloat32({target});",
                    BaseType.Float64 => $"view.writeFloat64({target});",
                    BaseType.String => $"view.writeString({target});",
                    BaseType.Guid => $"view.writeGuid({target});",
                    BaseType.Date => $"view.writeDate({target});",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                },
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    CompileEncodeField(ed.ScalarType, target, depth, indentDepth),
                DefinedType dt => $"{dt.ClassName}.encodeInto({target}, view);",
                _ => throw new InvalidOperationException($"CompileEncodeField: {type}")
            };
        }

        /// <summary>
        /// Generate the body of the <c>decode</c> function for the given <see cref="Definition"/>.
        /// </summary>
        private string CompileDecode(Definition definition)
        {
            return definition switch
            {
                MessageDefinition d => CompileDecodeMessage(d),
                StructDefinition d => CompileDecodeStruct(d),
                UnionDefinition d => CompileDecodeUnion(d),
                _ => throw new InvalidOperationException($"invalid CompileDecode value: {definition}"),
            };
        }

        private string CompileDecodeMessage(MessageDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine($"const message: {definition.ClassName()} = {{}};");
            builder.AppendLine("const length = view.readMessageLength();");
            builder.AppendLine("const end = view.index + length;");
            builder.AppendLine("while (true) {");
            builder.Indent(2);
            builder.AppendLine("switch (view.readByte()) {");
            builder.AppendLine("  case 0:");
            builder.AppendLine($"    return message;");
            builder.AppendLine("");
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"  case {field.ConstantValue}:");
                builder.AppendLine($"    {CompileDecodeField(field.Type, $"message.{field.NameCamelCase}")}");
                builder.AppendLine("    break;");
                builder.AppendLine("");
            }

            builder.AppendLine("  default:");
            builder.AppendLine("    view.index = end;");
            builder.AppendLine($"    return message;");
            builder.AppendLine("}");
            builder.Dedent(2);
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string CompileDecodeStruct(StructDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            int i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"let field{i}: {TypeName(field.Type)};");
                builder.AppendLine(CompileDecodeField(field.Type, $"field{i}"));
                i++;
            }

            builder.AppendLine($"return {{");
            i = 0;
            foreach (var field in definition.Fields)
            {
                builder.AppendLine($"  {field.NameCamelCase}: field{i},");
                i++;
            }

            builder.AppendLine("};");
            return builder.ToString();
        }

        private string CompileDecodeUnion(UnionDefinition definition)
        {
            var builder = new IndentedStringBuilder();
            builder.AppendLine("const length = view.readMessageLength();");
            builder.AppendLine("const end = view.index + 1 + length;");
            builder.AppendLine("const tag = view.readByte();");
            builder.AppendLine("switch (tag) {");
            foreach (var branch in definition.Branches)
            {
                builder.AppendLine($"  case {branch.Discriminator}:");
                builder.AppendLine(
                    $"    return {{ tag: {branch.Discriminator}, value: {branch.Definition.ClassName()}.readFrom(view) }};");
            }

            builder.AppendLine("  default:");
            builder.AppendLine("    view.index = end;");
            builder.AppendLine($"    throw new BebopRuntimeError(`Unknown union discriminator: ${{tag}}`);");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private string ReadBaseType(BaseType baseType)
        {
            return baseType switch
            {
                BaseType.Bool => "!!view.readByte()",
                BaseType.Byte => "view.readByte()",
                BaseType.UInt32 => "view.readUint32()",
                BaseType.Int32 => "view.readInt32()",
                BaseType.Float32 => "view.readFloat32()",
                BaseType.String => "view.readString()",
                BaseType.Guid => "view.readGuid()",
                BaseType.UInt16 => "view.readUint16()",
                BaseType.Int16 => "view.readInt16()",
                BaseType.UInt64 => "view.readUint64()",
                BaseType.Int64 => "view.readInt64()",
                BaseType.Float64 => "view.readFloat64()",
                BaseType.Date => "view.readDate()",
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private string CompileDecodeField(TypeBase type, string target, int depth = 0)
        {
            var tab = new string(' ', IndentStep);
            var nl = "\n" + new string(' ', depth * 2 * IndentStep);
            var i = GeneratorUtils.LoopVariable(depth);
            return type switch
            {
                ArrayType at when at.IsBytes() => $"{target} = view.readBytes();",
                ArrayType at =>
                    $"{{" + nl +
                    $"{tab}const length{depth} = view.readUint32();" + nl +
                    $"{tab}{target} = [];" + nl +
                    $"{tab}for (let {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}let x{depth}: {TypeName(at.MemberType)};" + nl +
                    $"{tab}{tab}{CompileDecodeField(at.MemberType, $"x{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{target}[{i}] = x{depth};" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                MapType mt =>
                    $"{{" + nl +
                    $"{tab}const length{depth} = view.readUint32();" + nl +
                    $"{tab}{target} = new Map();" + nl +
                    $"{tab}for (let {i} = 0; {i} < length{depth}; {i}++) {{" + nl +
                    $"{tab}{tab}let k{depth}: {TypeName(mt.KeyType)};" + nl +
                    $"{tab}{tab}let v{depth}: {TypeName(mt.ValueType)};" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.KeyType, $"k{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{CompileDecodeField(mt.ValueType, $"v{depth}", depth + 1)}" + nl +
                    $"{tab}{tab}{target}.set(k{depth}, v{depth});" + nl +
                    $"{tab}}}" + nl +
                    $"}}",
                ScalarType st => $"{target} = {ReadBaseType(st.BaseType)};",
                DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                    $"{target} = {ReadBaseType(ed.BaseType)};",
                DefinedType dt => $"{target} = {dt.ClassName}.readFrom(view);",
                _ => throw new InvalidOperationException($"CompileDecodeField: {type}")
            };
        }

        /// <summary>
        /// Generate a TypeScript type name for the given <see cref="TypeBase"/>.
        /// </summary>
        private string TypeName(in TypeBase type)
        {
            switch (type)
            {
                case ScalarType st:
                    return st.BaseType switch
                    {
                        BaseType.Bool => "boolean",
                        BaseType.Byte or BaseType.UInt16 or BaseType.Int16 or BaseType.UInt32 or BaseType.Int32 or
                            BaseType.Float32 or BaseType.Float64 => "number",
                        BaseType.UInt64 or BaseType.Int64 => "bigint",
                        BaseType.Guid => "string",
                        BaseType.String => "string",
                        BaseType.Date => "Date",
                        _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                    };
                case ArrayType at when at.IsBytes():
                    return "Uint8Array";
                case ArrayType at:
                    return $"{TypeName(at.MemberType)}[]";
                case MapType mt:
                    return $"Map<{TypeName(mt.KeyType)}, {TypeName(mt.ValueType)}>";
                case DefinedType dt:
                    return dt.ClassName;
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
                IntegerLiteral { Type: ScalarType { Is64Bit: true } } il => $"BigInt(\"{il.Value}\")",
                IntegerLiteral il => il.Value,
                FloatLiteral { Value: "inf" } => "Number.POSITIVE_INFINITY",
                FloatLiteral { Value: "-inf" } => "Number.NEGATIVE_INFINITY",
                FloatLiteral { Value: "nan" } => "Number.NaN",
                FloatLiteral fl => fl.Value,
                StringLiteral sl => EscapeStringLiteral(sl.Value),
                GuidLiteral gl => EscapeStringLiteral(gl.Value.ToString("D")),
                _ => throw new ArgumentOutOfRangeException(literal.ToString()),
            };
        }

        /// <summary>
        /// Generate code for a Bebop schema.
        /// </summary>
        public override ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config,
            CancellationToken cancellationToken = default)
        {
            Schema = schema;
            Config = config;
            var builder = new IndentedStringBuilder();
            var artifacts = new List<Artifact>();

            if (Config.EmitNotice)
            {
                builder.AppendLine(GeneratorUtils.GetXmlAutoGeneratedNotice());
            }

            builder.AppendLine("import { BebopView, BebopRuntimeError, BebopRecord } from \"bebop\";");
            if (Schema.Definitions.Values.OfType<ServiceDefinition>().Any() &&
                Config.Services is not TempoServices.None)
            {
                builder.AppendLine("import { Metadata, MethodType } from \"@tempojs/common\";");
                if (Config.Services is TempoServices.Client or TempoServices.Both)
                {
                    builder.AppendLine("import {  BaseClient, MethodInfo, CallOptions } from \"@tempojs/client\";");
                }

                if (Config.Services is TempoServices.Server or TempoServices.Both)
                {
                    builder.AppendLine(
                        "import { ServiceRegistry, BaseService, ServerContext, BebopMethodAny, BebopMethod } from \"@tempojs/server\";");
                }
            }

            builder.AppendLine("");

            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.AppendLine($"export namespace {Config.Namespace} {{");
                builder.Indent(2);
            }

            if (Config.EmitBinarySchema)
            {
                builder.AppendLine(Schema.ToBinary()
                    .ConvertToString("export const BEBOP_SCHEMA = new Uint8Array ([", "]);"));
            }

            foreach (var definition in Schema.Definitions.Values)
            {
                builder.AppendLine(FormatDocumentation(definition.Documentation, definition switch
                {
                    EnumDefinition e => e.DeprecatedDecorator,
                    RecordDefinition r => r.DeprecatedDecorator,
                    _ => null
                }, 0));

                switch (definition)
                {
                    case EnumDefinition ed:
                    {
                        var is64Bit = ed.ScalarType.Is64Bit;
                        if (is64Bit)
                        {
                            builder.AppendLine($"export type {ed.ClassName()} = bigint;");
                            builder.AppendLine($"export const {ed.ClassName()} = {{");
                        }
                        else
                        {
                            builder.AppendLine($"export enum {ed.ClassName()} {{");
                        }

                        for (var i = 0; i < ed.Members.Count; i++)
                        {
                            var field = ed.Members.ElementAt(i);
                            builder.AppendLine(FormatDocumentation(field.Documentation, field.DeprecatedDecorator, 2));
                            builder.AppendLine(is64Bit
                                ? $"  {field.Name.ToPascalCase()}: {field.ConstantValue}n,"
                                : $"  {field.Name.ToPascalCase()} = {field.ConstantValue},");
                        }

                        builder.AppendLine(is64Bit ? "};" : "}");
                        builder.AppendLine("");
                        break;
                    }
                    case FieldsDefinition fd:
                    {
                        // Generate interface
                        builder.AppendLine($"export interface {fd.ClassName()} {{");
                        foreach (var field in fd.Fields)
                        {
                            var type = TypeName(field.Type);
                            builder.AppendLine(FormatDocumentation(field.Documentation, field.DeprecatedDecorator, 2));
                            var optional = fd is MessageDefinition ? "?" : "";
                            var readonlyModifier = fd is StructDefinition { IsMutable: false } ? "readonly " : "";
                            builder.AppendLine($"  {readonlyModifier}{field.NameCamelCase}{optional}: {type};");
                        }

                        builder.AppendLine("}");
                        builder.AppendLine();

                        // Generate factory function with Object.assign
                        builder.AppendLine(
                            $"export const {fd.ClassName()} = /*#__PURE__*/ Object.freeze(/*#__PURE__*/ Object.assign(");
                        builder.Indent(2);

                        // Factory function
                        builder.AppendLine($"// Factory function");
                        builder.AppendLine($"(data: {fd.ClassName()}): {fd.ClassName()} & BebopRecord => {{");
                        builder.Indent(2);


                        var returnStatement = fd is StructDefinition { IsMutable: false }
                            ? "return Object.freeze({"
                            : "return {";

                        builder.AppendLine(returnStatement);
                        builder.AppendLine("  ...data,");
                        builder.AppendLine("  encode(): Uint8Array {");
                        builder.AppendLine($"    return {fd.ClassName()}.encode(this);");
                        builder.AppendLine("  }");
                        builder.AppendLine(fd is StructDefinition { IsMutable: false }
                            ? "});"
                            : "};");

                        builder.Dedent(2);
                        builder.AppendLine("},");

                        // Static methods object
                        builder.AppendLine("// Static methods");
                        builder.AppendLine("{");
                        builder.Indent(2);

                        // Add opcode getter if present
                        if (fd.OpcodeDecorator is not null && fd.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                        {
                            builder.AppendLine($"get opcode(): number {{");
                            builder.Indent(2);
                            builder.AppendLine($"return {FormatOpcodeWithUnderscores(fourcc)};");
                            builder.Dedent(2);
                            builder.AppendLine("},");
                            builder.AppendLine();
                        }

                        // encode convenience method (object -> Uint8Array)
                        builder.AppendLine($"encode(record: {fd.ClassName()}): Uint8Array {{");
                        builder.Indent(2);
                        builder.AppendLine("const view = BebopView.getInstance();");
                        builder.AppendLine("view.startWriting();");
                        builder.AppendLine($"{fd.ClassName()}.encodeInto(record, view);");
                        builder.AppendLine("return view.toArray();");
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // encodeInto method (object + view -> void)
                        builder.AppendLine($"encodeInto(record: {fd.ClassName()}, view: BebopView): void {{");
                        builder.Indent(2);
                        builder.AppendLine(CompileEncode(fd));
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // decode convenience method (Uint8Array -> object)
                        builder.AppendLine($"decode(buffer: Uint8Array): {fd.ClassName()} & BebopRecord {{");
                        builder.Indent(2);
                        builder.AppendLine("const view = BebopView.getInstance();");
                        builder.AppendLine("view.startReading(buffer);");
                        builder.AppendLine($"const decoded = {fd.ClassName()}.readFrom(view);");
                        builder.AppendLine($"return {fd.ClassName()}(decoded);");
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // readFrom method (view -> object)
                        builder.AppendLine($"readFrom(view: BebopView): {fd.ClassName()} {{");
                        builder.Indent(2);
                        builder.AppendLine(CompileDecode(fd));
                        builder.Dedent(2);
                        builder.AppendLine("},");

                        builder.Dedent(2);
                        builder.AppendLine("}");
                        builder.Dedent(2);
                        builder.AppendLine("));");
                        builder.AppendLine("");
                        break;
                    }
                    case UnionDefinition ud:
                    {
                        // Generate union type
                        var unionTypes = string.Join(" | ", ud.Branches.Select(b =>
                            $"{{ tag: {b.Discriminator}, value: {b.Definition.ClassName()} }}"));

                        builder.AppendLine($"export type {ud.ClassName()} = {unionTypes};");
                        builder.AppendLine();

                        // Generate factory function with Object.assign
                        builder.AppendLine(
                            $"export const {ud.ClassName()} = /*#__PURE__*/ Object.freeze(/*#__PURE__*/ Object.assign(");
                        builder.Indent(2);

                        // Factory function
                        builder.AppendLine($"// Factory function");
                        builder.AppendLine($"(data: {ud.ClassName()}): {ud.ClassName()} & BebopRecord => {{");
                        builder.Indent(2);

                        builder.AppendLine("return {");
                        builder.AppendLine("  ...data,");
                        builder.AppendLine("  encode(): Uint8Array {");
                        builder.AppendLine($"    return {ud.ClassName()}.encode(this);");
                        builder.AppendLine("  }");
                        builder.AppendLine("};");

                        builder.Dedent(2);
                        builder.AppendLine("},");

                        // Static methods object
                        builder.AppendLine("// Static methods");
                        builder.AppendLine("{");
                        builder.Indent(2);

                        if (ud.OpcodeDecorator is not null && ud.OpcodeDecorator.TryGetValue("fourcc", out var fourcc))
                        {
                            builder.AppendLine($"get opcode(): number {{");
                            builder.Indent(2);
                            builder.AppendLine($"return {FormatOpcodeWithUnderscores(fourcc)};");
                            builder.Dedent(2);
                            builder.AppendLine("},");
                            builder.AppendLine();
                        }

                        // Helper methods for each branch
                        foreach (var branch in ud.Branches)
                        {
                            builder.AppendLine(
                                $"from{branch.Definition.ClassName()}(value: {branch.Definition.ClassName()}): {ud.ClassName()} & BebopRecord {{");
                            builder.Indent(2);
                            builder.AppendLine($"return {ud.ClassName()}({{ tag: {branch.Discriminator}, value }});");
                            builder.Dedent(2);
                            builder.AppendLine("},");
                            builder.AppendLine();
                        }

                        // encode convenience method (object -> Uint8Array)
                        builder.AppendLine($"encode(record: {ud.ClassName()}): Uint8Array {{");
                        builder.Indent(2);
                        builder.AppendLine("const view = BebopView.getInstance();");
                        builder.AppendLine("view.startWriting();");
                        builder.AppendLine($"{ud.ClassName()}.encodeInto(record, view);");
                        builder.AppendLine("return view.toArray();");
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // encodeInto method (object + view -> void)
                        builder.AppendLine($"encodeInto(record: {ud.ClassName()}, view: BebopView): void {{");
                        builder.Indent(2);
                        builder.AppendLine(CompileEncode(ud));
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // decode convenience method (Uint8Array -> object)
                        builder.AppendLine($"decode(buffer: Uint8Array): {ud.ClassName()} & BebopRecord {{");
                        builder.Indent(2);
                        builder.AppendLine("const view = BebopView.getInstance();");
                        builder.AppendLine("view.startReading(buffer);");
                        builder.AppendLine($"const decoded = {ud.ClassName()}.readFrom(view);");
                        builder.AppendLine($"return {ud.ClassName()}(decoded);");
                        builder.Dedent(2);
                        builder.AppendLine("},");
                        builder.AppendLine();

                        // readFrom method (view -> object)
                        builder.AppendLine($"readFrom(view: BebopView): {ud.ClassName()} {{");
                        builder.Indent(2);
                        builder.AppendLine(CompileDecode(ud));
                        builder.Dedent(2);
                        builder.AppendLine("},");

                        builder.Dedent(2);
                        builder.AppendLine("}");
                        builder.Dedent(2);
                        builder.AppendLine("));");
                        builder.AppendLine("");
                        break;
                    }
                    case ConstDefinition cd:
                        builder.AppendLine(
                            $"export const {cd.Name}: {TypeName(cd.Value.Type)} = {EmitLiteral(cd.Value)};");
                        builder.AppendLine("");
                        break;
                    case ServiceDefinition:
                        // noop - services handled below
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported definition {definition}");
                }
            }

            var serviceDefinitions = Schema.Definitions.Values.OfType<ServiceDefinition>();
            var definitions = serviceDefinitions.ToList();
            if (definitions.Count > 0 && Config.Services is not TempoServices.None)
            {
                if (Config.Services is TempoServices.Server or TempoServices.Both)
                {
                    foreach (var service in definitions)
                    {
                        if (!string.IsNullOrWhiteSpace(service.Documentation))
                        {
                            builder.AppendLine(FormatDocumentation(service.Documentation, service.DeprecatedDecorator,
                                0));
                        }

                        builder.CodeBlock($"export abstract class {service.BaseClassName()} extends BaseService",
                            IndentStep, () =>
                            {
                                builder.AppendLine($"public static readonly serviceName = '{service.ClassName()}';");
                                foreach (var method in service.Methods)
                                {
                                    var methodType = method.Definition.Type;
                                    if (!string.IsNullOrWhiteSpace(method.Documentation))
                                    {
                                        builder.AppendLine(FormatDocumentation(method.Documentation,
                                            method.DeprecatedDecorator, 0));
                                    }

                                    if (methodType is MethodType.Unary)
                                    {
                                        builder.AppendLine(
                                            $"public abstract {method.Definition.Name.ToCamelCase()}(record: {method.Definition.RequestDefinition}, context: ServerContext): Promise<{method.Definition.ResponseDefintion}>;");
                                    }
                                    else if (methodType is MethodType.ClientStream)
                                    {
                                        builder.AppendLine(
                                            $"public abstract {method.Definition.Name.ToCamelCase()}(records: () => AsyncGenerator<{method.Definition.RequestDefinition}, void, undefined>, context: ServerContext): Promise<{method.Definition.ResponseDefintion}>;");
                                    }
                                    else if (methodType is MethodType.ServerStream)
                                    {
                                        builder.AppendLine(
                                            $"public abstract {method.Definition.Name.ToCamelCase()}(record: {method.Definition.RequestDefinition}, context: ServerContext): AsyncGenerator<{method.Definition.ResponseDefintion}, void, undefined>;");
                                    }
                                    else if (methodType is MethodType.DuplexStream)
                                    {
                                        builder.AppendLine(
                                            $"public abstract {method.Definition.Name.ToCamelCase()}(records: () => AsyncGenerator<{method.Definition.RequestDefinition}, void, undefined>, context: ServerContext): AsyncGenerator<{method.Definition.ResponseDefintion}, void, undefined>;");
                                    }
                                    else
                                    {
                                        throw new InvalidOperationException($"Unsupported method type {methodType}");
                                    }
                                }
                            });
                        builder.AppendLine();
                    }

                    builder.CodeBlock("export class TempoServiceRegistry extends ServiceRegistry", IndentStep, () =>
                    {
                        builder.AppendLine(
                            "private static readonly staticServiceInstances: Map<string, BaseService> = new Map<string, BaseService>();");
                        builder.CodeBlock("public static register(serviceName: string)", IndentStep, () =>
                        {
                            builder.CodeBlock("return (constructor: Function) =>", IndentStep, () =>
                            {
                                builder.AppendLine("const service = Reflect.construct(constructor, [undefined]);");
                                builder.CodeBlock("if (TempoServiceRegistry.staticServiceInstances.has(serviceName))",
                                    IndentStep, () =>
                                    {
                                        builder.AppendLine(
                                            "throw new BebopRuntimeError(`Duplicate service registered: ${serviceName}`);");
                                    });
                                builder.AppendLine(
                                    "TempoServiceRegistry.staticServiceInstances.set(serviceName, service);");
                            });
                        });
                        builder.CodeBlock("public static tryGetService(serviceName: string): BaseService", IndentStep,
                            () =>
                            {
                                builder.AppendLine(
                                    "const service = TempoServiceRegistry.staticServiceInstances.get(serviceName);");
                                builder.CodeBlock("if (service === undefined)", IndentStep, () =>
                                {
                                    builder.AppendLine(
                                        "throw new BebopRuntimeError(`Unable to retreive service '${serviceName}' - it is not registered.`);");
                                });
                                builder.AppendLine("return service;");
                            });

                        builder.AppendLine();

                        builder.CodeBlock("public init(): void", IndentStep, () =>
                        {
                            builder.AppendLine("let service: BaseService;");
                            builder.AppendLine("let serviceName: string;");
                            foreach (var service in definitions)

                            {
                                builder.AppendLine($"serviceName = '{service.ClassName()}';");
                                builder.AppendLine($"service = TempoServiceRegistry.tryGetService(serviceName);");
                                builder.CodeBlock($"if (!(service instanceof {service.BaseClassName()}))", IndentStep,
                                    () =>
                                    {
                                        builder.AppendLine(
                                            "throw new BebopRuntimeError(`No service named '${serviceName}'was registered with the TempoServiceRegistry`);");
                                    });
                                builder.AppendLine($"service.setLogger(this.logger.clone(serviceName));");
                                builder.AppendLine("TempoServiceRegistry.staticServiceInstances.delete(serviceName);");
                                builder.AppendLine("this.serviceInstances.push(service);");
                                foreach (var method in service.Methods)
                                {
                                    var methodType = method.Definition.Type;
                                    var methodName = method.Definition.Name.ToCamelCase();
                                    builder.CodeBlock($"if (this.methods.has({method.Id}))", IndentStep, () =>
                                    {
                                        builder.AppendLine($"const conflictService = this.methods.get({method.Id})!;");
                                        builder.AppendLine(
                                            $"throw new BebopRuntimeError(`{service.ClassName()}.{methodName} collides with ${{conflictService.service}}.${{conflictService.name}}`)");
                                    });
                                    builder.CodeBlock($"this.methods.set({method.Id},", IndentStep, () =>
                                        {
                                            builder.AppendLine($"name: '{methodName}',");
                                            builder.AppendLine($"service: serviceName,");
                                            builder.AppendLine($"invoke: service.{methodName},");
                                            builder.AppendLine(
                                                $"serialize: {method.Definition.ResponseDefintion}.encode,");
                                            builder.AppendLine(
                                                $"deserialize: {method.Definition.RequestDefinition}.decode,");
                                            builder.AppendLine(
                                                $"type: MethodType.{RpcSchema.GetMethodTypeName(methodType)},");
                                        },
                                        close:
                                        $"}} as BebopMethod<{method.Definition.RequestDefinition}, {method.Definition.ResponseDefintion}>);");
                                }
                            }
                        });

                        builder.AppendLine();
                        builder.CodeBlock("getMethod(id: number): BebopMethodAny | undefined", IndentStep, () =>
                        {
                            builder.AppendLine("return this.methods.get(id);");
                        });
                    });
                }

                if (Config.Services is TempoServices.Client or TempoServices.Both)
                {
                    static (string RequestType, string ResponseType) GetFunctionTypes(MethodDefinition definition)
                    {
                        return definition.Type switch
                        {
                            MethodType.Unary => (definition.RequestDefinition.ToString()!,
                                $"Promise<{definition.ResponseDefintion}>"),
                            MethodType.ServerStream => (definition.RequestDefinition.ToString()!,
                                $"Promise<AsyncGenerator<{definition.ResponseDefintion}, void, undefined>>"),
                            MethodType.ClientStream => (
                                $"() => AsyncGenerator<{definition.RequestDefinition}, void, undefined>",
                                $"Promise<{definition.ResponseDefintion}>"),
                            MethodType.DuplexStream => (
                                $"() => AsyncGenerator<{definition.RequestDefinition}, void, undefined>",
                                $"Promise<AsyncGenerator<{definition.ResponseDefintion}, void, undefined>>"),
                            _ => throw new InvalidOperationException($"Unsupported function type {definition.Type}")
                        };
                    }

                    foreach (var service in definitions)
                    {
                        var clientName = service.ClassName().ReplaceLastOccurrence("Service", "Client");
                        builder.AppendLine(FormatDocumentation(service.Documentation, service.DeprecatedDecorator, 0));
                        builder.CodeBlock($"export interface I{clientName}", IndentStep, () =>
                        {
                            foreach (var method in service.Methods)
                            {
                                var (requestType, responseType) = GetFunctionTypes(method.Definition);
                                builder.AppendLine(FormatDocumentation(method.Documentation, method.DeprecatedDecorator,
                                    0));
                                builder.AppendLine(
                                    $"{method.Definition.Name.ToCamelCase()}(request: {requestType}): {responseType};");
                                builder.AppendLine(
                                    $"{method.Definition.Name.ToCamelCase()}(request: {requestType}, metadata: Metadata): {responseType};");
                            }
                        });
                        builder.AppendLine();
                        builder.AppendLine(FormatDocumentation(service.Documentation, service.DeprecatedDecorator, 0));
                        builder.CodeBlock($"export class {clientName} extends BaseClient implements I{clientName}",
                            IndentStep, () =>
                            {
                                foreach (var method in service.Methods)
                                {
                                    var methodInfoName = $"{method.Definition.Name.ToCamelCase()}MethodInfo";
                                    var methodName = method.Definition.Name.ToCamelCase();
                                    var (requestType, responseType) = GetFunctionTypes(method.Definition);
                                    var methodType = method.Definition.Type;
                                    builder.CodeBlock(
                                        $"private static readonly {methodInfoName}: MethodInfo<{method.Definition.RequestDefinition}, {method.Definition.ResponseDefintion}> =",
                                        IndentStep, () =>
                                        {
                                            builder.AppendLine($"name: '{methodName}',");
                                            builder.AppendLine($"service: '{service.ClassName()}',");
                                            builder.AppendLine($"id: {method.Id},");
                                            builder.AppendLine(
                                                $"serialize: {method.Definition.RequestDefinition}.encode,");
                                            builder.AppendLine(
                                                $"deserialize: {method.Definition.ResponseDefintion}.decode,");
                                            builder.AppendLine(
                                                $"type: MethodType.{RpcSchema.GetMethodTypeName(methodType)},");
                                        });

                                    builder.AppendLine(FormatDocumentation(method.Documentation,
                                        method.DeprecatedDecorator, 0));
                                    builder.AppendLine($"async {methodName}(request: {requestType}): {responseType};");
                                    builder.AppendLine(
                                        $"async {methodName}(request: {requestType}, options: CallOptions): {responseType};");

                                    builder.CodeBlock(
                                        $"async {methodName}(request: {requestType}, options?: CallOptions): {responseType}",
                                        IndentStep, () =>
                                        {
                                            if (methodType is MethodType.Unary)
                                            {
                                                builder.AppendLine(
                                                    $"return await this.channel.startUnary(request, this.getContext(), {clientName}.{methodInfoName}, options);");
                                            }
                                            else if (methodType is MethodType.ServerStream)
                                            {
                                                builder.AppendLine(
                                                    $"return await this.channel.startServerStream(request, this.getContext(), {clientName}.{methodInfoName}, options);");
                                            }
                                            else if (methodType is MethodType.ClientStream)
                                            {
                                                builder.AppendLine(
                                                    $"return await this.channel.startClientStream(request, this.getContext(), {clientName}.{methodInfoName}, options);");
                                            }
                                            else if (methodType is MethodType.DuplexStream)
                                            {
                                                builder.AppendLine(
                                                    $"return await this.channel.startDuplexStream(request, this.getContext(), {clientName}.{methodInfoName}, options);");
                                            }
                                            else
                                                throw new InvalidOperationException(
                                                    $"Unsupported method type {methodType}");
                                        });
                                }
                            });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(Config.Namespace))
            {
                builder.Dedent(2);
                builder.AppendLine("}");
            }

            artifacts.Add(new Artifact(config.OutFile, builder.Encode()));

            return ValueTask.FromResult(artifacts.ToArray());
        }

        public override string Alias { get => "ts"; set => throw new NotImplementedException(); }
        public override string Name { get => "TypeScript"; set => throw new NotImplementedException(); }
    }
}