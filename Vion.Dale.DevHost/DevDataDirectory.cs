using System;
using System.IO;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Resolves the dev-tool data directories (<c>scenarios/</c>, <c>topologies/</c>) when no explicit
    ///     path is configured. <c>{cwd}/&lt;name&gt;</c> wins when it exists — the <c>dale dev</c> posture,
    ///     run from the repository root. IDE launches (Visual Studio Ctrl+F5, Rider) set the working
    ///     directory to <c>bin/Debug/netX.Y</c>, where that convention silently finds nothing — so when the
    ///     cwd has no such directory, the nearest ancestor's is used, bounded by the repository root
    ///     (<c>.git</c>; max 8 levels — deliberately NOT <c>*.sln</c>, nested per-project solutions below
    ///     the data directory are a real mono-repo layout). Fallback: <c>{cwd}/&lt;name&gt;</c> (possibly
    ///     non-existent — discovery then lists nothing and the UI shows where to create files).
    /// </summary>
    public static class DevDataDirectory
    {
        /// <param name="name">The directory name to resolve (e.g. <c>scenarios</c>).</param>
        /// <param name="explicitPath">A configured override (<c>WithScenarios</c> / <c>WithTopologies</c>) — used verbatim.</param>
        /// <param name="startDirectory">Where resolution starts; defaults to the current working directory.</param>
        public static string Resolve(string name, string? explicitPath, string? startDirectory = null)
        {
            if (explicitPath != null)
            {
                return Path.GetFullPath(explicitPath);
            }

            var start = Path.GetFullPath(startDirectory ?? Environment.CurrentDirectory);
            var current = start;
            for (var depth = 0; depth < 8 && current is not null; depth++)
            {
                var candidate = Path.Combine(current, name);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                if (Directory.Exists(Path.Combine(current, ".git")))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }

            return Path.Combine(start, name);
        }
    }
}