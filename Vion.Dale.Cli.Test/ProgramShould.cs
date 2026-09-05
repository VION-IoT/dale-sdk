using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli;

namespace Vion.Dale.Cli.Test
{
    [TestClass]
    public class ProgramShould
    {
        private static readonly HashSet<string> TopLevel = Program.TopLevelCommandNames(Program.BuildRootCommand());

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.1")]
        [DataRow("--version")]
        [DataRow("-v")]
        [DataRow("--version", "--output", "json")]
        [DataRow("--output", "json", "--version")]
        [DataRow("--verbose", "-v")]
        public void AnswerVersionWhereverItAppearsBeforeCommand(params string[] args)
        {
            // Arrange / Act
            var wantsVersion = Program.WantsVersion(args, TopLevel);

            // Assert
            Assert.IsTrue(wantsVersion);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.1")]
        [DataRow("pack", "--version", "1.2.3")]
        [DataRow("upload", "--version", "1.2.3")]
        [DataRow("build", "--version")]
        [DataRow("test", "-v")]
        [DataRow("list")]
        [DataRow]
        public void LeaveVersionToCommandThatClaimedLine(params string[] args)
        {
            // Arrange / Act
            var wantsVersion = Program.WantsVersion(args, TopLevel);

            // Assert
            Assert.IsFalse(wantsVersion);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.2")]
        public void ReportVersionWithoutSourceLinkSuffix()
        {
            // Arrange / Act
            var version = Program.Version();

            // Assert
            Assert.IsFalse(version.Contains('+'), $"expected no source-link suffix, got '{version}'");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.3")]
        public async Task RefuseOutputFormatOutsideTableAndJson()
        {
            // Arrange / Act
            var exitCode = await Program.Main(new[] { "list", "--output", "xml" });

            // Assert
            Assert.AreEqual(1, exitCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.3")]
        public void ReportRefusedOutputFormatAsParseError()
        {
            // Arrange
            var rootCommand = Program.BuildRootCommand();

            // Act
            var parseResult = rootCommand.Parse(new[] { "list", "--output", "xml" });

            // Assert
            Assert.AreNotEqual(0, parseResult.Errors.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.1")]
        public void OfferThirteenTopLevelCommands()
        {
            // Arrange / Act
            var names = Program.TopLevelCommandNames(Program.BuildRootCommand());

            // Assert
            CollectionAssert.AreEquivalent(new[]
                                           {
                                               "new", "build", "test", "dev", "list", "scenario", "add", "pack", "upload", "login", "logout", "whoami", "config",
                                           },
                                           names.ToArray());
        }
    }
}