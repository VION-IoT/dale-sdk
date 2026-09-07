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
        [DataRow(null, DisplayName = "nothing stored")]
        [DataRow("test", DisplayName = "logged into test")]
        [DataRow("production", DisplayName = "logged into production")]
        public async Task DescribeEnvironmentDefaultWhateverStoreHolds(string? storedEnvironment)
        {
            // Arrange
            if (storedEnvironment != null)
            {
                TokenStore.SaveConfig(new DaleConfig { Environment = storedEnvironment });
            }

            // Act
            await Program.BuildRootCommand().Parse(new[] { "login", "--help" }).InvokeAsync();

            // Assert
            var rendered = _standardOutput.ToString().Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("-e, --environment", StringComparison.Ordinal));
            Assert.AreEqual(ExpectedHelpLine, rendered);
        }
    }
}