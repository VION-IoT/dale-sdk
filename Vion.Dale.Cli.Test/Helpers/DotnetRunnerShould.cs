using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class DotnetRunnerShould
    {
        [TestCleanup]
        public void Cleanup()
        {
            DaleConsole.JsonMode = false;
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.9")]
        public void CaptureChildOutputInJsonModeSoOnlyDocumentReachesStandardOutput()
        {
            // Arrange
            DaleConsole.JsonMode = true;

            // Act
            var startInfo = DotnetRunner.ComposeStartInfo("publish", new[] { "MyLib.csproj" }, null);

            // Assert
            Assert.IsTrue(startInfo.RedirectStandardOutput);
            Assert.IsFalse(startInfo.RedirectStandardError);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.9")]
        public void InheritChildOutputInTableMode()
        {
            // Arrange
            DaleConsole.JsonMode = false;

            // Act
            var startInfo = DotnetRunner.ComposeStartInfo("build", new[] { "MyLib.csproj" }, null);

            // Assert
            Assert.IsFalse(startInfo.RedirectStandardOutput);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.2")]
        public void StartDotnetWithoutShellAndInGivenDirectory()
        {
            // Arrange / Act
            var startInfo = DotnetRunner.ComposeStartInfo("test", null, @"C:\work\MyLib");

            // Assert
            Assert.AreEqual("dotnet", startInfo.FileName);
            Assert.IsFalse(startInfo.UseShellExecute);
            Assert.AreEqual(@"C:\work\MyLib", startInfo.WorkingDirectory);
            CollectionAssert.AreEqual(new[] { "test" }, startInfo.ArgumentList.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public void PutVerbFirstInArgumentList()
        {
            // Arrange / Act
            var args = DotnetRunner.ComposeArguments("build", new[] { "MyLib.csproj" });

            // Assert
            CollectionAssert.AreEqual(new[] { "build", "MyLib.csproj" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public void ComposeVerbAloneWhenNoArgumentsGiven()
        {
            // Arrange / Act
            var args = DotnetRunner.ComposeArguments("restore", null);

            // Assert
            CollectionAssert.AreEqual(new[] { "restore" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public void KeepEachArgumentSeparateSoSpacesAndExpressionsSurvive()
        {
            // Arrange
            var forwarded = new[] { @"C:\a path\MyLib.csproj", "--filter", "kind!=headless-integration", "-c", "Release" };

            // Act
            var args = DotnetRunner.ComposeArguments("test", forwarded);

            // Assert
            CollectionAssert.AreEqual(new[] { "test", @"C:\a path\MyLib.csproj", "--filter", "kind!=headless-integration", "-c", "Release" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public void KeepArgumentOrderAsGiven()
        {
            // Arrange / Act
            var args = DotnetRunner.ComposeArguments("pack", new[] { "MyLib.csproj", "-c", "Release", "-p:IsPackable=true", "-p:Version=1.2.3" });

            // Assert
            CollectionAssert.AreEqual(new[] { "pack", "MyLib.csproj", "-c", "Release", "-p:IsPackable=true", "-p:Version=1.2.3" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.9")]
        public async Task RelayCapturedChildOutputToStandardError()
        {
            // Arrange
            var originalError = Console.Error;
            var standardError = new StringWriter();
            Console.SetError(standardError);
            using var childOutput = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Determining projects to restore...\nRestored MyLib.csproj\n")));

            // Act
            try
            {
                await DotnetRunner.RelayToStandardErrorAsync(childOutput);
            }
            finally
            {
                Console.SetError(originalError);
            }

            // Assert
            StringAssert.Contains(standardError.ToString(), "Determining projects to restore...");
            StringAssert.Contains(standardError.ToString(), "Restored MyLib.csproj");
        }
    }
}