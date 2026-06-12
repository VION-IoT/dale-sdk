using System.CommandLine;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     `dale test` / `dale build` / `dale dev` forward tokens they don't recognize to the underlying
    ///     `dotnet` command. That only works when the parser collects unmatched tokens instead of treating
    ///     them as errors — these tests parse the real root command to pin that behavior.
    /// </summary>
    [TestClass]
    public class UnmatchedTokenForwardingTests
    {
        [TestMethod]
        public void TestCommand_UnknownOption_ParsesCleanlyAndForwards()
        {
            var result = Program.BuildRootCommand().Parse(new[] { "test", "--filter", "kind!=headless-integration" });

            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
            CollectionAssert.AreEqual(new[] { "--filter", "kind!=headless-integration" }, result.UnmatchedTokens.ToArray());
        }

        [TestMethod]
        public void TestCommand_DoubleDashSeparator_ForwardsTokensWithoutTheSeparator()
        {
            var result = Program.BuildRootCommand().Parse(new[] { "test", "--", "--filter", "kind!=headless-integration" });

            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
            CollectionAssert.AreEqual(new[] { "--filter", "kind!=headless-integration" }, result.UnmatchedTokens.ToArray());
        }

        [TestMethod]
        public void TestCommand_KnownOptionsAreBound_NotForwarded()
        {
            var result = Program.BuildRootCommand().Parse(new[] { "test", "--project", "My.csproj", "--filter", "x" });

            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
            Assert.AreEqual("My.csproj", result.GetValue<string?>("--project"));
            CollectionAssert.AreEqual(new[] { "--filter", "x" }, result.UnmatchedTokens.ToArray());
        }

        [TestMethod]
        public void BuildCommand_UnknownShortOption_ParsesCleanlyAndForwards()
        {
            var result = Program.BuildRootCommand().Parse(new[] { "build", "-c", "Release" });

            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
            CollectionAssert.AreEqual(new[] { "-c", "Release" }, result.UnmatchedTokens.ToArray());
        }

        [TestMethod]
        public void DevCommand_ScenarioAfterDoubleDash_ParsesCleanly()
        {
            // `dale dev -- operator-steering` — the documented way to pass a scenario to the DevHost app.
            var result = Program.BuildRootCommand().Parse(new[] { "dev", "--", "operator-steering" });

            Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
            CollectionAssert.AreEqual(new[] { "operator-steering" }, result.UnmatchedTokens.ToArray());
        }

        [TestMethod]
        public void RootCommand_UnknownSubcommand_IsStillAParseError()
        {
            // Forwarding is scoped to the dotnet-wrapping commands; a typo'd subcommand must keep failing fast.
            var result = Program.BuildRootCommand().Parse(new[] { "bogus" });

            Assert.AreNotEqual(0, result.Errors.Count);
        }
    }
}