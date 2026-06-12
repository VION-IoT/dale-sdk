using System;
using System.IO;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     Default resolution of the dev-tool data directories: cwd wins when present, IDE launches
    ///     (cwd = bin/Debug/netX.Y) walk up to the repository root, and the walk never escapes it.
    /// </summary>
    [TestClass]
    public class DevDataDirectoryShould
    {
        [TestMethod]
        public void UseTheExplicitPathVerbatim()
        {
            var resolved = DevDataDirectory.Resolve("scenarios", @"some\explicit\dir", Path.GetTempPath());
            Assert.AreEqual(Path.GetFullPath(@"some\explicit\dir"), resolved);
        }

        [TestMethod]
        public void PreferTheStartDirectorysOwnFolder()
        {
            var root = NewTempTree();
            Directory.CreateDirectory(Path.Combine(root, "scenarios"));
            Assert.AreEqual(Path.Combine(root, "scenarios"), DevDataDirectory.Resolve("scenarios", null, root));
        }

        [TestMethod]
        public void WalkUpToTheRepositoryRootForIdeLaunches()
        {
            // The Visual Studio Ctrl+F5 shape: cwd = <repo>/Project.DevHost/bin/Debug/net10.0, the
            // scenarios live at the repo root next to .git.
            var repo = NewTempTree();
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "scenarios"));
            var binDir = Path.Combine(repo, "Project.DevHost", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(binDir);

            Assert.AreEqual(Path.Combine(repo, "scenarios"), DevDataDirectory.Resolve("scenarios", null, binDir));
        }

        [TestMethod]
        public void WalkPastNestedSolutionFiles()
        {
            // Mono-repo shape: a per-project .sln sits BELOW the data directory (the SDK's own examples
            // do this) — only .git bounds the walk.
            var repo = NewTempTree();
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "scenarios"));
            var projectDir = Path.Combine(repo, "examples", "Demo");
            var binDir = Path.Combine(projectDir, "Demo.DevHost", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(projectDir, "Demo.sln"), "");

            Assert.AreEqual(Path.Combine(repo, "scenarios"), DevDataDirectory.Resolve("scenarios", null, binDir));
        }

        [TestMethod]
        public void NeverEscapeTheRepositoryRoot()
        {
            // scenarios/ ABOVE the repo root must not be picked up — the walk stops at .git.
            var outside = NewTempTree();
            Directory.CreateDirectory(Path.Combine(outside, "scenarios"));
            var repo = Path.Combine(outside, "repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            var binDir = Path.Combine(repo, "bin");
            Directory.CreateDirectory(binDir);

            Assert.AreEqual(Path.Combine(binDir, "scenarios"), DevDataDirectory.Resolve("scenarios", null, binDir));
        }

        private static string NewTempTree()
        {
            var dir = Path.Combine(Path.GetTempPath(), "dale-datadir-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}