using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Core.Lexer.Extensions;

namespace Core.Meta.Extensions
{
    public static partial class StringExtensions
    {
        private const char SnakeSeparator = '_';
        private const char KebabSeparator = '-';

        private static readonly string[] NewLines = { "\r\n", "\r", "\n" };

        public static string ReplaceLastOccurrence(this string source, string find, string replace)
        {
            int place = source.LastIndexOf(find, StringComparison.Ordinal);

            if (place == -1)
                return source;

            return source.Remove(place, find.Length).Insert(place, replace);
        }


        /// <summary>
        ///     Splits the specified <paramref name="value"/> based on line ending.
        /// </summary>
        /// <param name="value">The input string to split.</param>
        /// <returns>An array of each line in the string.</returns>
        public static string[] GetLines(this string value) => string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : value.Split(NewLines, StringSplitOptions.None);

        /// <summary>
        ///     Determines if the specified char array contains only uppercase characters.
        /// </summary>
        private static bool IsUpper(this Span<char> array)
        {
            foreach (var currentChar in array)
            {
                if (!char.IsUpper(currentChar) && currentChar is not (SnakeSeparator or KebabSeparator))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        ///     Converts the specified <paramref name="input"/> string into PascalCase.
        /// </summary>
        /// <param name="input">The input string that will be converted.</param>
        /// <returns>The mutated string.</returns>
        public static string ToPascalCase(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            // DO capitalize both characters on two-character acronyms.
            if (input.Length <= 2)
            {
                return input.ToUpper();
            }
            // Remove invalid characters.
            var charArray = new Span<char>(input.ToCharArray());
            // Set first letter to uppercase
            if (char.IsLower(charArray[0]))
            {
                charArray[0] = char.ToUpperInvariant(charArray[0]);
            }

            // DO capitalize only the first character of acronyms with three or more characters, except the first word.
            // DO NOT capitalize any of the characters of any acronyms, whatever their length.
            if (charArray.IsUpper())
            {
                // Replace all characters following the first to lowercase when the entire string is uppercase (ABC -> Abc)
                for (var i = 1; i < charArray.Length; i++)
                {
                    charArray[i] = char.ToLowerInvariant(charArray[i]);
                }
            }

            for (var i = 1; i < charArray.Length; i++)
            {
                var currentChar = charArray[i];
                var lastChar = charArray.Peek(i is 1 ? 1 : i - 1);
                var nextChar = charArray.Peek(i + 1);

                if (currentChar.IsDecimalDigit() && char.IsLower(nextChar))
                {
                    charArray[i + 1] = char.ToUpperInvariant(nextChar);
                }
                else if (currentChar is SnakeSeparator or KebabSeparator)
                {
                    if (char.IsLower(nextChar))
                    {
                        charArray[i + 1] = char.ToUpperInvariant(nextChar);
                    }
                    if (char.IsUpper(lastChar))
                    {
                        charArray[i - 1] = char.ToLowerInvariant(lastChar);
                    }
                }
            }
            return new string(charArray.ToArray().Where(c => c is not (SnakeSeparator or KebabSeparator)).ToArray());
        }

        /// <summary>
        ///     Peeks a char at the specified <paramref name="index"/> from the provided <paramref name="array"/>
        /// </summary>
        private static char Peek(this Span<char> array, int index)
        {
            return index < array.Length && index >= 0 ? array[index] : default;
        }

        /// <summary>
        ///     Converts the specified <paramref name="input"/> string into camelCase.
        /// </summary>
        /// <param name="input">The input string that will be converted.</param>
        /// <returns>The mutated string.</returns>
        public static string ToCamelCase(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            if (input.Length == 1)
            {
                return input;
            }
            // Pascal is a subset of camelCase. The first letter of Pascal is capital and first letter of the camel is small
            var converted = input.ToPascalCase();
            var f = converted[..1];
            var r = converted[1..];

            if (char.IsUpper(f[0]) && char.IsUpper(r[0]))
            {
                return input;
            }

            return f.ToLowerInvariant() + r;
        }

        /// <summary>
        ///     Converts the specified <paramref name="input"/> string into snake_case.
        /// </summary>
        /// <param name="input">The input string that will be converted.</param>
        /// <returns>The mutated string.</returns>
        public static string ToSnakeCase(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }
            if (input.Length == 1)
            {
                return input;
            }
            // Remove invalid characters.
            var charArray = new Span<char>(input.ToCharArray());
            var builder = new StringBuilder();

            for (var i = 0; i < charArray.Length; i++)
            {
                var currentChar = charArray[i];
                var nextChar = charArray.Peek(i + 1);

                if (currentChar is SnakeSeparator or KebabSeparator)
                {
                    builder.Append(SnakeSeparator);
                }
                else if (char.IsLower(currentChar) && char.IsUpper(nextChar))
                {
                    builder.Append(char.ToLowerInvariant(currentChar));
                    builder.Append('_');
                }
                else if (char.IsUpper(currentChar))
                {
                    builder.Append(char.ToLowerInvariant(currentChar));
                }
                else
                {
                    builder.Append(currentChar);
                }
            }
            return builder.ToString();
        }

        public static string ToScreamingSnakeCase(this string input)
        {
            return input.ToSnakeCase().ToUpper();
        }

        /// <summary>
        ///     Converts the specified <paramref name="input"/> string into kebab-case.
        /// </summary>
        /// <param name="input">The input string that will be converted.</param>
        /// <returns>The mutated string.</returns>
        public static string ToKebabCase(this string input) => input.ToSnakeCase().Replace(SnakeSeparator, KebabSeparator);

        public static bool TryParseUInt(this string str, out uint result)
        {
            if (uint.TryParse(str, out result))
            {
                return true;
            }
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    result = Convert.ToUInt32(str, 16);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public static void PrettyPrint(byte[] data)
        {
            StringBuilder sb = new StringBuilder();
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                // Add byte in hex to string
                sb.Append($"{data[i]:x2} ");

                // If byte represents a printable ASCII character, add it to the text string
                if (data[i] >= 0x20 && data[i] < 0x7F)
                {
                    text.Append((char)data[i]);
                }
                else
                {
                    // Replace non-printable characters with '.'
                    text.Append('.');
                }

                // Every 16 bytes, print the hex and text strings, then clear them
                if ((i + 1) % 16 == 0)
                {
                    Console.WriteLine($"{sb}  {text}");
                    sb.Clear();
                    text.Clear();
                }
            }

            // Print any remaining bytes
            if (sb.Length > 0)
            {
                // Pad the hex string to align with the previous lines
                while (sb.Length < 48)
                {
                    sb.Append(' ');
                }
                Console.WriteLine($"{sb}  {text}");
            }
        }

        public static string ConvertToString(this byte[] byteArray, string leftBrace = "[", string rightBrace = "];")
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(leftBrace);

            int printWidth = 60; // Prettier default print width
            int currentLineWidth = 0;

            for (int i = 0; i < byteArray.Length; i++)
            {
                string byteString = byteArray[i].ToString();
                if (i != byteArray.Length - 1)
                {
                    byteString += ", ";
                }

                currentLineWidth += byteString.Length;

                // add new line if the current line width with this byte exceeds print width
                if (currentLineWidth >= printWidth && i < byteArray.Length - 1)
                {
                    builder.AppendLine();
                    currentLineWidth = byteString.Length; // reset the line width
                }

                builder.Append(byteString);
            }

            // Append a new line to de-indent the closing brackets.
            builder.AppendLine();
            builder.Append(rightBrace);

            return builder.ToString();
        }


        public static bool IsLegalPath(this string path, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            // Check for invalid path characters
            var invalidPathChars = Path.GetInvalidPathChars();
            var invalidPathCharIndex = path.IndexOfAny(invalidPathChars);
            if (invalidPathCharIndex >= 0)
            {
                index = invalidPathCharIndex;
                return false;
            }
            return true;
        }

        public static bool IsLegalFilePath(this string filePath, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            // Check for invalid path characters in the entire filePath
            if (!IsLegalPath(filePath, out index))
            {
                return false;
            }

            // Extract the file name from the path and check for invalid file name characters
            var fileName = Path.GetFileName(filePath);
            var invalidFileNameChars = Path.GetInvalidFileNameChars();
            var invalidFileNameCharIndex = fileName.IndexOfAny(invalidFileNameChars);
            if (invalidFileNameCharIndex >= 0)
            {
                // Adjust the index to be in the context of the full filePath, not just fileName
                index = filePath.LastIndexOf(fileName, StringComparison.Ordinal) + invalidFileNameCharIndex;
                return false;
            }
            return true;
        }

        public static bool IsLegalPathGlob(this string pathOrGlob)
        {
            if (string.IsNullOrWhiteSpace(pathOrGlob))
            {
                return false;
            }
            if (LegalPathGlobRegex().IsMatch(pathOrGlob))
            {
                return true;
            }
            return true;
        }

        public static bool IsLegalFileGlobal(this string fileGlob)
        {
            if (string.IsNullOrWhiteSpace(fileGlob))
            {
                return false;
            }
            if (LegalFileGlobRegex().IsMatch(fileGlob))
            {
                return true;
            }
            return true;
        }

        public static bool IsValidNamespace(this string @namespace)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
            {
                return false;
            }
            return NamespaceRegex().IsMatch(@namespace);
        }

        public static bool IsLegalPathOrGlob(this string pathOrGlob, out int invalidIndex)
        {
            invalidIndex = -1;
            if (string.IsNullOrWhiteSpace(pathOrGlob))
            {
                return false;
            }
            if (IsLegalPathGlob(pathOrGlob))
            {
                return true;
            }
            if (IsLegalPath(pathOrGlob, out invalidIndex))
            {
                return true;
            }
            return false;
        }
        public static bool IsLegalFilePathOrGlob(this string filePathOrGlob, out int invalidIndex)
        {
            invalidIndex = -1;
            if (string.IsNullOrWhiteSpace(filePathOrGlob))
            {
                return false;
            }
            if (IsLegalFileGlobal(filePathOrGlob))
            {
                return true;
            }
            if (IsLegalFilePath(filePathOrGlob, out invalidIndex))
            {
                return true;
            }
            return false;
        }

        public static bool IsPathAttemptingTraversal(this string path)
        {
            // Split the path into segments
            var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.None);

            // Keep track of the depth
            int depth = 0;

            foreach (var segment in segments)
            {
                if (segment == "..")
                {
                    // Going up one directory
                    depth--;

                    // If depth goes negative, it's attempting to go above the root
                    if (depth < 0)
                    {
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(segment) && segment != ".")
                {
                    // Normal directory, go one level deeper
                    depth++;
                }
            }

            return false;
        }


        /// <summary>
        /// Escapes a string with \u0000 style escape sequences
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string EscapeString(this string? value)
        {
            ArgumentNullException.ThrowIfNull(value);

            StringBuilder escaped = new StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': escaped.Append(@"\\"); break;
                    case '\"': escaped.Append("\\\""); break;
                    case '\b': escaped.Append("\\b"); break;
                    case '\f': escaped.Append("\\f"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (c < 32 || c > 127)
                        {
                            escaped.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            escaped.Append(c);
                        }
                        break;
                }
            }
            return escaped.ToString();
        }


        [GeneratedRegex(@"^(\*\*\/|.*\/)$")]
        private static partial Regex LegalPathGlobRegex();

        [GeneratedRegex(@"^.*[\*\?\[\]].*(\.[a-zA-Z0-9]+)?$")]
        private static partial Regex LegalFileGlobRegex();

        [GeneratedRegex(@"^[a-zA-Z]+(\.[a-zA-Z]+)*$")]
        private static partial Regex NamespaceRegex();
    }
}
