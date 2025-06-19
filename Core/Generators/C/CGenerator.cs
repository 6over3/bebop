using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Core.Meta;
using Core.Meta.Extensions;
using Environment = DotEnv.Generated.Environment;

namespace Core.Generators.C;

public class CGenerator : BaseGenerator
{
    private const int IndentStep = 4; // WebKit uses 4 spaces

    private const string BytePattern = """(?:")?(\b(1?[0-9]{1,2}|2[0-4][0-9]|25[0-5])\b)(?:")?""";

    private static readonly Regex _majorRegex = new($@"(?<=BEBOPC_VER_MAJOR\s){BytePattern}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex _minorRegex = new($@"(?<=BEBOPC_VER_MINOR\s){BytePattern}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex _patchRegex = new($@"(?<=BEBOPC_VER_PATCH\s){BytePattern}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex _informationalRegex = new($@"(?<=BEBOPC_VER_INFO\s){BytePattern}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public override string Alias { get => "c"; set => throw new NotImplementedException(); }
    public override string Name { get => "c"; set => throw new NotImplementedException(); }

    private RegionStyle GetRegionStyle()
    {
        var style = Config.GetOptionRawValue("region-style")?.ToLowerInvariant();
        return style switch
        {
            "cpp" => RegionStyle.CppStyle,
            "comment" => RegionStyle.Comment,
            "standard" => RegionStyle.Standard,
            _ => RegionStyle.Comment
        };
    }

    private string FormatRegionStart(string name)
    {
        return GetRegionStyle() switch
        {
            RegionStyle.CppStyle => $"//region {name}",
            RegionStyle.Comment => $"//#region {name}",
            RegionStyle.Standard => $"#region {name}",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private string FormatRegionEnd()
    {
        return GetRegionStyle() switch
        {
            RegionStyle.CppStyle => "//endregion",
            RegionStyle.Comment => "//#endregion",
            RegionStyle.Standard => "#endregion",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static string FormatDocumentation(string? documentation, int spaces, bool isDeprecated = false,
        string? deprecatedReason = null, string? briefTag = null, string? typeTag = null)
    {
        if (string.IsNullOrWhiteSpace(documentation) && !isDeprecated && string.IsNullOrWhiteSpace(briefTag))
        {
            return string.Empty;
        }

        var builder = new IndentedStringBuilder();
        builder.Indent(spaces);
        builder.AppendLine("/**");

        // Add type tag if provided (e.g., @enum, @typedef)
        if (!string.IsNullOrWhiteSpace(typeTag))
        {
            builder.AppendLine($" * {typeTag}");
        }

        // Add brief tag if provided
        if (!string.IsNullOrWhiteSpace(briefTag))
        {
            builder.AppendLine($" * @brief {briefTag}");
        }

        if (!string.IsNullOrWhiteSpace(documentation))
        {
            // Add empty line after brief/type tags if we have documentation
            if (!string.IsNullOrWhiteSpace(briefTag) || !string.IsNullOrWhiteSpace(typeTag))
            {
                builder.AppendLine(" *");
            }

            foreach (var line in documentation.GetLines())
            {
                builder.AppendLine($" * {line}");
            }
        }

        if (isDeprecated)
        {
            if (!string.IsNullOrWhiteSpace(documentation) || !string.IsNullOrWhiteSpace(briefTag))
            {
                builder.AppendLine(" *");
            }

            if (!string.IsNullOrWhiteSpace(deprecatedReason))
            {
                builder.AppendLine($" * @deprecated {deprecatedReason}");
            }
            else
            {
                builder.AppendLine(" * @deprecated");
            }
        }

        builder.AppendLine(" */");
        return builder.ToString();
    }

    // Overload for simple member documentation (single line with @brief)
    private static string FormatMemberDocumentation(string? documentation, int spaces, bool isDeprecated = false,
        string? deprecatedReason = null)
    {
        if (string.IsNullOrWhiteSpace(documentation) && !isDeprecated)
        {
            return string.Empty;
        }

        var builder = new IndentedStringBuilder();
        builder.Indent(spaces);

        if (!string.IsNullOrWhiteSpace(documentation))
        {
            builder.AppendLine($"/** @brief {documentation} */");
        }
        else if (isDeprecated)
        {
            if (!string.IsNullOrWhiteSpace(deprecatedReason))
            {
                builder.AppendLine($"/** @deprecated {deprecatedReason} */");
            }
            else
            {
                builder.AppendLine("/** @deprecated */");
            }
        }

        return builder.ToString();
    }

    private string GetNamespacePrefix()
    {
        if (string.IsNullOrWhiteSpace(Config.Namespace))
        {
            return string.Empty;
        }

        return Config.Namespace.ToSnakeCase() + "_";
    }

    private string GetHeaderGuard(string filename)
    {
        var guardName = Path.GetFileNameWithoutExtension(filename)
            .ToUpperInvariant()
            .Replace('-', '_')
            .Replace('.', '_');

        var namespacePrefix = GetNamespacePrefix().ToUpperInvariant().Replace("_", "");

        if (!string.IsNullOrEmpty(namespacePrefix))
        {
            return $"{namespacePrefix}_{guardName}_H";
        }

        return $"{guardName}_H";
    }

    /// <summary>
    ///     Generate the body of the encode function for the given RecordDefinition.
    /// </summary>
    private string CompileEncode(RecordDefinition definition)
    {
        return definition switch
        {
            MessageDefinition d => CompileEncodeMessage(d),
            StructDefinition d => CompileEncodeStruct(d),
            UnionDefinition d => CompileEncodeUnion(d),
            _ => throw new InvalidOperationException($"invalid CompileEncode kind: {definition}")
        };
    }

    private string CompileEncodeMessage(MessageDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);
        builder.AppendLine("size_t pos;");
        builder.AppendLine("bebop_result_t result = bebop_writer_reserve_message_length(writer, &pos);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("size_t start = bebop_writer_length(writer);");
        builder.AppendLine("");

        foreach (var field in definition.Fields)
        {
            if (field.DeprecatedDecorator != null)
            {
                continue;
            }

            builder.AppendLine($"if (bebop_is_some(record->{field.Name.ToSnakeCase()})) {{");
            builder.Indent(IndentStep);
            builder.AppendLine($"result = bebop_writer_write_byte(writer, {field.ConstantValue});");
            builder.AppendLine("if (result != BEBOP_OK)");
            builder.Indent(IndentStep);
            builder.AppendLine("return result;");
            builder.Dedent(IndentStep);
            builder.AppendLine("");
            builder.AppendLine(CompileEncodeField(field.Type, $"bebop_unwrap(record->{field.Name.ToSnakeCase()})"));
            builder.Dedent(IndentStep);
            builder.AppendLine("}");
            builder.AppendLine("");
        }

        builder.AppendLine("result = bebop_writer_write_byte(writer, 0);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("size_t end = bebop_writer_length(writer);");
        builder.AppendLine("return bebop_writer_fill_message_length(writer, pos, (uint32_t)(end - start));");

        return builder.ToString();
    }

    private string CompileEncodeStruct(StructDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);

        // Check if the struct has any fields
        if (definition.Fields.Count == 0)
        {
            // For empty structs, just return BEBOP_OK directly
            builder.AppendLine("return BEBOP_OK;");
            return builder.ToString();
        }

        // For structs with fields, proceed with normal generation
        builder.AppendLine("bebop_result_t result;");
        builder.AppendLine("");

        foreach (var field in definition.Fields)
        {
            builder.AppendLine(CompileEncodeField(field.Type, $"record->{field.Name.ToSnakeCase()}"));
            builder.AppendLine("");
        }

        builder.AppendLine("return BEBOP_OK;");
        return builder.ToString();
    }

    private string CompileEncodeUnion(UnionDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);
        builder.AppendLine("size_t pos;");
        builder.AppendLine("bebop_result_t result = bebop_writer_reserve_message_length(writer, &pos);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("result = bebop_writer_write_byte(writer, record->tag);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("size_t start = bebop_writer_length(writer);");
        builder.AppendLine("");
        builder.AppendLine("switch (record->tag) {");

        foreach (var branch in definition.Branches)
        {
            var namespacePrefix = GetNamespacePrefix();
            builder.AppendLine($"case {branch.Discriminator}:");
            builder.Indent(IndentStep);
            builder.AppendLine(
                $"result = {namespacePrefix}{branch.Definition.Name.ToSnakeCase()}_encode_into(&record->as_{branch.Definition.Name.ToSnakeCase()}, writer);");
            builder.AppendLine("if (result != BEBOP_OK)");
            builder.Indent(IndentStep);
            builder.AppendLine("return result;");
            builder.Dedent(IndentStep);
            builder.AppendLine("break;");
            builder.Dedent(IndentStep);
        }

        builder.AppendLine("default:");
        builder.Indent(IndentStep);
        builder.AppendLine("return BEBOP_ERROR_MALFORMED_PACKET;");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");
        builder.AppendLine("size_t end = bebop_writer_length(writer);");
        builder.AppendLine("return bebop_writer_fill_message_length(writer, pos, (uint32_t)(end - start));");

        return builder.ToString();
    }

    private string CompileEncodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0)
    {
        var i = GeneratorUtils.LoopVariable(depth);
        var builder = new IndentedStringBuilder(indentDepth);

        switch (type)
        {
            case ArrayType at when at.IsBytes():
                builder.AppendLine($"result = bebop_writer_write_byte_array_view(writer, {target});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            case ArrayType at when IsFixedScalarTypeForBulk(at.MemberType):
                // Use bulk operations for fixed-size scalar arrays
                var bulkFunction = GetBulkWriteFunction(at.MemberType);
                if (bulkFunction != null)
                {
                    builder.AppendLine($"result = {bulkFunction}(writer, {target}.data, {target}.length);");
                    builder.AppendLine("if (result != BEBOP_OK)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return result;");
                    builder.Dedent(IndentStep);
                }
                else
                {
                    // Fallback to loop for types without specialized bulk functions
                    builder.AppendLine($"result = bebop_writer_write_uint32(writer, (uint32_t){target}.length);");
                    builder.AppendLine("if (result != BEBOP_OK)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return result;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine($"for (size_t {i} = 0; {i} < {target}.length; {i}++) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(CompileEncodeField(at.MemberType, $"{target}.data[{i}]", depth + 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                }

                break;

            case ArrayType at:
                // Use loop-based approach for complex types (strings, structs, etc.)
                builder.AppendLine($"result = bebop_writer_write_uint32(writer, (uint32_t){target}.length);");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                builder.AppendLine($"for (size_t {i} = 0; {i} < {target}.length; {i}++) {{");
                builder.Indent(IndentStep);
                builder.AppendLine(CompileEncodeField(at.MemberType, $"{target}.data[{i}]", depth + 1));
                builder.Dedent(IndentStep);
                builder.AppendLine("}");
                break;

            case MapType mt:
                builder.AppendLine($"result = bebop_writer_write_uint32(writer, (uint32_t){target}.length);");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                builder.AppendLine($"for (size_t {i} = 0; {i} < {target}.length; {i}++) {{");
                builder.Indent(IndentStep);
                builder.AppendLine(CompileEncodeField(mt.KeyType, $"{target}.entries[{i}].key", depth + 1));
                builder.AppendLine(CompileEncodeField(mt.ValueType, $"{target}.entries[{i}].value", depth + 1));
                builder.Dedent(IndentStep);
                builder.AppendLine("}");
                break;

            case ScalarType st:
                var call = st.BaseType switch
                {
                    BaseType.Bool => $"bebop_writer_write_bool(writer, {target})",
                    BaseType.Byte => $"bebop_writer_write_byte(writer, {target})",
                    BaseType.UInt16 => $"bebop_writer_write_uint16(writer, {target})",
                    BaseType.Int16 => $"bebop_writer_write_int16(writer, {target})",
                    BaseType.UInt32 => $"bebop_writer_write_uint32(writer, {target})",
                    BaseType.Int32 => $"bebop_writer_write_int32(writer, {target})",
                    BaseType.UInt64 => $"bebop_writer_write_uint64(writer, {target})",
                    BaseType.Int64 => $"bebop_writer_write_int64(writer, {target})",
                    BaseType.Float32 => $"bebop_writer_write_float32(writer, {target})",
                    BaseType.Float64 => $"bebop_writer_write_float64(writer, {target})",
                    BaseType.String => $"bebop_writer_write_string_view(writer, {target})",
                    BaseType.Guid => $"bebop_writer_write_guid(writer, {target})",
                    BaseType.Date => $"bebop_writer_write_date(writer, {target})",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                };
                builder.AppendLine($"result = {call};");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            case DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed:
                return CompileEncodeField(ed.ScalarType, target, depth, indentDepth);

            case DefinedType dt:
                builder.AppendLine(
                    $"result = {GetNamespacePrefix()}{dt.Name.ToSnakeCase()}_encode_into(&{target}, writer);");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            default:
                throw new InvalidOperationException($"CompileEncodeField: {type}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Check if a type is suitable for bulk array operations
    /// </summary>
    private bool IsFixedScalarTypeForBulk(TypeBase type)
    {
        return type switch
        {
            ScalarType st => st.BaseType switch
            {
                BaseType.Float32 or BaseType.Float64 or
                    BaseType.UInt16 or BaseType.Int16 or
                    BaseType.UInt32 or BaseType.Int32 or
                    BaseType.UInt64 or BaseType.Int64 or
                    BaseType.Bool => true,
                _ => false
            },
            DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition => true,
            _ => false
        };
    }

    /// <summary>
    /// Get the specialized bulk write function name for a type
    /// </summary>
    private string? GetBulkWriteFunction(TypeBase type)
    {
        return type switch
        {
            ScalarType st => st.BaseType switch
            {
                BaseType.Float32 => "bebop_writer_write_float32_array",
                BaseType.Float64 => "bebop_writer_write_float64_array",
                BaseType.UInt16 => "bebop_writer_write_uint16_array",
                BaseType.Int16 => "bebop_writer_write_int16_array",
                BaseType.UInt32 => "bebop_writer_write_uint32_array",
                BaseType.Int32 => "bebop_writer_write_int32_array",
                BaseType.UInt64 => "bebop_writer_write_uint64_array",
                BaseType.Int64 => "bebop_writer_write_int64_array",
                BaseType.Bool => "bebop_writer_write_bool_array",
                _ => null
            },
            DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed => GetBulkWriteFunction(ed.ScalarType),
            _ => null
        };
    }

    /// <summary>
    ///     Generate the body of the decode function for the given RecordDefinition.
    /// </summary>
    private string CompileDecode(RecordDefinition definition)
    {
        return definition switch
        {
            MessageDefinition d => CompileDecodeMessage(d),
            StructDefinition d => CompileDecodeStruct(d),
            UnionDefinition d => CompileDecodeUnion(d),
            _ => throw new InvalidOperationException($"invalid CompileDecode kind: {definition}")
        };
    }

    private string CompileDecodeMessage(MessageDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);
        builder.AppendLine("memset(out_record, 0, sizeof(*out_record));");
        builder.AppendLine("");
        builder.AppendLine("uint32_t length;");
        builder.AppendLine("bebop_result_t result = bebop_reader_read_length_prefix(reader, &length);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("const uint8_t* end = bebop_reader_position(reader) + length;");
        builder.AppendLine("");
        builder.AppendLine("while (bebop_reader_position(reader) < end) {");
        builder.Indent(IndentStep);
        builder.AppendLine("uint8_t field_id;");
        builder.AppendLine("result = bebop_reader_read_byte(reader, &field_id);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("switch (field_id) {");
        builder.AppendLine("case 0:");
        builder.Indent(IndentStep);
        builder.AppendLine("return BEBOP_OK;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");

        var fieldCounter = 0;
        foreach (var field in definition.Fields)
        {
            builder.AppendLine($"case {field.ConstantValue}:");
            builder.Indent(IndentStep);
            builder.AppendLine($"out_record->{field.Name.ToSnakeCase()}.has_value = true;");
            builder.AppendLine(CompileDecodeField(field.Type, $"out_record->{field.Name.ToSnakeCase()}.value",
                fieldCounter));
            builder.AppendLine("break;");
            builder.Dedent(IndentStep);
            builder.AppendLine("");
            fieldCounter++;
        }

        builder.AppendLine("default:");
        builder.Indent(IndentStep);
        builder.AppendLine("bebop_reader_seek(reader, end);");
        builder.AppendLine("return BEBOP_OK;");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");
        builder.AppendLine("return BEBOP_OK;");

        return builder.ToString();
    }

    private string CompileDecodeStruct(StructDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);

        // Check if the struct has any fields
        if (definition.Fields.Count == 0)
        {
            // For empty structs, just return BEBOP_OK directly
            builder.AppendLine("return BEBOP_OK;");
            return builder.ToString();
        }

        // For structs with fields, proceed with normal generation
        builder.AppendLine("bebop_result_t result;");
        builder.AppendLine("");

        var fieldIndex = 0;
        foreach (var field in definition.Fields)
        {
            // Use fieldIndex to ensure unique variable names for each field
            builder.AppendLine(CompileDecodeField(field.Type, $"out_record->{field.Name.ToSnakeCase()}", fieldIndex,
                0, definition));
            builder.AppendLine("");
            fieldIndex++;
        }

        builder.AppendLine("return BEBOP_OK;");
        return builder.ToString();
    }

    private string CompileDecodeUnion(UnionDefinition definition)
    {
        var builder = new IndentedStringBuilder(IndentStep);
        builder.AppendLine("uint32_t length;");
        builder.AppendLine("bebop_result_t result = bebop_reader_read_length_prefix(reader, &length);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("const uint8_t* end = bebop_reader_position(reader) + length + 1;");
        builder.AppendLine("");
        builder.AppendLine("uint8_t tag;");
        builder.AppendLine("result = bebop_reader_read_byte(reader, &tag);");
        builder.AppendLine("if (result != BEBOP_OK)");
        builder.Indent(IndentStep);
        builder.AppendLine("return result;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine("out_record->tag = tag;");
        builder.AppendLine("");
        builder.AppendLine("switch (tag) {");

        foreach (var branch in definition.Branches)
        {
            var namespacePrefix = GetNamespacePrefix();
            builder.AppendLine($"case {branch.Discriminator}:");
            builder.Indent(IndentStep);
            builder.AppendLine(
                $"return {namespacePrefix}{branch.Definition.Name.ToSnakeCase()}_decode_into(reader, &out_record->as_{branch.Definition.Name.ToSnakeCase()});");
            builder.Dedent(IndentStep);
        }

        builder.AppendLine("default:");
        builder.Indent(IndentStep);
        builder.AppendLine("bebop_reader_seek(reader, end);");
        builder.AppendLine("return BEBOP_ERROR_MALFORMED_PACKET;");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");

        return builder.ToString();
    }

    private string CompileDecodeField(TypeBase type, string target, int depth = 0, int indentDepth = 0,
        RecordDefinition? parentDefinition = null)
    {
        var i = GeneratorUtils.LoopVariable(depth);
        var builder = new IndentedStringBuilder(indentDepth);

        // Determine if we need to cast away const for non-mutable structs
        var needsConstCast = parentDefinition is StructDefinition { IsMutable: false };

        switch (type)
        {
            case ArrayType at when at.IsBytes():
                var byteArrayTarget = needsConstCast ? $"(bebop_byte_array_view_t*)&{target}" : $"&{target}";
                builder.AppendLine($"result = bebop_reader_read_byte_array_view(reader, {byteArrayTarget});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            case ArrayType at when IsFixedScalarType(at.MemberType):
                // For fixed-size primitive arrays, use zero-copy views
                builder.AppendLine($"uint32_t length_{depth};");
                builder.AppendLine($"result = bebop_reader_read_uint32(reader, &length_{depth});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);

                if (needsConstCast)
                {
                    var arrayType = GetArrayTypeName(at);
                    builder.AppendLine($"(({arrayType}*)&{target})->length = length_{depth};");
                    builder.AppendLine(
                        $"(({arrayType}*)&{target})->data = ({GetCTypeName(at.MemberType)}*)bebop_reader_position(reader);");
                }
                else
                {
                    builder.AppendLine($"{target}.length = length_{depth};");
                    builder.AppendLine(
                        $"{target}.data = ({GetCTypeName(at.MemberType)}*)bebop_reader_position(reader);");
                }

                builder.AppendLine(
                    $"bebop_reader_skip(reader, length_{depth} * sizeof({GetCTypeName(at.MemberType)}));");
                break;

            case ArrayType at:
                // For variable-length elements, allocate and parse each element
                builder.AppendLine($"uint32_t array_length_{depth};");
                builder.AppendLine($"result = bebop_reader_read_uint32(reader, &array_length_{depth});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);

                if (needsConstCast)
                {
                    var arrayType = GetAllocatedArrayTypeName(at);
                    // Fix: Add extra parentheses around the cast expressions
                    builder.AppendLine($"(({arrayType}*)&{target})->length = array_length_{depth};");
                    builder.AppendLine($"if (array_length_{depth} > 0) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(
                        $"(({arrayType}*)&{target})->data = ({GetCTypeName(at.MemberType)}*)bebop_context_alloc(reader->context, array_length_{depth} * sizeof({GetCTypeName(at.MemberType)}));");
                    builder.AppendLine($"if (!(({arrayType}*)&{target})->data)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return BEBOP_ERROR_OUT_OF_MEMORY;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine($"for (size_t {i} = 0; {i} < array_length_{depth}; {i}++) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(CompileDecodeField(at.MemberType, $"(({arrayType}*)&{target})->data[{i}]",
                        depth + 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("} else {");
                    builder.Indent(IndentStep);
                    builder.AppendLine($"(({arrayType}*)&{target})->data = NULL;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                }
                else
                {
                    builder.AppendLine($"{target}.length = array_length_{depth};");
                    builder.AppendLine($"if (array_length_{depth} > 0) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(
                        $"{target}.data = ({GetCTypeName(at.MemberType)}*)bebop_context_alloc(reader->context, array_length_{depth} * sizeof({GetCTypeName(at.MemberType)}));");
                    builder.AppendLine($"if (!{target}.data)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return BEBOP_ERROR_OUT_OF_MEMORY;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine($"for (size_t {i} = 0; {i} < array_length_{depth}; {i}++) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(CompileDecodeField(at.MemberType, $"{target}.data[{i}]", depth + 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("} else {");
                    builder.Indent(IndentStep);
                    builder.AppendLine($"{target}.data = NULL;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                }

                break;

            case MapType mt when IsFixedScalarType(mt.KeyType) && IsFixedScalarType(mt.ValueType):
                // For maps with fixed-size keys and values, use zero-copy views
                builder.AppendLine($"uint32_t map_length_{depth};");
                builder.AppendLine($"result = bebop_reader_read_uint32(reader, &map_length_{depth});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);

                if (needsConstCast)
                {
                    var mapType = GetMapTypeName(mt);
                    // Fix: Add extra parentheses around the cast expressions
                    builder.AppendLine($"(({mapType}*)&{target})->length = map_length_{depth};");
                    builder.AppendLine(
                        $"(({mapType}*)&{target})->entries = ({GetMapEntryTypeName(mt)}*)bebop_reader_position(reader);");
                }
                else
                {
                    builder.AppendLine($"{target}.length = map_length_{depth};");
                    builder.AppendLine(
                        $"{target}.entries = ({GetMapEntryTypeName(mt)}*)bebop_reader_position(reader);");
                }

                builder.AppendLine(
                    $"bebop_reader_skip(reader, map_length_{depth} * sizeof({GetMapEntryTypeName(mt)}));");
                break;

            case MapType mt:
                // For maps with variable-length data, allocate and parse each entry
                builder.AppendLine($"uint32_t map_length_{depth};");
                builder.AppendLine($"result = bebop_reader_read_uint32(reader, &map_length_{depth});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);

                if (needsConstCast)
                {
                    var mapType = GetAllocatedMapTypeName(mt);
                    // Fix: Add extra parentheses around the cast expressions
                    builder.AppendLine($"(({mapType}*)&{target})->length = map_length_{depth};");
                    builder.AppendLine($"if (map_length_{depth} > 0) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(
                        $"(({mapType}*)&{target})->entries = ({GetMapEntryTypeName(mt)}*)bebop_context_alloc(reader->context, map_length_{depth} * sizeof({GetMapEntryTypeName(mt)}));");
                    builder.AppendLine($"if (!(({mapType}*)&{target})->entries)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return BEBOP_ERROR_OUT_OF_MEMORY;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine($"for (size_t {i} = 0; {i} < map_length_{depth}; {i}++) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(CompileDecodeField(mt.KeyType, $"(({mapType}*)&{target})->entries[{i}].key",
                        depth + 1));
                    builder.AppendLine(CompileDecodeField(mt.ValueType,
                        $"(({mapType}*)&{target})->entries[{i}].value", depth + 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("} else {");
                    builder.Indent(IndentStep);
                    builder.AppendLine($"(({mapType}*)&{target})->entries = NULL;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                }
                else
                {
                    builder.AppendLine($"{target}.length = map_length_{depth};");
                    builder.AppendLine($"if (map_length_{depth} > 0) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(
                        $"{target}.entries = ({GetMapEntryTypeName(mt)}*)bebop_context_alloc(reader->context, map_length_{depth} * sizeof({GetMapEntryTypeName(mt)}));");
                    builder.AppendLine($"if (!{target}.entries)");
                    builder.Indent(IndentStep);
                    builder.AppendLine("return BEBOP_ERROR_OUT_OF_MEMORY;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine($"for (size_t {i} = 0; {i} < map_length_{depth}; {i}++) {{");
                    builder.Indent(IndentStep);
                    builder.AppendLine(CompileDecodeField(mt.KeyType, $"{target}.entries[{i}].key", depth + 1));
                    builder.AppendLine(CompileDecodeField(mt.ValueType, $"{target}.entries[{i}].value", depth + 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("} else {");
                    builder.Indent(IndentStep);
                    builder.AppendLine($"{target}.entries = NULL;");
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                }

                break;

            case ScalarType st:
                // Handle const casting for scalar types
                var scalarTarget = needsConstCast ? GetConstCastTarget(st, target) : $"&{target}";
                var call = st.BaseType switch
                {
                    BaseType.Bool => $"bebop_reader_read_bool(reader, {scalarTarget})",
                    BaseType.Byte => $"bebop_reader_read_byte(reader, {scalarTarget})",
                    BaseType.UInt16 => $"bebop_reader_read_uint16(reader, {scalarTarget})",
                    BaseType.Int16 => $"bebop_reader_read_int16(reader, {scalarTarget})",
                    BaseType.UInt32 => $"bebop_reader_read_uint32(reader, {scalarTarget})",
                    BaseType.Int32 => $"bebop_reader_read_int32(reader, {scalarTarget})",
                    BaseType.UInt64 => $"bebop_reader_read_uint64(reader, {scalarTarget})",
                    BaseType.Int64 => $"bebop_reader_read_int64(reader, {scalarTarget})",
                    BaseType.Float32 => $"bebop_reader_read_float32(reader, {scalarTarget})",
                    BaseType.Float64 => $"bebop_reader_read_float64(reader, {scalarTarget})",
                    BaseType.String => $"bebop_reader_read_string_view(reader, {scalarTarget})",
                    BaseType.Guid => $"bebop_reader_read_guid(reader, {scalarTarget})",
                    BaseType.Date => $"bebop_reader_read_date(reader, {scalarTarget})",
                    _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
                };
                builder.AppendLine($"result = {call};");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            case DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed:
                // For enums, read directly into the target with const casting if needed
                var enumTarget = needsConstCast ? GetEnumConstCastTarget(ed, target) : $"&{target}";
                var underlyingCall = ed.ScalarType.BaseType switch
                {
                    BaseType.Byte => $"bebop_reader_read_byte(reader, {enumTarget})",
                    BaseType.UInt16 => $"bebop_reader_read_uint16(reader, {enumTarget})",
                    BaseType.Int16 => $"bebop_reader_read_int16(reader, {enumTarget})",
                    BaseType.UInt32 => $"bebop_reader_read_uint32(reader, {enumTarget})",
                    BaseType.Int32 => $"bebop_reader_read_int32(reader, {enumTarget})",
                    BaseType.UInt64 => $"bebop_reader_read_uint64(reader, {enumTarget})",
                    BaseType.Int64 => $"bebop_reader_read_int64(reader, {enumTarget})",
                    _ => throw new ArgumentOutOfRangeException(ed.ScalarType.BaseType.ToString())
                };
                builder.AppendLine($"result = {underlyingCall};");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            case DefinedType dt:
                var structTarget = needsConstCast
                    ? $"({GetStructTypeName(Schema.Definitions[dt.Name])}*)&{target}"
                    : $"&{target}";
                builder.AppendLine(
                    $"result = {GetNamespacePrefix()}{dt.Name.ToSnakeCase()}_decode_into(reader, {structTarget});");
                builder.AppendLine("if (result != BEBOP_OK)");
                builder.Indent(IndentStep);
                builder.AppendLine("return result;");
                builder.Dedent(IndentStep);
                break;

            default:
                throw new InvalidOperationException($"CompileDecodeField: {type}");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Get the appropriate const cast target for scalar types
    /// </summary>
    private string GetConstCastTarget(ScalarType st, string target)
    {
        return st.BaseType switch
        {
            BaseType.Bool => $"(bool*)&{target}",
            BaseType.Byte => $"(uint8_t*)&{target}",
            BaseType.UInt16 => $"(uint16_t*)&{target}",
            BaseType.Int16 => $"(int16_t*)&{target}",
            BaseType.UInt32 => $"(uint32_t*)&{target}",
            BaseType.Int32 => $"(int32_t*)&{target}",
            BaseType.UInt64 => $"(uint64_t*)&{target}",
            BaseType.Int64 => $"(int64_t*)&{target}",
            BaseType.Float32 => $"(float*)&{target}",
            BaseType.Float64 => $"(double*)&{target}",
            BaseType.String => $"(bebop_string_view_t*)&{target}",
            BaseType.Guid => $"(bebop_guid_t*)&{target}",
            BaseType.Date => $"(bebop_date_t*)&{target}",
            _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
        };
    }

    /// <summary>
    ///     Get the appropriate const cast target for enum types
    /// </summary>
    private string GetEnumConstCastTarget(EnumDefinition ed, string target)
    {
        return ed.ScalarType.BaseType switch
        {
            BaseType.Byte => $"(uint8_t*)&{target}",
            BaseType.UInt16 => $"(uint16_t*)&{target}",
            BaseType.Int16 => $"(int16_t*)&{target}",
            BaseType.UInt32 => $"(uint32_t*)&{target}",
            BaseType.Int32 => $"(int32_t*)&{target}",
            BaseType.UInt64 => $"(uint64_t*)&{target}",
            BaseType.Int64 => $"(int64_t*)&{target}",
            _ => throw new ArgumentOutOfRangeException(ed.ScalarType.BaseType.ToString())
        };
    }

    /// <summary>
    ///     Generate a C type name for the given TypeBase.
    /// </summary>
    private string GetCTypeName(TypeBase type)
    {
        return type switch
        {
            ScalarType st => st.BaseType switch
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
                BaseType.String => "bebop_string_view_t",
                BaseType.Guid => "bebop_guid_t",
                BaseType.Date => "bebop_date_t",
                _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
            },
            ArrayType at when at.IsBytes() => "bebop_byte_array_view_t",
            ArrayType at when IsFixedScalarType(at.MemberType) => GetArrayTypeName(at),
            ArrayType at => GetAllocatedArrayTypeName(at),
            MapType mt when IsFixedScalarType(mt.KeyType) && IsFixedScalarType(mt.ValueType) => GetMapTypeName(mt),
            MapType mt => GetAllocatedMapTypeName(mt),
            DefinedType dt => GetStructTypeName(Schema.Definitions[dt.Name]),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    /// <summary>
    ///     Get the C type name for message fields (wrapped in optional)
    /// </summary>
    private string GetMessageFieldTypeName(TypeBase type)
    {
        var baseTypeName = GetCTypeName(type);
        return $"bebop_optional({baseTypeName})";
    }

    /// <summary>
    ///     Get the C type name for struct fields, with const modifier if the struct is not mutable
    /// </summary>
    private string GetStructFieldTypeName(TypeBase type, bool isMutable)
    {
        var baseTypeName = GetCTypeName(type);
        return isMutable ? baseTypeName : $"const {baseTypeName}";
    }

    private string GetArrayTypeName(ArrayType arrayType)
    {
        var memberTypeName = GetCTypeName(arrayType.MemberType);

        // Map C types to array type names
        return memberTypeName switch
        {
            "uint8_t" => "bebop_uint8_array_view_t",
            "uint16_t" => "bebop_uint16_array_view_t",
            "uint32_t" => "bebop_uint32_array_view_t",
            "uint64_t" => "bebop_uint64_array_view_t",
            "int16_t" => "bebop_int16_array_view_t",
            "int32_t" => "bebop_int32_array_view_t",
            "int64_t" => "bebop_int64_array_view_t",
            "float" => "bebop_float32_array_view_t",
            "double" => "bebop_float64_array_view_t",
            "bool" => "bebop_bool_array_view_t",
            "bebop_guid_t" => "bebop_guid_array_view_t",
            "bebop_date_t" => "bebop_date_array_view_t",
            _ => $"bebop_{memberTypeName.Replace("_t", "").Replace("bebop_", "")}_array_view_t"
        };
    }

    private string GetAllocatedArrayTypeName(ArrayType arrayType)
    {
        var memberTypeName = GetCTypeName(arrayType.MemberType);

        // Map C types to allocated array type names
        return memberTypeName switch
        {
            "uint8_t" => "bebop_uint8_array_t",
            "uint16_t" => "bebop_uint16_array_t",
            "uint32_t" => "bebop_uint32_array_t",
            "uint64_t" => "bebop_uint64_array_t",
            "int16_t" => "bebop_int16_array_t",
            "int32_t" => "bebop_int32_array_t",
            "int64_t" => "bebop_int64_array_t",
            "float" => "bebop_float32_array_t",
            "double" => "bebop_float64_array_t",
            "bool" => "bebop_bool_array_t",
            "bebop_string_view_t" => "bebop_string_view_array_t",
            "bebop_guid_t" => "bebop_guid_array_t",
            "bebop_date_t" => "bebop_date_array_t",
            _ => $"bebop_{memberTypeName.Replace("_t", "").Replace("bebop_", "")}_array_t"
        };
    }

    private string GetMapTypeName(MapType mapType)
    {
        var keyTypeName = GetCTypeName(mapType.KeyType).Replace("_t", "").Replace("bebop_", "");
        var valueTypeName = GetCTypeName(mapType.ValueType).Replace("_t", "").Replace("bebop_", "");
        return $"bebop_{keyTypeName}_{valueTypeName}_map_view_t";
    }

    private string GetAllocatedMapTypeName(MapType mapType)
    {
        var keyTypeName = GetCTypeName(mapType.KeyType).Replace("_t", "").Replace("bebop_", "");
        var valueTypeName = GetCTypeName(mapType.ValueType).Replace("_t", "").Replace("bebop_", "");
        return $"bebop_{keyTypeName}_{valueTypeName}_map_t";
    }

    private string GetMapEntryTypeName(MapType mapType)
    {
        var keyTypeName = GetCTypeName(mapType.KeyType).Replace("_t", "").Replace("bebop_", "");
        var valueTypeName = GetCTypeName(mapType.ValueType).Replace("_t", "").Replace("bebop_", "");
        return $"bebop_{keyTypeName}_{valueTypeName}_map_entry_t";
    }

    private string GetStructTypeName(Definition definition)
    {
        return GetNamespacePrefix() + definition.Name.ToSnakeCase() + "_t";
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
            FloatLiteral { Value: "inf" } => "INFINITY",
            FloatLiteral { Value: "-inf" } => "-INFINITY",
            FloatLiteral { Value: "nan" } => "NAN",
            FloatLiteral fl => fl.Value,
            StringLiteral sl => EscapeStringLiteral(sl.Value),
            GuidLiteral gl => $"/* GUID: {gl.Value} */",
            _ => throw new ArgumentOutOfRangeException(literal.ToString())
        };
    }

    /// <summary>
    ///     Generate code for a Bebop schema.
    /// </summary>
    public override ValueTask<Artifact[]> Compile(BebopSchema schema, GeneratorConfig config,
        CancellationToken cancellationToken = default)
    {
        var artifacts = new List<Artifact>();
        Schema = schema;
        Config = config;

        var headerBuilder = new IndentedStringBuilder();
        var sourceBuilder = new IndentedStringBuilder();

        // Header file
        if (Config.EmitNotice)
        {
            headerBuilder.AppendLine("/* Auto-generated by bebop compiler */");
        }

        var headerFileName = Path.ChangeExtension(config.OutFile, ".h");
        var headerGuard = GetHeaderGuard(headerFileName);

        headerBuilder.AppendLine($"#ifndef {headerGuard}");
        headerBuilder.AppendLine($"#define {headerGuard}");
        headerBuilder.AppendLine("");
        headerBuilder.AppendLine("#include \"bebop.h\"");
        headerBuilder.AppendLine("");
        headerBuilder.AppendLine("#ifdef __cplusplus");
        headerBuilder.AppendLine("extern \"C\" {");
        headerBuilder.AppendLine("#endif");
        headerBuilder.AppendLine("");

        if (!string.IsNullOrWhiteSpace(Config.Namespace))
        {
            headerBuilder.AppendLine(FormatRegionStart($"Namespace: {Config.Namespace}"));
            headerBuilder.AppendLine();
        }

        // Source file
        if (Config.EmitNotice)
        {
            sourceBuilder.AppendLine("/* Auto-generated by bebop compiler */");
        }

        sourceBuilder.AppendLine($"#include \"{Path.GetFileName(headerFileName)}\"");
        sourceBuilder.AppendLine("#include <string.h>");
        sourceBuilder.AppendLine("");

        // Generate definitions in proper order using topological sort
        var sortedDefinitions = Schema.SortedDefinitions();

        // Step 1: Generate forward declarations for all record types
        var recordDefinitions = sortedDefinitions.OfType<RecordDefinition>().ToList();
        if (recordDefinitions.Any())
        {
            headerBuilder.RegionBlock(FormatRegionStart("Forward declarations"), 0, regionBuilder =>
            {
                foreach (var definition in recordDefinitions)
                {
                    // Check for deprecated decorator on the record definition itself
                    var isRecordDeprecated = definition.DeprecatedDecorator != null;

                    var recordDeprecatedReason = isRecordDeprecated &&
                                                 definition.DeprecatedDecorator != null &&
                                                 definition.DeprecatedDecorator.TryGetValue("reason",
                                                     out var recordReason)
                        ? recordReason
                        : null;

                    // Generate documentation for the forward declaration
                    var typeTag = $"@typedef {GetStructTypeName(definition)}";
                    var briefTag = $"Opaque handle to a {definition.Name.ToSnakeCase()} object.";

                    regionBuilder.AppendLine(FormatDocumentation(definition.Documentation, 0, isRecordDeprecated,
                        recordDeprecatedReason, briefTag, typeTag));
                    regionBuilder.AppendLine(
                        $"typedef struct {GetStructTypeName(definition)} {GetStructTypeName(definition)};");
                }
            }, FormatRegionEnd());
        }

        // Step 2: Generate enums and constants first (no dependencies)
        var enumsAndConstants = sortedDefinitions.Where(d => d is EnumDefinition or ConstDefinition).ToList();
        if (enumsAndConstants.Any())
        {
            headerBuilder.RegionBlock(FormatRegionStart("Enums and Constants"), 0, regionBuilder =>
            {
                foreach (var definition in enumsAndConstants)
                {
                    switch (definition)
                    {
                        case EnumDefinition ed:
                            // Check for deprecated decorator on the enum itself
                            var isEnumDeprecated = ed.DeprecatedDecorator != null;
                            var enumDeprecatedReason = isEnumDeprecated && ed.DeprecatedDecorator != null &&
                                                       ed.DeprecatedDecorator.TryGetValue("reason",
                                                           out var enumReason)
                                ? enumReason
                                : null;

                            var enumTypeTag = $"@enum {GetStructTypeName(ed)}";
                            var enumBriefTag = definition.Documentation.Split('\n')[0].Trim();

                            regionBuilder.AppendLine(FormatDocumentation(definition.Documentation, 0, isEnumDeprecated,
                                enumDeprecatedReason, enumBriefTag, enumTypeTag));
                            GenerateEnum(regionBuilder, ed);
                            break;
                        case ConstDefinition cd:
                            regionBuilder.AppendLine(FormatDocumentation(definition.Documentation, 0));
                            GenerateConst(regionBuilder, cd);
                            break;
                        case ServiceDefinition:
                            // Skip services for C
                            break;
                    }
                }
            }, FormatRegionEnd());
        }

        // Step 3: Generate struct definitions and their array/map types in dependency order
        if (recordDefinitions.Any())
        {
            headerBuilder.RegionBlock(FormatRegionStart("Record Definitions"), 0, regionBuilder =>
            {
                foreach (var definition in recordDefinitions)
                {
                    // Generate struct definition (without documentation since it's on the forward declaration)
                    GenerateStructDefinitionOnly(regionBuilder, definition);

                    // Then generate array/map types that depend on this struct
                    GenerateArrayMapTypesForStruct(regionBuilder, definition);

                    // Finally generate function declarations
                    GenerateFunctionDeclarations(regionBuilder, definition);
                }
            }, FormatRegionEnd());

            // Generate function implementations in source file
            sourceBuilder.RegionBlock(FormatRegionStart("Function Implementations"), 0, regionBuilder =>
            {
                foreach (var definition in recordDefinitions)
                {
                    GenerateEncodeFunctions(regionBuilder, definition);
                    GenerateDecodeFunctions(regionBuilder, definition);
                    GenerateSizeFunctions(regionBuilder, definition);
                }
            }, FormatRegionEnd());
        }

        // Generate constant implementations in source file
        GenerateConstImplementations(sourceBuilder, schema);

        // Close namespace region if applicable
        if (!string.IsNullOrWhiteSpace(Config.Namespace))
        {
            headerBuilder.AppendLine(FormatRegionEnd());
            headerBuilder.AppendLine();
        }

        // Close header guard
        headerBuilder.AppendLine("#ifdef __cplusplus");
        headerBuilder.AppendLine("}");
        headerBuilder.AppendLine("#endif");
        headerBuilder.AppendLine("");
        headerBuilder.AppendLine($"#endif /* {headerGuard} */");
        headerBuilder.AppendLine();


        // Create artifacts
        artifacts.Add(new Artifact(headerFileName, headerBuilder.TrimEnd().AppendLine().Encode()));

        var sourceFileName = Path.ChangeExtension(config.OutFile, ".c");
        artifacts.Add(new Artifact(sourceFileName, sourceBuilder.TrimEnd().AppendLine().Encode()));

        var runtime = GetRuntime();
        if (runtime is not null)
        {
            foreach (var artifact in runtime)
            {
                artifacts.Add(artifact);
            }
        }

        return ValueTask.FromResult(artifacts.ToArray());
    }

    private List<Artifact>? GetRuntime()
    {
        var assembly = Assembly.GetEntryAssembly()!;

        var header = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Equals("Compiler.bebop.h"));
        var runtime = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Contains("Compiler.bebop.c"));

        if (header is null || runtime is null)
        {
            return null;
        }

        var artifacts = new List<Artifact>();

        // Process header with version replacements
        using (var stream = assembly.GetManifestResourceStream(header)!)
        using (var reader = new StreamReader(stream))
        {
            var builder = new IndentedStringBuilder();
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is { } line)
                {
                    if (_majorRegex.IsMatch(line))
                    {
                        line = _majorRegex.Replace(line, Environment.Major.ToString());
                    }

                    if (_minorRegex.IsMatch(line))
                    {
                        line = _minorRegex.Replace(line, Environment.Minor.ToString());
                    }

                    if (_patchRegex.IsMatch(line))
                    {
                        line = _patchRegex.Replace(line, Environment.Patch.ToString());
                    }

                    if (_informationalRegex.IsMatch(line))
                    {
                        line = _informationalRegex.Replace(line, $"\"{Environment.Version}\"");
                    }

                    builder.AppendLine(line);
                }
            }

            artifacts.Add(new Artifact("bebop.h", builder.Encode()));
        }

        // Process runtime as-is (no replacements)
        using (var stream = assembly.GetManifestResourceStream(runtime)!)
        using (var reader = new StreamReader(stream))
        {
            var builder = new IndentedStringBuilder();
            while (!reader.EndOfStream)
            {
                if (reader.ReadLine() is { } line)
                {
                    builder.AppendLine(line);
                }
            }

            artifacts.Add(new Artifact("bebop.c", builder.Encode()));
        }

        return artifacts;
    }

    private void GenerateEnum(IndentedStringBuilder builder, EnumDefinition definition)
    {
        var namespacePrefix = GetNamespacePrefix();

        // Apply deprecated attribute to the enum type itself if present
        var deprecatedAttribute = "";
        if (definition.DeprecatedDecorator != null)
        {
            if (definition.DeprecatedDecorator.TryGetValue("reason", out var reason) &&
                !string.IsNullOrWhiteSpace(reason))
            {
                var escapedReason = reason.Replace("\"", "\\\"");
                deprecatedAttribute = $"BEBOP_DEPRECATED_MSG(\"{escapedReason}\") ";
            }
            else
            {
                deprecatedAttribute = "BEBOP_DEPRECATED ";
            }
        }

        builder.AppendLine($"{deprecatedAttribute}typedef enum {{");
        builder.Indent(2); // Use 2 spaces for enum members

        for (var i = 0; i < definition.Members.Count; i++)
        {
            var member = definition.Members.ElementAt(i);

            // For enum members, emit Doxygen documentation including deprecated info
            var isMemberDeprecated = member.DeprecatedDecorator != null;

            var memberDeprecatedReason = isMemberDeprecated &&
                                         member.DeprecatedDecorator != null &&
                                         member.DeprecatedDecorator.TryGetValue("reason", out var memberReason)
                ? memberReason
                : null;

            if (!string.IsNullOrWhiteSpace(member.Documentation) || isMemberDeprecated)
            {
                builder.AppendLine(FormatMemberDocumentation(member.Documentation, 0, isMemberDeprecated,
                    memberDeprecatedReason));
            }

            var enumName =
                $"{namespacePrefix.ToUpper()}{definition.Name.ToScreamingSnakeCase()}_{member.Name.ToScreamingSnakeCase()}";

            builder.AppendLine($"{enumName} = {member.ConstantValue},");
        }

        builder.Dedent(2);
        builder.AppendLine($"}} {GetStructTypeName(definition)};");
        builder.AppendLine("");
    }

    private void GenerateStructDefinitionOnly(IndentedStringBuilder headerBuilder, RecordDefinition definition)
    {
        // Apply deprecated attribute to the struct type itself if present
        var deprecatedAttribute = "";
        if (definition.DeprecatedDecorator != null)
        {
            if (definition.DeprecatedDecorator.TryGetValue("reason", out var reason) &&
                !string.IsNullOrWhiteSpace(reason))
            {
                var escapedReason = reason.Replace("\"", "\\\"");
                deprecatedAttribute = $"BEBOP_DEPRECATED_MSG(\"{escapedReason}\") ";
            }
            else
            {
                deprecatedAttribute = "BEBOP_DEPRECATED ";
            }
        }

        // Generate struct definition
        headerBuilder.AppendLine($"struct {deprecatedAttribute}{GetStructTypeName(definition)} {{");
        headerBuilder.Indent(2); // Use 2 spaces for struct members

        if (definition is FieldsDefinition fd)
        {
            foreach (var field in fd.Fields)
            {
                // Generate Doxygen documentation for fields (including deprecated info)
                var isFieldDeprecated = field.DeprecatedDecorator != null;

                var fieldDeprecatedReason = isFieldDeprecated &&
                                            field.DeprecatedDecorator != null &&
                                            field.DeprecatedDecorator.TryGetValue("reason", out var fieldReason)
                    ? fieldReason
                    : null;

                if (!string.IsNullOrWhiteSpace(field.Documentation) || isFieldDeprecated)
                {
                    headerBuilder.AppendLine(FormatMemberDocumentation(field.Documentation, 0, isFieldDeprecated,
                        fieldDeprecatedReason));
                }


                var fieldName = field.Name.ToSnakeCase();

                // Apply deprecated attribute to field if present
                var fieldDeprecatedAttribute = "";
                if (isFieldDeprecated)
                {
                    if (!string.IsNullOrWhiteSpace(fieldDeprecatedReason))
                    {
                        var escapedReason = fieldDeprecatedReason.Replace("\"", "\\\"");
                        fieldDeprecatedAttribute = $"BEBOP_DEPRECATED_MSG(\"{escapedReason}\") ";
                    }
                    else
                    {
                        fieldDeprecatedAttribute = "BEBOP_DEPRECATED ";
                    }
                }

                if (definition is MessageDefinition)
                {
                    // Use optional wrapper for message fields
                    var fieldType = GetMessageFieldTypeName(field.Type);
                    headerBuilder.AppendLine($"{fieldDeprecatedAttribute}{fieldType} {fieldName};");
                }
                else if (definition is StructDefinition sd)
                {
                    // Use const modifier for non-mutable struct fields
                    var fieldType = GetStructFieldTypeName(field.Type, sd.IsMutable);
                    headerBuilder.AppendLine($"{fieldDeprecatedAttribute}{fieldType} {fieldName};");
                }
                else
                {
                    // Regular types for other field definitions
                    var fieldType = GetCTypeName(field.Type);
                    headerBuilder.AppendLine($"{fieldDeprecatedAttribute}{fieldType} {fieldName};");
                }
            }
        }
        else if (definition is UnionDefinition ud)
        {
            headerBuilder.AppendLine("uint8_t tag;");
            headerBuilder.AppendLine("union {");
            headerBuilder.Indent(2);

            foreach (var branch in ud.Branches)
            {
                headerBuilder.AppendLine(
                    $"{GetStructTypeName(branch.Definition)} as_{branch.Definition.Name.ToSnakeCase()};");
            }

            headerBuilder.Dedent(2);
            headerBuilder.AppendLine("};");
        }

        headerBuilder.Dedent(2);
        headerBuilder.AppendLine("};");
        headerBuilder.AppendLine("");
    }

    private void GenerateArrayMapTypesForStruct(IndentedStringBuilder headerBuilder, RecordDefinition definition)
    {
        var arrayTypes = new HashSet<string>();
        var mapTypes = new HashSet<string>();

        // Collect types that use THIS struct
        CollectArrayAndMapTypesUsingStruct(definition, arrayTypes, mapTypes);

        if (arrayTypes.Count > 0)
        {
            foreach (var arrayType in arrayTypes.OrderBy(x => x))
            {
                headerBuilder.AppendLine(arrayType);
            }

            headerBuilder.AppendLine("");
        }

        if (mapTypes.Count > 0)
        {
            foreach (var mapType in mapTypes.OrderBy(x => x))
            {
                headerBuilder.AppendLine(mapType);
            }

            headerBuilder.AppendLine("");
        }
    }

    private void CollectArrayAndMapTypesUsingStruct(RecordDefinition definition, HashSet<string> arrayTypes,
        HashSet<string> mapTypes)
    {
        var structTypeName = GetStructTypeName(definition);
        var namespacePrefix = GetNamespacePrefix();

        // Remove namespace prefix and _t suffix to get the base name
        var memberTypeName = structTypeName;
        if (!string.IsNullOrEmpty(namespacePrefix))
        {
            memberTypeName = memberTypeName.Replace(namespacePrefix, "");
        }

        memberTypeName = memberTypeName.Replace("_t", "");

        // Generate array type for this struct
        arrayTypes.Add($"BEBOP_DECLARE_ARRAY_ALLOC({memberTypeName}, {structTypeName});");

        // Check if any other types in the schema use this struct in maps
        foreach (var otherDef in Schema.Definitions.Values)
        {
            if (otherDef is RecordDefinition rd)
            {
                CheckForMapUsage(rd, definition, mapTypes);
            }
        }
    }

    private void CheckForMapUsage(RecordDefinition searchIn, RecordDefinition targetStruct,
        HashSet<string> mapTypes)
    {
        if (searchIn is FieldsDefinition fd)
        {
            foreach (var field in fd.Fields)
            {
                CheckTypeForMapUsage(field.Type, targetStruct, mapTypes);
            }
        }
        else if (searchIn is UnionDefinition ud)
        {
            foreach (var branch in ud.Branches)
            {
                CheckForMapUsage(branch.Definition, targetStruct, mapTypes);
            }
        }
    }

    private void CheckTypeForMapUsage(TypeBase type, RecordDefinition targetStruct, HashSet<string> mapTypes)
    {
        switch (type)
        {
            case MapType mt:
                var targetTypeName = GetStructTypeName(targetStruct);

                if (GetCTypeName(mt.KeyType) == targetTypeName || GetCTypeName(mt.ValueType) == targetTypeName)
                {
                    var keyTypeName = GetCTypeName(mt.KeyType).Replace("_t", "").Replace("bebop_", "");
                    var valueTypeName = GetCTypeName(mt.ValueType).Replace("_t", "").Replace("bebop_", "");

                    if (IsFixedScalarType(mt.KeyType) && IsFixedScalarType(mt.ValueType))
                    {
                        mapTypes.Add(
                            $"BEBOP_DECLARE_MAP_VIEW({keyTypeName}, {GetCTypeName(mt.KeyType)}, {valueTypeName}, {GetCTypeName(mt.ValueType)});");
                    }
                    else
                    {
                        mapTypes.Add(
                            $"BEBOP_DECLARE_MAP_ALLOC({keyTypeName}, {GetCTypeName(mt.KeyType)}, {valueTypeName}, {GetCTypeName(mt.ValueType)});");
                    }
                }

                break;

            case ArrayType at:
                CollectTypesFromType(at.MemberType, new HashSet<string>(), mapTypes);
                break;
        }
    }

    private void GenerateFunctionDeclarations(IndentedStringBuilder headerBuilder, RecordDefinition definition)
    {
        var structName = GetStructTypeName(definition);
        var funcPrefix = GetNamespacePrefix() + definition.Name.ToSnakeCase();

        headerBuilder.AppendLine(
            $"bebop_result_t {funcPrefix}_encode(const {structName}* record, bebop_writer_t* writer);");
        headerBuilder.AppendLine(
            $"bebop_result_t {funcPrefix}_decode(bebop_reader_t* reader, {structName}* out_record);");
        headerBuilder.AppendLine(
            $"bebop_result_t {funcPrefix}_encode_into(const {structName}* record, bebop_writer_t* writer);");
        headerBuilder.AppendLine(
            $"bebop_result_t {funcPrefix}_decode_into(bebop_reader_t* reader, {structName}* out_record);");
        headerBuilder.AppendLine($"size_t {funcPrefix}_encoded_size(const {structName}* record);");
        headerBuilder.AppendLine($"size_t {funcPrefix}_max_encoded_size(const {structName}* record);");
        headerBuilder.AppendLine("");
    }

    private void CollectArrayAndMapTypes(Definition definition, HashSet<string> arrayTypes,
        HashSet<string> mapTypes)
    {
        switch (definition)
        {
            case FieldsDefinition fd:
                foreach (var field in fd.Fields)
                {
                    CollectTypesFromType(field.Type, arrayTypes, mapTypes);
                }

                break;
            case UnionDefinition ud:
                foreach (var branch in ud.Branches)
                {
                    CollectArrayAndMapTypes(branch.Definition, arrayTypes, mapTypes);
                }

                break;
        }
    }

    private void CollectTypesFromType(TypeBase type, HashSet<string> arrayTypes, HashSet<string> mapTypes)
    {
        switch (type)
        {
            case ArrayType at when !at.IsBytes():
                var memberTypeName = GetCTypeName(at.MemberType).Replace("_t", "").Replace("bebop_", "");

                if (IsFixedScalarType(at.MemberType))
                {
                    arrayTypes.Add($"BEBOP_DECLARE_ARRAY_VIEW({memberTypeName}, {GetCTypeName(at.MemberType)});");
                }
                else
                {
                    arrayTypes.Add($"BEBOP_DECLARE_ARRAY_ALLOC({memberTypeName}, {GetCTypeName(at.MemberType)});");
                }

                CollectTypesFromType(at.MemberType, arrayTypes, mapTypes);
                break;

            case MapType mt:
                var keyTypeName = GetCTypeName(mt.KeyType).Replace("_t", "").Replace("bebop_", "");
                var valueTypeName = GetCTypeName(mt.ValueType).Replace("_t", "").Replace("bebop_", "");

                if (IsFixedScalarType(mt.KeyType) && IsFixedScalarType(mt.ValueType))
                {
                    mapTypes.Add(
                        $"BEBOP_DECLARE_MAP_VIEW({keyTypeName}, {GetCTypeName(mt.KeyType)}, {valueTypeName}, {GetCTypeName(mt.ValueType)});");
                }
                else
                {
                    mapTypes.Add(
                        $"BEBOP_DECLARE_MAP_ALLOC({keyTypeName}, {GetCTypeName(mt.KeyType)}, {valueTypeName}, {GetCTypeName(mt.ValueType)});");
                }

                CollectTypesFromType(mt.KeyType, arrayTypes, mapTypes);
                CollectTypesFromType(mt.ValueType, arrayTypes, mapTypes);
                break;

            case DefinedType dt:
                // For defined types, also check their fields if they're records
                if (Schema.Definitions.TryGetValue(dt.Name, out var definition))
                {
                    CollectArrayAndMapTypes(definition, arrayTypes, mapTypes);
                }

                break;
        }
    }

    private void GenerateConst(IndentedStringBuilder builder, ConstDefinition definition)
    {
        var namespacePrefix = GetNamespacePrefix();
        var constName = namespacePrefix.ToUpper() + definition.Name.ToScreamingSnakeCase();
        var literal = EmitLiteral(definition.Value);

        // Generate different forms based on the type
        switch (definition.Value.Type)
        {
            case ScalarType { BaseType: BaseType.String }:
                // String constants as both #define and extern declaration
                builder.AppendLine($"#define {constName} {literal}");
                builder.AppendLine($"extern const bebop_string_view_t {constName}_VIEW;");
                break;

            case ScalarType { BaseType: BaseType.Guid }:
                // GUID constants need special handling
                builder.AppendLine($"extern const bebop_guid_t {constName};");
                break;

            case ScalarType { IsFloat: true } st:
                // Float constants with type suffixes
                var suffix = st.BaseType == BaseType.Float32 ? "f" : "";
                builder.AppendLine($"#define {constName} {literal}{suffix}");
                break;

            case ScalarType st when st.IsInteger || st.BaseType == BaseType.Bool:
                // Integer and bool constants as #define
                var typeSuffix = GetIntegerSuffix(st);
                builder.AppendLine($"#define {constName} {literal}{typeSuffix}");
                break;

            default:
                // Fallback to #define
                builder.AppendLine($"#define {constName} {literal}");
                break;
        }

        builder.AppendLine("");
    }

    private string GetIntegerSuffix(ScalarType scalarType)
    {
        return scalarType.BaseType switch
        {
            BaseType.UInt32 => "U",
            BaseType.Int64 => "LL",
            BaseType.UInt64 => "ULL",
            BaseType.UInt16 => "U",
            _ => ""
        };
    }

    private void GenerateConstImplementations(IndentedStringBuilder sourceBuilder, BebopSchema schema)
    {
        var constantDefinitions = schema.Definitions.Values.OfType<ConstDefinition>().ToList();

        if (!constantDefinitions.Any())
        {
            return;
        }

        sourceBuilder.RegionBlock(FormatRegionStart("Constant implementations"), 0, regionBuilder =>
        {
            foreach (var definition in constantDefinitions)
            {
                var namespacePrefix = GetNamespacePrefix();
                var constName = namespacePrefix.ToUpper() + definition.Name.ToScreamingSnakeCase();

                switch (definition.Value.Type)
                {
                    case ScalarType { BaseType: BaseType.String }:
                        // Implement string view constant
                        var stringLiteral = EmitLiteral(definition.Value);
                        regionBuilder.AppendLine($"const bebop_string_view_t {constName}_VIEW = {{");
                        regionBuilder.AppendLine($"    .data = {stringLiteral},");
                        regionBuilder.AppendLine($"    .length = {((StringLiteral)definition.Value).Value.Length}");
                        regionBuilder.AppendLine("};");
                        regionBuilder.AppendLine("");
                        break;

                    case ScalarType { BaseType: BaseType.Guid }:
                        // Implement GUID constant
                        var guidValue = ((GuidLiteral)definition.Value).Value;
                        regionBuilder.AppendLine($"const bebop_guid_t {constName} = {{");
                        regionBuilder.AppendLine($"    .data1 = 0x{guidValue.ToString("N")[..8]},");
                        regionBuilder.AppendLine($"    .data2 = 0x{guidValue.ToString("N")[8..12]},");
                        regionBuilder.AppendLine($"    .data3 = 0x{guidValue.ToString("N")[12..16]},");
                        regionBuilder.AppendLine("    .data4 = {");
                        var guidBytes = guidValue.ToByteArray();
                        for (var i = 8; i < 16; i++)
                        {
                            var comma = i < 15 ? "," : "";
                            regionBuilder.AppendLine($"        0x{guidBytes[i]:X2}{comma}");
                        }

                        regionBuilder.AppendLine("    }");
                        regionBuilder.AppendLine("};");
                        regionBuilder.AppendLine("");
                        break;
                }
            }
        }, FormatRegionEnd());
    }

    private void GenerateEncodeFunctions(IndentedStringBuilder builder, RecordDefinition definition)
    {
        var structName = GetStructTypeName(definition);
        var funcName = GetNamespacePrefix() + definition.Name.ToSnakeCase();

        // Helper encode_into function
        builder.AppendLine(
            $"bebop_result_t {funcName}_encode_into(const {structName}* record, bebop_writer_t* writer)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine("if (!record || !writer)");
        builder.Indent(IndentStep);
        builder.AppendLine("return BEBOP_ERROR_NULL_POINTER;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine(CompileEncode(definition));
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");

        // Convenience encode function
        builder.AppendLine($"bebop_result_t {funcName}_encode(const {structName}* record, bebop_writer_t* writer)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine($"return {funcName}_encode_into(record, writer);");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");
    }

    private void GenerateDecodeFunctions(IndentedStringBuilder builder, RecordDefinition definition)
    {
        var structName = GetStructTypeName(definition);
        var funcName = GetNamespacePrefix() + definition.Name.ToSnakeCase();

        // Helper decode_into function
        builder.AppendLine(
            $"bebop_result_t {funcName}_decode_into(bebop_reader_t* reader, {structName}* out_record)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine("if (!reader || !out_record)");
        builder.Indent(IndentStep);
        builder.AppendLine("return BEBOP_ERROR_NULL_POINTER;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine(CompileDecode(definition));
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");

        // Convenience decode function
        builder.AppendLine($"bebop_result_t {funcName}_decode(bebop_reader_t* reader, {structName}* out_record)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine($"return {funcName}_decode_into(reader, out_record);");
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");
    }

    private void GenerateSizeFunctions(IndentedStringBuilder builder, RecordDefinition definition)
    {
        var structName = GetStructTypeName(definition);
        var funcName = GetNamespacePrefix() + definition.Name.ToSnakeCase();

        // Accurate encoded size function
        builder.AppendLine($"size_t {funcName}_encoded_size(const {structName}* record)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine("if (!record)");
        builder.Indent(IndentStep);
        builder.AppendLine("return 0;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine(CompileGetEncodedSize(definition, true));
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");

        // Maximum encoded size function
        builder.AppendLine($"size_t {funcName}_max_encoded_size(const {structName}* record)");
        builder.AppendLine("{");
        builder.Indent(IndentStep);
        builder.AppendLine("if (!record)");
        builder.Indent(IndentStep);
        builder.AppendLine("return 0;");
        builder.Dedent(IndentStep);
        builder.AppendLine("");
        builder.AppendLine(CompileGetEncodedSize(definition, false));
        builder.Dedent(IndentStep);
        builder.AppendLine("}");
        builder.AppendLine("");
    }

    private string CompileGetEncodedSize(RecordDefinition definition, bool isAccurate)
    {
        var builder = new IndentedStringBuilder(IndentStep);
        builder.AppendLine("size_t byte_count = 0;");
        builder.AppendLine("");

        if (definition is FieldsDefinition fd)
        {
            if (fd is MessageDefinition)
            {
                builder.AppendLine($"byte_count += {GetMinimalEncodedSize(fd)};");
                builder.AppendLine("");
            }

            foreach (var field in fd.Fields)
            {
                if (fd is MessageDefinition)
                {
                    builder.AppendLine($"if (bebop_is_some(record->{field.Name.ToSnakeCase()})) {{");
                    builder.Indent(IndentStep);
                    // Add the field ID byte
                    builder.AppendLine("byte_count += sizeof(uint8_t);");
                    builder.AppendLine(CompileSizeAssignment(field.Type,
                        $"bebop_unwrap(record->{field.Name.ToSnakeCase()})", isAccurate, 0, 1));
                    builder.Dedent(IndentStep);
                    builder.AppendLine("}");
                    builder.AppendLine("");
                }
                else
                {
                    builder.AppendLine(CompileSizeAssignment(field.Type, $"record->{field.Name.ToSnakeCase()}",
                        isAccurate));
                    builder.AppendLine("");
                }
            }
        }
        else if (definition is UnionDefinition ud)
        {
            builder.AppendLine("byte_count += sizeof(uint32_t) + sizeof(uint8_t);");
            builder.AppendLine("");
            builder.AppendLine("switch (record->tag) {");
            foreach (var branch in ud.Branches)
            {
                var namespacePrefix = GetNamespacePrefix();
                builder.AppendLine($"case {branch.Discriminator}:");
                builder.Indent(IndentStep);
                builder.AppendLine(
                    $"byte_count += {namespacePrefix}{branch.Definition.Name.ToSnakeCase()}_{(isAccurate ? "encoded" : "max_encoded")}_size(&record->as_{branch.Definition.Name.ToSnakeCase()});");
                builder.AppendLine("break;");
                builder.Dedent(IndentStep);
            }

            builder.AppendLine("default:");
            builder.Indent(IndentStep);
            builder.AppendLine("break;");
            builder.Dedent(IndentStep);
            builder.AppendLine("}");
        }

        builder.AppendLine("return byte_count;");
        return builder.ToString();
    }

    private string CompileSizeAssignment(TypeBase type, string target, bool isAccurate, int depth = 0,
        int indentDepth = 0)
    {
        var tab = new string(' ', 4); // WebKit uses 4 spaces
        var nl = "\n" + new string(' ', indentDepth * 4);
        var i = GeneratorUtils.LoopVariable(depth);

        return type switch
        {
            ArrayType at when at.IsBytes() =>
                $"byte_count += sizeof(uint32_t) + {target}.length;",
            ArrayType at when IsFixedScalarType(at.MemberType) =>
                $"byte_count += sizeof(uint32_t) + ({target}.length * {GetMinimalEncodedSize(at.MemberType)});",
            ArrayType at =>
                "{" + nl +
                $"{tab}byte_count += sizeof(uint32_t);" + nl +
                $"{tab}for (size_t {i} = 0; {i} < {target}.length; {i}++) {{" + nl +
                $"{tab}{tab}{CompileSizeAssignment(at.MemberType, $"{target}.data[{i}]", isAccurate, depth + 1, indentDepth + 2)}" +
                nl +
                $"{tab}}}" + nl +
                "}",
            MapType mt when IsFixedScalarType(mt.KeyType) && IsFixedScalarType(mt.ValueType) =>
                $"byte_count += sizeof(uint32_t) + ({target}.length * {GetMinimalEncodedSize(mt.KeyType) + GetMinimalEncodedSize(mt.ValueType)});",
            MapType mt =>
                "byte_count += sizeof(uint32_t);" + nl +
                $"for (size_t {i} = 0; {i} < {target}.length; {i}++) {{" + nl +
                $"{tab}{CompileSizeAssignment(mt.KeyType, $"{target}.entries[{i}].key", isAccurate, depth + 1, indentDepth + 1)}" +
                nl +
                $"{tab}{CompileSizeAssignment(mt.ValueType, $"{target}.entries[{i}].value", isAccurate, depth + 1, indentDepth + 1)}" +
                nl +
                "}",
            ScalarType st => st.BaseType switch
            {
                BaseType.Bool => "byte_count += sizeof(uint8_t);",
                BaseType.Byte => "byte_count += sizeof(uint8_t);",
                BaseType.UInt16 => "byte_count += sizeof(uint16_t);",
                BaseType.Int16 => "byte_count += sizeof(int16_t);",
                BaseType.UInt32 => "byte_count += sizeof(uint32_t);",
                BaseType.Int32 => "byte_count += sizeof(int32_t);",
                BaseType.UInt64 => "byte_count += sizeof(uint64_t);",
                BaseType.Int64 => "byte_count += sizeof(int64_t);",
                BaseType.Float32 => "byte_count += sizeof(float);",
                BaseType.Float64 => "byte_count += sizeof(double);",
                BaseType.String when isAccurate => $"byte_count += sizeof(uint32_t) + {target}.length;",
                BaseType.String =>
                    $"byte_count += sizeof(uint32_t) + ({target}.length * 4); /* Max UTF-8 bytes per char */",
                BaseType.Guid => $"byte_count += {GetMinimalEncodedSize(st)};",
                BaseType.Date => $"byte_count += {GetMinimalEncodedSize(st)};",
                _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
            },
            DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                CompileSizeAssignment(ed.ScalarType, target, isAccurate, depth, indentDepth),
            DefinedType dt =>
                $"byte_count += {GetNamespacePrefix()}{dt.Name.ToSnakeCase()}_{(isAccurate ? "encoded" : "max_encoded")}_size(&{target});",
            _ => throw new InvalidOperationException($"CompileSizeAssignment: {type}")
        };
    }

    private bool IsFixedScalarType(TypeBase type)
    {
        return type switch
        {
            ScalarType st => st.BaseType switch
            {
                BaseType.Bool or BaseType.Byte or BaseType.UInt16 or BaseType.Int16 or
                    BaseType.UInt32 or BaseType.Int32 or BaseType.UInt64 or BaseType.Int64 or
                    BaseType.Float32 or BaseType.Float64 or BaseType.Guid or BaseType.Date => true,
                _ => false
            },
            DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition => true,
            _ => false
        };
    }

    private int GetMinimalEncodedSize(TypeBase type)
    {
        return type switch
        {
            ScalarType st => st.BaseType switch
            {
                BaseType.Bool => 1,
                BaseType.Byte => 1,
                BaseType.UInt16 => 2,
                BaseType.Int16 => 2,
                BaseType.UInt32 => 4,
                BaseType.Int32 => 4,
                BaseType.UInt64 => 8,
                BaseType.Int64 => 8,
                BaseType.Float32 => 4,
                BaseType.Float64 => 8,
                BaseType.String => 4, // Length prefix only
                BaseType.Guid => 16,
                BaseType.Date => 8,
                _ => throw new ArgumentOutOfRangeException(st.BaseType.ToString())
            },
            ArrayType => 4, // Length prefix only
            MapType => 4, // Length prefix only
            DefinedType dt when Schema.Definitions[dt.Name] is EnumDefinition ed =>
                GetMinimalEncodedSize(ed.ScalarType),
            DefinedType => 0, // Variable size, depends on content
            _ => throw new InvalidOperationException($"GetMinimalEncodedSize: {type}")
        };
    }

    private int GetMinimalEncodedSize(RecordDefinition definition)
    {
        return definition switch
        {
            MessageDefinition => 5, // Length prefix (4) + terminator (1)
            StructDefinition sd => sd.Fields.Sum(f => GetMinimalEncodedSize(f.Type)),
            UnionDefinition => 5, // Length prefix (4) + discriminator (1)
            _ => throw new InvalidOperationException($"GetMinimalEncodedSize: {definition}")
        };
    }

    private enum RegionStyle
    {
        Standard, // #region / #endregion
        CppStyle, // //region / //endregion
        Comment // //#region / //#endregion
    }
}