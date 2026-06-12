using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     Scenario-file discovery and persistence (RFC 0006): <c>&lt;id&gt;.scenario.json</c> files under the
    ///     scenarios directory (default <c>{cwd}/scenarios</c>, overridable via
    ///     <c>DevConfigurationBuilder.WithScenarios</c>). The directory is re-scanned on every read — editing
    ///     a file in the IDE is picked up by the next request, no watcher state to invalidate. Saving is
    ///     path-confined to the directory and disabled by <c>DALE_DEVHOST_READONLY_SCENARIOS=1</c>.
    /// </summary>
    public sealed class ScenarioStore
    {
        public const string FileSuffix = ".scenario.json";

        /// <summary>Set to <c>1</c> to reject saves (<c>PUT /api/scenarios/{id}</c>) — CI / locked-down checkouts.</summary>
        public const string ReadOnlyEnvVar = "DALE_DEVHOST_READONLY_SCENARIOS";

        private static readonly Regex IdSlug = new("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        /// <summary>The absolute scenarios directory this store serves.</summary>
        public string Directory { get; }

        public static bool IsReadOnly
        {
            get => Environment.GetEnvironmentVariable(ReadOnlyEnvVar) == "1";
        }

        public ScenarioStore(string? directory = null)
        {
            Directory = Path.GetFullPath(directory ?? Path.Combine(Environment.CurrentDirectory, "scenarios"));
        }

        /// <summary>
        ///     Discover all scenario files. Files that fail structural parsing still appear, carrying their
        ///     error — a broken scenario must be visible, not silently absent.
        /// </summary>
        public IReadOnlyList<ScenarioListEntry> List()
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<ScenarioListEntry>();
            }

            return System.IO
                         .Directory
                         .EnumerateFiles(Directory, "*" + FileSuffix)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                         .Select(path =>
                                 {
                                     var id = Path.GetFileName(path);
                                     id = id.Substring(0, id.Length - FileSuffix.Length);
                                     try
                                     {
                                         var file = ScenarioFile.Load(path);
                                         return new ScenarioListEntry
                                                {
                                                    Id = id,
                                                    Title = file.Title,
                                                    Topology = file.Topology,
                                                    Specs = file.Specs ?? Array.Empty<string>(),
                                                    FileName = Path.GetFileName(path),
                                                };
                                     }
                                     catch (ScenarioFormatException e)
                                     {
                                         return new ScenarioListEntry { Id = id, FileName = Path.GetFileName(path), Error = e.Message };
                                     }
                                     catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                                     {
                                         // A locked or deleted-mid-scan file must not take down discovery —
                                         // broken stays visible, unreadable does too.
                                         return new ScenarioListEntry { Id = id, FileName = Path.GetFileName(path), Error = e.Message };
                                     }
                                 })
                         .ToList();
        }

        /// <summary>The raw file content for an id, or null when no such scenario exists.</summary>
        public string? ReadRaw(string id)
        {
            if (!TryGetPath(id, out var path) || !ExistsExactCase(path))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                // Deleted between the existence check and the read — absent, not an error.
                return null;
            }
        }

        /// <summary>
        ///     Git blob hash (<c>sha1("blob {len}\0" + bytes)</c>) of the scenario file as on disk — pins
        ///     verification reports to an exact file version without shelling out to git.
        /// </summary>
        public string? FileHash(string id)
        {
            if (!TryGetPath(id, out var path) || !ExistsExactCase(path))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                var header = System.Text.Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
                var buffer = new byte[header.Length + bytes.Length];
                header.CopyTo(buffer, 0);
                bytes.CopyTo(buffer, header.Length);
                return Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(buffer));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Load and parse a scenario by id. Throws <see cref="FileNotFoundException" /> /
        ///     <see cref="ScenarioFormatException" />.
        /// </summary>
        public ScenarioFile LoadFile(string id)
        {
            if (!TryGetPath(id, out var path))
            {
                throw new ScenarioFormatException(new[] { $"'{id}' is not a valid scenario id" });
            }

            if (!ExistsExactCase(path))
            {
                throw new FileNotFoundException($"No scenario '{id}' under {Directory}.", path);
            }

            return ScenarioFile.Load(path);
        }

        /// <summary>
        ///     Save a scenario (the Explorer's save-as-scenario, <c>PUT /api/scenarios/{id}</c>): the body is
        ///     parsed and structurally validated, the embedded id must match, and the write is confined to the
        ///     scenarios directory. Throws <see cref="InvalidOperationException" /> when saving is disabled via
        ///     <see cref="ReadOnlyEnvVar" />, <see cref="ScenarioFormatException" /> for invalid content.
        ///     Returns the absolute path written.
        /// </summary>
        public string Save(string id, string rawJson)
        {
            if (IsReadOnly)
            {
                throw new InvalidOperationException($"Scenario saving is disabled ({ReadOnlyEnvVar}=1).");
            }

            var file = ScenarioFile.Parse(rawJson);
            if (!string.Equals(file.Id, id, StringComparison.Ordinal))
            {
                throw new ScenarioFormatException(new[] { $"id '{file.Id}' does not match the requested id '{id}'" });
            }

            if (!TryGetPath(id, out var path))
            {
                throw new ScenarioFormatException(new[] { $"'{id}' is not a valid scenario id" });
            }

            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(path, rawJson);
            return path;
        }

        // Windows file systems match names case-insensitively: without this check, 'Smoke' would load
        // smoke.scenario.json locally and then 404 on Linux CI. Ordinal-exact name matching everywhere.
        private static bool ExistsExactCase(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            return directory is not null && System.IO.Directory.EnumerateFiles(directory, name).Any(f => string.Equals(Path.GetFileName(f), name, StringComparison.Ordinal));
        }

        // Confinement: the id must be a slug (no separators, no dot-dot), and the combined path must stay
        // under the scenarios directory — belt and suspenders against traversal.
        private bool TryGetPath(string id, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrEmpty(id) || !IdSlug.IsMatch(id) || id.Contains(".."))
            {
                return false;
            }

            var candidate = Path.GetFullPath(Path.Combine(Directory, id + FileSuffix));
            if (!candidate.StartsWith(Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = candidate;
            return true;
        }
    }

    /// <summary>One discovered scenario in <c>GET /api/scenarios</c> — metadata for pickers, plus the parse error when broken.</summary>
    public sealed class ScenarioListEntry
    {
        public string Id { get; set; } = string.Empty;

        public string? Title { get; set; }

        public string? Topology { get; set; }

        public IReadOnlyList<string> Specs { get; set; } = Array.Empty<string>();

        public string FileName { get; set; } = string.Empty;

        /// <summary>Set when the file fails structural parsing — broken scenarios stay visible.</summary>
        public string? Error { get; set; }
    }
}