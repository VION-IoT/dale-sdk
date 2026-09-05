using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class DotnetRunnerShould
    {
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
    }
}
