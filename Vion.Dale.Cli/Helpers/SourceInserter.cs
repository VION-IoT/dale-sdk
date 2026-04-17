using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var lines = File.ReadAllLines(filePath).ToList();
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

            File.WriteAllLines(filePath, lines);
            return true;
        }

        /// <summary>
        ///     Ensure a using statement exists at the top of the file.
        /// </summary>
        public static void EnsureUsing(string filePath, string usingNamespace)
        {
            var content = File.ReadAllText(filePath);
            var usingStatement = $"using {usingNamespace};";

            if (content.Contains(usingStatement))
            {
                return;
            }

            var lines = File.ReadAllLines(filePath).ToList();

            // Find last using statement and insert after it
            var lastUsingIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("using ") && lines[i].TrimEnd().EndsWith(";"))
                {
                    lastUsingIndex = i;
                }
            }

            if (lastUsingIndex >= 0)
            {
                lines.Insert(lastUsingIndex + 1, usingStatement);
            }
            else
            {
                lines.Insert(0, usingStatement);
            }

            File.WriteAllLines(filePath, lines);
        }

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
                    if (trimmed == "{" || trimmed == "}" ||
                        trimmed.Contains(" class ") || trimmed.StartsWith("class ") ||
                        trimmed.StartsWith("namespace "))
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