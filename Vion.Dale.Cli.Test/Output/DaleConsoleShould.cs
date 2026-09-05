using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Test.Output
{
    [TestClass]
    public class DaleConsoleShould
    {
        private TextWriter _originalError = null!;

        private TextWriter _originalOut = null!;

        private StringWriter _standardError = null!;

        private StringWriter _standardOutput = null!;

        [TestInitialize]
        public void Setup()
        {
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _standardOutput = new StringWriter();
            _standardError = new StringWriter();
            Console.SetOut(_standardOutput);
            Console.SetError(_standardError);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            DaleConsole.JsonMode = false;
            DaleConsole.VerboseMode = false;
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.4")]
        public void WriteFailureToStandardErrorInTableMode()
        {
            // Arrange
            DaleConsole.JsonMode = false;

            // Act
            DaleConsole.Error("no Dale project found");

            // Assert
            StringAssert.Contains(_standardError.ToString(), "no Dale project found");
            Assert.AreEqual(string.Empty, _standardOutput.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.6")]
        public void WriteFailureToStandardOutputAsJsonInJsonMode()
        {
            // Arrange
            DaleConsole.JsonMode = true;

            // Act
            DaleConsole.Error("no Dale project found");

            // Assert
            StringAssert.Contains(_standardOutput.ToString(), "\"error\": \"no Dale project found\"");
            Assert.AreEqual(string.Empty, _standardError.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.5")]
        public void SuppressEveryHumanLineInJsonMode()
        {
            // Arrange
            DaleConsole.JsonMode = true;
            DaleConsole.VerboseMode = true;

            // Act
            DaleConsole.Success("Packed", "MyLib v1.0.0");
            DaleConsole.Info("some detail");
            DaleConsole.Warning("a caution");
            DaleConsole.Header("A heading");
            DaleConsole.KeyValue("Key:", "value");
            DaleConsole.Verbose("a trace line");
            DaleConsole.Blank();

            // Assert
            Assert.AreEqual(string.Empty, _standardOutput.ToString());
            Assert.AreEqual(string.Empty, _standardError.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.5")]
        public void WriteJsonResultToStandardOutput()
        {
            // Arrange
            DaleConsole.JsonMode = true;

            // Act
            DaleConsole.WriteJsonResult(new { packageId = "MyLib", version = "1.0.0" });

            // Assert
            StringAssert.Contains(_standardOutput.ToString(), "\"packageId\": \"MyLib\"");
            StringAssert.Contains(_standardOutput.ToString(), "\"version\": \"1.0.0\"");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.7")]
        public void SuppressVerboseLineUnlessAsked()
        {
            // Arrange
            DaleConsole.JsonMode = false;
            DaleConsole.VerboseMode = false;

            // Act
            DaleConsole.Verbose("a trace line");

            // Assert
            Assert.AreEqual(string.Empty, _standardOutput.ToString());
            Assert.AreEqual(string.Empty, _standardError.ToString());
        }
    }
}