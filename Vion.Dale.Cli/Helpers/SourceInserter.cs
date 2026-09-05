using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Vion.Dale.Cli.Helpers
{
    /// <summary>
    ///     Shared logic for inserting code into LogicBlock source files.
    ///     Used by add serviceproperty, add timer, etc.
    /// </summary>
    public static class SourceInserter
    {
        /// <summary>
        ///     Resolve which LogicBlock to target.
        ///     Returns null and prints error if ambiguous or not found.
        /// </summary>
        public static LogicBlockInfo? ResolveTarget(List<LogicBlockInfo> logicBlocks, string? toOption)
        {
            if (logicBlocks.Count == 0)
            {
                return null;
            }

            if (toOption != null)
            {
                return logicBlocks.FirstOrDefault(lb => string.Equals(lb.ClassName, toOption, StringComparison.OrdinalIgnoreCase));
            }

            if (logicBlocks.Count == 1)
            {
                return logicBlocks[0];
            }

            // Ambiguous
            return null;
        }

        /// <summary>
        ///     Insert a code snippet before the last closing brace of the target class.
        ///     Returns true on success.
        /// </summary>
        public static bool InsertIntoClass(string filePath, string className, string snippet)
        {
            var source = SourceText.Read(filePath);
            var lines = source.Lines;
            var insertIndex = FindClassClosingBrace(lines, className);
            if (insertIndex < 0)
            {
                return false;
            }

            // Detect indentation of class members
            var indent = DetectMemberIndentation(lines, insertIndex);

            // Add a blank line separator if the line before isn't blank
            if (insertIndex > 0 && !string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
            {
                lines.Insert(insertIndex, "");
                insertIndex++;
            }

            // Indent and insert the snippet
            var snippetLines = snippet.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (var i = 0; i < snippetLines.Length; i++)
            {
                var line = snippetLines[i];
                var indentedLine = string.IsNullOrWhiteSpace(line) ? "" : indent + line;
                lines.Insert(insertIndex + i, indentedLine);
            }

            source.Write(filePath);
            return true;
        }

        /// <summary>
        ///     Ensure a using statement exists at the top of the file.
        /// </summary>
        public static void EnsureUsing(string filePath, string usingNamespace)
        {
            var usingStatement = $"using {usingNamespace};";
            var source = SourceText.Read(filePath);
            var lines = source.Lines;

            if (lines.Any(line => line.Trim() == usingStatement))
            {
                return;
            }

            // Find last using statement and insert after it
            var lastUsingIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("using ") && lines[i].TrimEnd().EndsWith(";"))
                {
                    lastUsingIndex = i;
                }
            }

            lines.Insert(lastUsingIndex >= 0 ? lastUsingIndex + 1 : 0, usingStatement);
            source.Write(filePath);
        }

        /// <summary>
        ///     A source file read as lines, remembering the bytes that are not lines: its byte-order mark,
        ///     its line ending and whether it ended with one. Writing it back changes only the lines that
        ///     changed — <c>File.WriteAllLines</c> rewrites every terminator to the platform's and drops the
        ///     mark, which turns one added property into a whole-file diff (both this repository and the
        ///     first consumer declare <c>* text=auto eol=lf</c>, and this repository gates the mark with
        ///     <c>bom-lint</c>).
        /// </summary>
        internal sealed class SourceText
        {
            private SourceText(List<string> lines, string newLine, bool hasByteOrderMark, bool endsWithNewLine)
            {
                Lines = lines;
                NewLine = newLine;
                HasByteOrderMark = hasByteOrderMark;
                EndsWithNewLine = endsWithNewLine;
            }

            public List<string> Lines { get; }

            public string NewLine { get; }

            public bool HasByteOrderMark { get; }

            private bool EndsWithNewLine { get; }

            public static SourceText Read(string filePath)
            {
                var bytes = File.ReadAllBytes(filePath);
                var hasByteOrderMark = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var offset = hasByteOrderMark ? 3 : 0;
                var text = new UTF8Encoding(false).GetString(bytes, offset, bytes.Length - offset);

                var crlfCount = CountOccurrences(text, "\r\n");
                var bareLineFeedCount = CountOccurrences(text, "\n") - crlfCount;
                var newLine = crlfCount >= bareLineFeedCount ? "\r\n" : "\n";

                var lines = LineBreakPattern.Split(text).ToList();
                var endsWithNewLine = lines.Count > 1 && lines[^1].Length == 0;
                if (endsWithNewLine)
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                return new SourceText(lines, newLine, hasByteOrderMark, endsWithNewLine);
            }

            public void Write(string filePath)
            {
                var text = string.Join(NewLine, Lines) + (EndsWithNewLine ? NewLine : string.Empty);

                // WriteAllText emits the encoding's preamble; GetBytes does not, so a mark would be lost.
                File.WriteAllText(filePath, text, new UTF8Encoding(HasByteOrderMark));
            }

            private static int CountOccurrences(string text, string value)
            {
                var count = 0;
                for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>
        ///     The declaration of a member called <paramref name="memberName" />, with the attribute lines
        ///     directly above it, or null when the source declares no such member. Regex over the source
        ///     like the rest of this class — the shape it recognises is a property declaration, which is what
        ///     both annotations attach to.
        /// </summary>
        public static MemberDeclaration? FindMember(string sourceContent, string memberName)
        {
            var lines = LineBreakPattern.Split(sourceContent);
            var declaration = new Regex($@"^\s*(?<modifiers>[\w\s]*?)\b(?<type>[\w.<>\[\]?,]+)\s+{Regex.Escape(memberName)}\s*(\{{|=>)");

            for (var i = 0; i < lines.Length; i++)
            {
                var match = declaration.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }

                var attributes = new List<string>();
                for (var above = i - 1; above >= 0 && lines[above].TrimStart().StartsWith("["); above--)
                {
                    attributes.Insert(0, lines[above].Trim());
                }

                return new MemberDeclaration(match.Groups["type"].Value, attributes);
            }

            return null;
        }

        /// <summary>
        ///     Insert <paramref name="attributeLine" /> directly above the declaration of
        ///     <paramref name="memberName" />, at that declaration's own indentation. Returns false when the
        ///     member is not there.
        /// </summary>
        public static bool AddAttributeToMember(string filePath, string memberName, string attributeLine)
        {
            var source = SourceText.Read(filePath);
            var declaration = new Regex($@"^\s*(?<modifiers>[\w\s]*?)\b(?<type>[\w.<>\[\]?,]+)\s+{Regex.Escape(memberName)}\s*(\{{|=>)");

            for (var i = 0; i < source.Lines.Count; i++)
            {
                if (!declaration.IsMatch(source.Lines[i]))
                {
                    continue;
                }

                var insertIndex = i;
                while (insertIndex > 0 && source.Lines[insertIndex - 1].TrimStart().StartsWith("["))
                {
                    insertIndex--;
                }

                var line = source.Lines[i];
                var indent = line.Substring(0, line.Length - line.TrimStart().Length);
                source.Lines.Insert(insertIndex, indent + attributeLine);
                source.Write(filePath);
                return true;
            }

            return false;
        }

        /// <summary>A member declaration as the generators need to judge it: its type, and its attributes.</summary>
        public sealed class MemberDeclaration
        {
            internal MemberDeclaration(string declaredType, IReadOnlyList<string> attributes)
            {
                DeclaredType = declaredType;
                Attributes = attributes;
            }

            public string DeclaredType { get; }

            public IReadOnlyList<string> Attributes { get; }

            /// <summary>Whether an attribute of this name is already on the member, with or without arguments.</summary>
            public bool CarriesAttribute(string attributeName)
            {
                return Attributes.Any(attribute => Regex.IsMatch(attribute, $@"\[\s*{Regex.Escape(attributeName)}\s*[(\]]"));
            }

            /// <summary>Whether the member is a property declared with exactly this type.</summary>
            public bool IsPropertyOfType(string type)
            {
                return string.Equals(DeclaredType, type, StringComparison.Ordinal);
            }
        }

        private static readonly Regex LineBreakPattern = new("\\r\\n|\\n|\\r", RegexOptions.Compiled);

        private static int FindClassClosingBrace(List<string> lines, string className)
        {
            var classPattern = new Regex($@"\bclass\s+{Regex.Escape(className)}\b");
            var inClass = false;
            var braceDepth = 0;
            var openingSeen = false;

            for (var i = 0; i < lines.Count; i++)
            {
                if (!inClass && classPattern.IsMatch(lines[i]))
                {
                    inClass = true;
                }

                if (inClass)
                {
                    foreach (var ch in lines[i])
                    {
                        if (ch == '{')
                        {
                            braceDepth++;
                            openingSeen = true;
                        }

                        if (ch == '}')
                        {
                            braceDepth--;
                        }

                        if (openingSeen && braceDepth == 0)
                        {
                            return i; // This line contains the closing brace
                        }
                    }
                }
            }

            return -1;
        }

        private static string DetectMemberIndentation(List<string> lines, int closingBraceIndex)
        {
            // Look at lines above the closing brace for member indentation
            for (var i = closingBraceIndex - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var trimmed = line.TrimStart();

                    // Skip structural lines — these are not members
                    if (trimmed == "{" || trimmed == "}" || trimmed.Contains(" class ") || trimmed.StartsWith("class ") || trimmed.StartsWith("namespace "))
                    {
                        continue;
                    }

                    var leadingWhitespace = line.Substring(0, line.Length - trimmed.Length);

                    // If this looks like a member (attribute, property, method), use its indentation
                    if (trimmed.StartsWith("[") || trimmed.StartsWith("public ") || trimmed.StartsWith("private ") || trimmed.StartsWith("protected ") ||
                        trimmed.StartsWith("internal ") || trimmed.StartsWith("//"))
                    {
                        return leadingWhitespace;
                    }
                }
            }

            // No members found — derive from the closing brace indentation + one level
            var closingLine = lines[closingBraceIndex];
            var braceIndent = closingLine.Substring(0, closingLine.Length - closingLine.TrimStart().Length);
            return braceIndent + "    ";
        }
    }
}