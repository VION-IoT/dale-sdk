using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Commands.Auth
{
    /// <summary>
    ///     The login command's own option surface. The environment option resolves from stored state,
    ///     which is a rule its help describes rather than resolves — a help line that reads the store
    ///     differs between the developer's machine and the runner that regenerates the snapshot.
    /// </summary>
    [TestClass]
    public class LoginCommandShould
    {
        private const string ExpectedHelpLine = "-e, --environment <environment>  Target environment (production, test); defaults to the stored one, else production";

        private TextWriter _originalOut = null!;

        private StringWriter _standardOutput = null!;

        private TemporaryStoreRoot _store = null!;

        [TestInitialize]
        public void Setup()
        {
            _store = new TemporaryStoreRoot();
            _originalOut = Console.Out;
            _standardOutput = new StringWriter();
            Console.SetOut(_standardOutput);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(_originalOut);
            _store.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-019.3")]

        // Never "production": DaleConfig.Environment defaults to it, so that row reads the same on an
        // empty store and cannot tell a loaded fixture from no fixture at all. The empty store is the
        // test below.
        [DataRow("test")]
        [DataRow("staging")]
        public async Task DescribeEnvironmentDefaultWhateverStoreHolds(string storedEnvironment)
        {
            // Arrange
            TokenStore.SaveConfig(new DaleConfig { Environment = storedEnvironment });

            // Act
            await Program.BuildRootCommand().Parse(new[] { "login", "--help" }).InvokeAsync();

            // Assert
            Assert.AreEqual(storedEnvironment,
                            TokenStore.LoadConfig().Environment,
                            "The store has to hold the row's value, or the row proves nothing about a help text that ignores it.");
            Assert.AreEqual(ExpectedHelpLine, EnvironmentHelpLine());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-019.3")]
        public async Task DescribeEnvironmentDefaultWithEmptyStore()
        {
            // Arrange / Act
            await Program.BuildRootCommand().Parse(new[] { "login", "--help" }).InvokeAsync();

            // Assert
            Assert.AreEqual(ExpectedHelpLine, EnvironmentHelpLine());
        }

        private string EnvironmentHelpLine()
        {
            return _standardOutput.ToString().Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("-e, --environment", StringComparison.Ordinal));
        }
    }
}