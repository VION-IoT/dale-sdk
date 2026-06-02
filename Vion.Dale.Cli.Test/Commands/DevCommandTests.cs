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
    }
}
