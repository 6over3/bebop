﻿using System;
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

        public override string ToString()
        {
            return Builder.ToString();
        }
    }
}
