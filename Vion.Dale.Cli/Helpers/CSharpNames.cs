using System;
using System.Linq;

namespace Vion.Dale.Cli.Helpers
{
    /// <summary>
    ///     The two name shapes the generators write into a consumer's source: an identifier (a class, a
    ///     property, a method) and a type reference. Checked here rather than left to the compiler, because
    ///     a generator that reports success and leaves a file that will not build sends the author looking
    ///     for a fault in code they did not write. Regex-and-lexing rather than Roslyn, like the rest of
    ///     this area (`Vion.Dale.Cli/CLAUDE.md`, "Source manipulation is regex-based, not Roslyn").
    /// </summary>
    internal static class CSharpNames
    {
        /// <summary>
        ///     Whether the value is a legal C# identifier: a letter or underscore, then letters, digits or
        ///     underscores. Verbatim identifiers and Unicode escapes are deliberately not accepted — a
        ///     generator has no reason to write one.
        /// </summary>
        internal static bool IsIdentifier(string? value)
        {
            if (string.IsNullOrEmpty(value) || !(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            return value.All(character => char.IsLetterOrDigit(character) || character == '_');
        }

        /// <summary>
        ///     Whether the value reads as a C# type reference — an identifier, optionally dotted, with any
        ///     mix of generic arguments, array ranks and a nullable mark. Deliberately permissive about what
        ///     the SDK knows: the set of legal property types is the compiler's business, and this check
        ///     exists only to keep a value that cannot be a type at all out of the emitted declaration.
        /// </summary>
        internal static bool IsTypeReference(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Every rejection below lands on the per-segment identifier check: a segment carrying a space,
            // a semicolon or a bracket is not an identifier. Spaces *inside* generic arguments are legal
            // and reach the recursive call, which trims them.
            var core = value!.Trim().TrimEnd('?');
            while (core.EndsWith("[]", StringComparison.Ordinal))
            {
                core = core.Substring(0, core.Length - 2).TrimEnd('?');
            }

            var genericStart = core.IndexOf('<');
            if (genericStart >= 0)
            {
                if (!core.EndsWith(">", StringComparison.Ordinal))
                {
                    return false;
                }

                var arguments = core.Substring(genericStart + 1, core.Length - genericStart - 2);
                core = core.Substring(0, genericStart);
                if (arguments.Length == 0 || !arguments.Split(',').All(IsTypeReference))
                {
                    return false;
                }
            }

            var segments = core.Split('.');
            return segments.Length > 0 && segments.All(IsIdentifier);
        }

        internal static string DescribeInvalidIdentifier(string what, string? value)
        {
            return $"Invalid {what} '{value}'. Use a letter or underscore, then letters, digits or underscores.";
        }

        internal static string DescribeInvalidTypeReference(string? value)
        {
            return $"'{value}' is not a C# type. Pass a type name such as `double`, `string?`, `int[]` or `MyNamespace.MyEnum`.";
        }
    }
}
