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
        public void BuildRunArguments_NoForwardedTokens_OmitsDelimiter()
        {
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new string[0]);

            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj" }, args);
        }

        [TestMethod]
        public void BuildRunArguments_ForwardsScenarioAfterDelimiter()
        {
            // `dale dev -- operator-steering` must reach the DevHost app's args[0] as the scenario name.
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "operator-steering" });

            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering" }, args);
        }

        [TestMethod]
        public void BuildRunArguments_DelimiterShieldsOptionLikeTokens()
        {
            // The `--` ensures even option-shaped tokens are forwarded to the app verbatim rather than being
            // interpreted by dotnet run (which would otherwise swallow or reject them).
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "--scenario", "release" });

            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "--scenario", "release" }, args);
        }

        [TestMethod]
        public void BuildRunArguments_Preset_BecomesTheFirstProgramArgument()
        {
            // `dale dev --preset operator-steering` — the discoverable form — reaches the app as args[0].
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new string[0], "operator-steering");

            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering" }, args);
        }

        [TestMethod]
        public void BuildRunArguments_PresetPrecedesForwardedTokens()
        {
            // The preset is args[0] (the consumer's switch); tokens after `dale dev --` follow it.
            var args = DevCommand.BuildRunArguments("My.DevHost.csproj", new[] { "--verbose" }, "operator-steering");

            CollectionAssert.AreEqual(new[] { "--project", "My.DevHost.csproj", "--", "operator-steering", "--verbose" }, args);
        }

        [TestMethod]
        public async Task BootWindow_ProcessExitsNormally_ReturnsItsExitCode()
        {
            // The cooperating host boots, writes the export, and exits — we just relay its exit code.
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(0), () => true, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, exit);
        }

        [TestMethod]
        public async Task BootWindow_ProcessExitsZeroButWroteNothing_FailsWithAHint()
        {
            // DF-16: a freshly-restored `dotnet run` can boot serve-mode and exit 0 without honoring the
            // export — don't report that as a silent success.
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(0), () => false, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        public async Task BootWindow_FileWrittenButProcessLingers_StopsItAndSucceeds()
        {
            // Defensive: the host wrote the export but didn't exit. Once the file exists we don't hang on
            // the boot window — we stop the stray process and report success.
            var exit = await DevCommand.RunWithBootWindowAsync(InfiniteAsync, () => true, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

            Assert.AreEqual(0, exit);
        }

        [TestMethod]
        public async Task BootWindow_NoFileAppears_KillsTheHangAndFails()
        {
            // The Program.cs ignored the export env vars and ran forever — bounded, killed, non-zero (DF-01).
            var exit = await DevCommand.RunWithBootWindowAsync(InfiniteAsync, () => false, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5));

            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [DataRow(1, DisplayName = "--export-config only")]
        [DataRow(2, DisplayName = "--export-config and --export-topology")]
        public async Task Export_StaleTargetFromAnEarlierRun_DoesNotCountAsAFreshExport(int targetCount)
        {
            // VION-70: completion is "the target appeared". A leftover from an earlier run satisfied the very
            // first poll, so a host that wrote nothing was reported as a successful export. Both flags.
            var (root, targets) = CreateStaleTargets(targetCount);
            try
            {
                var exit = await DevCommand.RunExportAsync(targets, _ => Task.FromResult(0), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

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
        public async Task Export_StaleTargetOverwrittenByTheRun_Succeeds()
        {
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

                Assert.AreEqual(0, exit);
                Assert.AreEqual("fresh", File.ReadAllText(targets[0]));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public async Task Export_TargetThatCannotBeCleared_FailsBeforeBootingTheHost()
        {
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

                Assert.AreEqual(1, exit);
                Assert.IsFalse(booted, "The host must not be started when the target could not be cleared.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestMethod]
        public async Task Export_TargetDirectoryMissing_NamesThatAsTheCauseRatherThanAFailedRemoval()
        {
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