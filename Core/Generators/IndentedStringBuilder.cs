using System;
using System.Linq;
using System.Text;
using Core.Meta.Extensions;

namespace Core.Generators
{
    public class IndentedStringBuilder
    {
        private int Spaces { get; set; }
        private StringBuilder Builder { get; }


        public IndentedStringBuilder(int spaces = 0)
        {
            Spaces = spaces;
            Builder = new StringBuilder();
        }

        public IndentedStringBuilder AppendLine()
        {
            return AppendLine(Environment.NewLine);
        }

        public IndentedStringBuilder Append(string text)
        {
            var indent = new string(' ', Spaces);
            var lines = text.GetLines();
            var indentedLines = lines.Select(x => (indent + x).TrimEnd()).ToArray();
            var indentedText = string.Join(Environment.NewLine, indentedLines).TrimEnd();
            Builder.Append(indentedText);
            return this;
        }

        /// <summary>
        /// Append text in the middle of the current line; includes no newline and no indent and the beginning.
        /// </summary>
        public IndentedStringBuilder AppendMid(string text)
        {
            if (text.GetLines().Length > 1)
            {
                throw new ArgumentException("AppendMid must not contain multiple lines");
            }

            Builder.Append(text);
            return this;
        }

        /// <summary>
        /// Append the last part of a line. This will add a new line but no indent at the beginning.
        /// </summary>
        public IndentedStringBuilder AppendEnd(string text)
        {
            if (text.GetLines().Length > 1)
            {
                throw new ArgumentException("AppendEnd must not contain multiple lines");
            }

            Builder.AppendLine(text.TrimEnd());
            return this;
        }

        public IndentedStringBuilder AppendLine(string text)
        {
            var indent = new string(' ', Spaces);
            var lines = text.GetLines();
            var indentedLines = lines.Select(x => (indent + x).TrimEnd()).ToArray();
            var indentedText = string.Join(Environment.NewLine, indentedLines).TrimEnd();
            Builder.AppendLine(indentedText);
            return this;
        }

        public IndentedStringBuilder Indent(int addSpaces = 0)
        {
            Spaces = Math.Max(0, Spaces + addSpaces);
            return this;
        }

        public IndentedStringBuilder Dedent(int removeSpaces = 0)
        {
            Spaces = Math.Max(0, Spaces - removeSpaces);
            return this;
        }

        public IndentedStringBuilder RegionBlock(string regionStart, int spaces, Action<IndentedStringBuilder> fn,
            string endRegion)
        {
            AppendLine();
            AppendLine(regionStart);
            AppendLine();

            var regionBuilder = new IndentedStringBuilder(Spaces + spaces);
            fn(regionBuilder);
            var regionContent = regionBuilder.ToString().Trim();

            if (!string.IsNullOrEmpty(regionContent))
            {
                Builder.Append(regionContent);
            }

            AppendLine();
            AppendLine();
            AppendLine(endRegion);
            AppendLine();
            return this;
        }

        /// <summary>
        /// Write a new scope and take a lambda to write to the builder within it. This way it is easy to ensure the
        /// scope is closed correctly.
        /// </summary>
        public IndentedStringBuilder CodeBlock(string openingLine, int spaces, Action fn, string open = "{",
            string close = "}")
        {
            if (!string.IsNullOrEmpty(openingLine))
            {
                Append(openingLine);
                AppendEnd($" {open}");
            }
            else
            {
                AppendLine(open);
            }

            Indent(spaces);
            fn();
            Dedent(spaces);
            AppendLine(close);
            return this;
        }

        /// <summary>
        /// Trim whitespace from the start of the accumulated content.
        /// </summary>
        public IndentedStringBuilder TrimStart()
        {
            var content = Builder.ToString().TrimStart();
            Builder.Clear();
            Builder.Append(content);
            return this;
        }

        /// <summary>
        /// Trim whitespace from the end of the accumulated content.
        /// </summary>
        public IndentedStringBuilder TrimEnd()
        {
            var content = Builder.ToString().TrimEnd();
            Builder.Clear();
            Builder.Append(content);
            return this;
        }

        /// <summary>
        /// Trim whitespace from both the start and end of the accumulated content.
        /// </summary>
        public IndentedStringBuilder Trim()
        {
            var content = Builder.ToString().Trim();
            Builder.Clear();
            Builder.Append(content);
            return this;
        }

        public byte[] Encode(bool encoderShouldEmitUTF8Identifier = false){
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier);
            return encoding.GetBytes(this.ToString());
        }

        public override string ToString()
        {
            return Builder.ToString();
        }
    }
}