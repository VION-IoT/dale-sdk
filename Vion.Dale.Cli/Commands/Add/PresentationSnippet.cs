using System.Collections.Generic;

namespace Vion.Dale.Cli.Commands.Add
{
    /// <summary>
    ///     Shared builder for the <c>[Presentation(...)]</c> attribute emitted by
    ///     <c>dale add property</c> and <c>dale add measuringpoint</c>.
    /// </summary>
    internal static class PresentationSnippet
    {
        /// <summary>Well-known <see cref="Vion.Dale.Sdk.Core.PropertyGroup" /> constant names.</summary>
        // SYNC: keep in lockstep with Vion.Dale.Sdk.Core.PropertyGroup constant names (CLI has no SDK ref)
        internal static readonly string[] KnownGroups =
        {
            "Identity", "Status", "Configuration", "Metric", "Diagnostics", "Alarm",
        };

        internal static readonly string[] Importances =
        {
            "Primary", "Secondary", "Normal", "Hidden",
        };

        /// <summary>
        ///     Escapes a user-supplied value for safe interpolation into an emitted C#
        ///     string literal (<c>"..."</c>): backslashes and double quotes.
        /// </summary>
        internal static string EscapeCsString(string value)
            => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        ///     Builds a single <c>[Presentation(...)]</c> attribute line, or <c>null</c> when
        ///     none of the four flags were supplied (so callers emit no empty attribute).
        ///     Argument order is stable and readable: Group, Importance, Decimals, Format.
        /// </summary>
        internal static string? Build(string? group, string? importance, int? decimals, string? format)
        {
            var args = new List<string>();

            if (!string.IsNullOrWhiteSpace(group))
            {
                args.Add($"Group = {RenderGroup(group!)}");
            }

            if (importance != null)
            {
                args.Add($"Importance = Importance.{importance}");
            }

            if (decimals.HasValue)
            {
                args.Add($"Decimals = {decimals.Value}");
            }

            if (!string.IsNullOrWhiteSpace(format))
            {
                args.Add($"Format = \"{EscapeCsString(format!)}\"");
            }

            if (args.Count == 0)
            {
                return null;
            }

            return $"[Presentation({string.Join(", ", args)})]";
        }

        /// <summary>
        ///     Maps a known <see cref="Vion.Dale.Sdk.Core.PropertyGroup" /> constant name
        ///     (case-insensitive) to its C# member reference; otherwise emits a string literal.
        /// </summary>
        private static string RenderGroup(string group)
        {
            foreach (var known in KnownGroups)
            {
                if (string.Equals(known, group, System.StringComparison.OrdinalIgnoreCase))
                {
                    return $"PropertyGroup.{known}";
                }
            }

            return $"\"{EscapeCsString(group)}\"";
        }
    }
}
