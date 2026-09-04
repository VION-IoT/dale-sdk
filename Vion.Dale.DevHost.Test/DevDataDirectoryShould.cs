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
        [TestProperty("spec", "AC-CTRL-007.5")]
        public void UseConfiguredDirectoryVerbatim()
        {
            // Arrange
            // Act / Assert
            var resolved = DevDataDirectory.Resolve("scenarios", @"some\explicit\dir", Path.GetTempPath());
            Assert.AreEqual(Path.GetFullPath(@"some\explicit\dir"), resolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-007.6")]
        public void PreferDirectoryInStartingDirectory()
        {
            // Arrange
            var root = NewTempTree();
            Directory.CreateDirectory(Path.Combine(root, "scenarios"));
            // Act / Assert
            Assert.AreEqual(Path.Combine(root, "scenarios"), DevDataDirectory.Resolve("scenarios", null, root));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-007.6")]
        public void SearchAncestorsUpToRepositoryRoot()
        {
            // The Visual Studio Ctrl+F5 shape: cwd = <repo>/Project.DevHost/bin/Debug/net10.0, the
            // scenarios live at the repo root next to .git.
            // Arrange
            var repo = NewTempTree();
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "scenarios"));
            var binDir = Path.Combine(repo, "Project.DevHost", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(binDir);

            // Act / Assert
            Assert.AreEqual(Path.Combine(repo, "scenarios"), DevDataDirectory.Resolve("scenarios", null, binDir));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-007.6")]
        public void SearchPastNestedSolutionFiles()
        {
            // Mono-repo shape: a per-project .sln sits BELOW the data directory (the SDK's own examples
            // do this) — only .git bounds the walk.
            // Arrange
            var repo = NewTempTree();
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "scenarios"));
            var projectDir = Path.Combine(repo, "examples", "Demo");
            var binDir = Path.Combine(projectDir, "Demo.DevHost", "bin", "Debug", "net10.0");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(projectDir, "Demo.sln"), "");

            // Act / Assert
            Assert.AreEqual(Path.Combine(repo, "scenarios"), DevDataDirectory.Resolve("scenarios", null, binDir));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-007.6")]
        [TestProperty("spec", "AC-CTRL-007.7")]
        public void NameDirectoryInStartingDirectoryWhenSearchFindsNone()
        {
            // scenarios/ ABOVE the repo root must not be picked up — the walk stops at .git.
            // Arrange
            var outside = NewTempTree();
            Directory.CreateDirectory(Path.Combine(outside, "scenarios"));
            var repo = Path.Combine(outside, "repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            var binDir = Path.Combine(repo, "bin");
            Directory.CreateDirectory(binDir);

            // Act / Assert
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