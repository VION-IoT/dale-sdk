using System;
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
            var exit = await DevCommand.RunWithBootWindowAsync(_ => Task.FromResult(0), () => false, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            Assert.AreEqual(0, exit);
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

        // A run that never finishes on its own but honors cancellation — the shape of a real `dotnet run`
        // wrapped by DotnetRunner (which kills the process tree and throws on the token).
        private static async Task<int> InfiniteAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}