using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class DevCommandTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.8")]
        public async Task SucceedWhenExportWasWrittenAndHostExitedNonZero()
        {
            // Arrange
            var target = Path.Combine(Path.GetTempPath(), $"dale-export-{Guid.NewGuid():N}.json");

            // Act
            var exit = await DevCommand.RunWithBootWindowAsync(_ =>
                                                               {
                                                                   File.WriteAllText(target, "{}");
                                                                   return Task.FromResult(134);
                                                               },
                                                               () => File.Exists(target),
                                                               TimeSpan.FromSeconds(5),
                                                               TimeSpan.FromMilliseconds(50));

            // Assert
            Assert.AreEqual(0, exit);
            File.Delete(target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.8")]
        public async Task FailWhenHostExitedNonZeroHavingWrittenNothing()
        {
            // Arrange / Act
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(134), () => false, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50));

            // Assert
            Assert.AreEqual(134, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.9")]
        [DataRow(false, false, "  Web UI at http://localhost:5000")]
        [DataRow(true, false, "  Control API at http://localhost:5000/api (no browser)")]
        [DataRow(false, true, "  Writing the export and exiting — no server is started")]
        [DataRow(true, true, "  Writing the export and exiting — no server is started")]
        public void AnnounceWhatItWillActuallyDo(bool headless, bool exporting, string expectedSecondLine)
        {
            // Arrange / Act
            var lines = DevCommand.DescribeStartup("MyLib.DevHost", headless, exporting);

            // Assert
            Assert.AreEqual(expectedSecondLine, lines[1]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.3")]
        public void BuildRunArguments_NoForwardedTokens_OmitsDelimiter()
        {
            // Arrange / Act
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new string[0]);

            // Assert
            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.3")]
        public void BuildRunArguments_ForwardsScenarioAfterDelimiter()
        {
            // Arrange / Act
            // `dale dev -- operator-steering` must reach the DevHost app's args[0] as the scenario name.
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "operator-steering" });

            // Assert
            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.3")]
        public void BuildRunArguments_DelimiterShieldsOptionLikeTokens()
        {
            // Arrange / Act
            // The `--` ensures even option-shaped tokens are forwarded to the app verbatim rather than being
            // interpreted by dotnet run (which would otherwise swallow or reject them).
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "--scenario", "release" });

            // Assert
            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "--scenario", "release" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.3")]
        public void BuildRunArguments_Preset_BecomesFirstProgramArgument()
        {
            // Arrange / Act
            // `dale dev --preset operator-steering` — the discoverable form — reaches the app as args[0].
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new string[0], "operator-steering");

            // Assert
            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.3")]
        public void BuildRunArguments_PresetPrecedesForwardedTokens()
        {
            // Arrange / Act
            // The preset is args[0] (the consumer's switch); tokens after `dale dev --` follow it.
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "--verbose" }, "operator-steering");

            // Assert
            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering", "--verbose" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.5")]
        public async Task BootWindow_ProcessExitsNormally_ReturnsItsExitCode()
        {
            // Arrange / Act
            // The cooperating host boots, writes the export, and exits — we just relay its exit code.
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(0), () => true, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(0, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.5")]
        public async Task BootWindow_ProcessExitsZeroButWroteNothing_FailsWithHint()
        {
            // Arrange / Act
            // DF-16: a freshly-restored `dotnet run` can boot serve-mode and exit 0 without honoring the
            // export — don't report that as a silent success.
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(0), () => false, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.5")]
        public async Task BootWindow_FileWrittenButProcessLingers_StopsItAndSucceeds()
        {
            // Arrange / Act
            // Defensive: the host wrote the export but didn't exit. Once the file exists we don't hang on
            // the boot window — we stop the stray process and report success.
            var exit = await DevCommand.RunWithBootWindowAsync(InfiniteAsync, () => true, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.AreEqual(0, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.5")]
        public async Task BootWindow_NoFileAppears_KillsHangAndFails()
        {
            // Arrange / Act
            // The Program.cs ignored the export env vars and ran forever — bounded, killed, non-zero (DF-01).
            var exit = await DevCommand.RunWithBootWindowAsync(InfiniteAsync, () => false, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.4")]
        [DataRow(1, DisplayName = "--export-config only")]
        [DataRow(2, DisplayName = "--export-config and --export-topology")]
        public async Task Export_StaleTargetFromEarlierRun_DoesNotCountAsFreshExport(int targetCount)
        {
            // Arrange / Act
            // VION-70: completion is "the target appeared". A leftover from an earlier run satisfied the very
            // first poll, so a host that wrote nothing was reported as a successful export. Both flags.
            var (root, targets) = CreateStaleTargets(targetCount);
            try
            {
                var exit = await DevCommand.RunExportAsync(targets, _ => Task.FromResult(0), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                // Assert
                Assert.AreEqual(1, exit, "A host that wrote nothing must fail even when a stale target is lying at the path.");
                foreach (var target in targets)
                {
                    Assert.IsFalse(File.Exists(target), "The stale target must not survive the run — it would be read as this run's export.");
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.4")]
        public async Task Export_StaleTargetOverwrittenByRun_Succeeds()
        {
            // Arrange / Act
            // The other half of the pair: clearing the target first must not break the normal path, where the
            // host really does write and the fresh content is what the caller gets.
            var (root, targets) = CreateStaleTargets(1);
            try
            {
                var exit = await DevCommand.RunExportAsync(targets,
                                                           _ =>
                                                           {
                                                               File.WriteAllText(targets[0], "fresh");
                                                               return Task.FromResult(0);
                                                           },
                                                           TimeSpan.FromSeconds(5),
                                                           TimeSpan.FromSeconds(5));

                // Assert
                Assert.AreEqual(0, exit);
                Assert.AreEqual("fresh", File.ReadAllText(targets[0]));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.4")]
        public async Task Export_TargetCannotBeCleared_FailsBeforeBootingHost()
        {
            // Arrange / Act
            // With something un-removable at the target path, "the file exists" can no longer mean "this run
            // wrote it" — so refuse rather than boot a host whose success we could not tell from a stale file.
            // A directory at the path is the portable way to make removal fail: File.Delete throws on it on
            // every OS, where a read-only file is only undeletable on Windows (Unix unlink obeys the parent
            // directory's permissions, and CI containers usually run as root).
            var root = Path.Combine(Path.GetTempPath(), $"vion70-{Guid.NewGuid():N}");
            var target = Path.Combine(root, "export-0.json");
            Directory.CreateDirectory(target);
            var booted = false;
            try
            {
                var exit = await DevCommand.RunExportAsync(new[] { target },
                                                           _ =>
                                                           {
                                                               booted = true;
                                                               return Task.FromResult(0);
                                                           },
                                                           TimeSpan.FromSeconds(5),
                                                           TimeSpan.FromSeconds(5));

                // Assert
                Assert.AreEqual(1, exit);
                Assert.IsFalse(booted, "The host must not be started when the target could not be cleared.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-009.4")]
        public async Task Export_TargetDirectoryMissing_NamesMissingDirectoryRatherThanFailedRemoval()
        {
            // Arrange / Act
            // File.Delete throws DirectoryNotFoundException when the parent is absent, which would otherwise
            // report a first-ever export into a new folder as "could not remove the previous export".
            var root = Path.Combine(Path.GetTempPath(), $"vion70-{Guid.NewGuid():N}");
            var target = Path.Combine(root, "nested", "export.json");
            var booted = false;

            var exit = await DevCommand.RunExportAsync(new[] { target },
                                                       _ =>
                                                       {
                                                           booted = true;
                                                           return Task.FromResult(0);
                                                       },
                                                       TimeSpan.FromSeconds(5),
                                                       TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(1, exit);
            Assert.IsFalse(booted, "A host that cannot write the export must not be started.");
            Assert.IsFalse(Directory.Exists(root), "The export must not create directories it was not asked to create.");
        }

        // A scratch directory holding <paramref name="count" /> export targets that already exist — the
        // leftover-from-an-earlier-run state VION-70 is about.
        private static (string Root, string[] Targets) CreateStaleTargets(int count)
        {
            var root = Path.Combine(Path.GetTempPath(), $"vion70-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var targets = new string[count];
            for (var i = 0; i < count; i++)
            {
                targets[i] = Path.Combine(root, $"export-{i}.json");
                File.WriteAllText(targets[i], "{ \"stale\": true }");
            }

            return (root, targets);
        }

        // A run that never finishes on its own but honors cancellation — the shape of a real `dotnet run`
        // wrapped by DotnetRunner (which kills the process tree and throws on the token).
        private static async Task<int> InfiniteAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}