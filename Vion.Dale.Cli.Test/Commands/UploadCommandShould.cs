using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class UploadCommandShould
    {
        private string _root = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "dale-cli-upload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "bin", "Release"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // Best effort — a temporary directory left behind fails nothing.
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.5")]
        [DataRow("Acme.Energy", "Acme.Energy.1.4.0.nupkg", true)]
        [DataRow("Acme.Energy", "Acme.Energy.1.4.0-preview.2.nupkg", true)]
        [DataRow("Acme.Energy", "Acme.Energy.Modbus.1.4.0.nupkg", false)]
        [DataRow("Acme.Energy", "Acme.EnergyPlus.1.4.0.nupkg", false)]
        [DataRow("Acme.Energy", "Other.Library.1.0.0.nupkg", false)]
        [DataRow("Acme.Energy", "Acme.Energy.nupkg", false)]
        public void RecogniseOnlyItsOwnPackage(string packageId, string fileName, bool expectedMatch)
        {
            // Arrange / Act
            var matches = UploadCommand.IsPackageOf(packageId, Path.Combine(_root, fileName));

            // Assert
            Assert.AreEqual(expectedMatch, matches);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.5")]
        public void FindPackageOfProjectBesideLongerNamedSibling()
        {
            // Arrange
            var releaseDir = Path.Combine(_root, "bin", "Release");
            File.WriteAllText(Path.Combine(releaseDir, "Acme.Energy.Modbus.1.4.0.nupkg"), "sibling");
            var expected = Path.Combine(releaseDir, "Acme.Energy.1.4.0.nupkg");
            File.WriteAllText(expected, "own");
            File.SetLastWriteTimeUtc(Path.Combine(releaseDir, "Acme.Energy.Modbus.1.4.0.nupkg"), DateTime.UtcNow);
            File.SetLastWriteTimeUtc(expected, DateTime.UtcNow.AddMinutes(-5));

            // Act
            var found = UploadCommand.FindNupkg(ProjectAt("Acme.Energy"));

            // Assert
            Assert.AreEqual(expected, found);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.5")]
        public void FindNoPackageWhenOnlySiblingsArePresent()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_root, "bin", "Release", "Acme.Energy.Modbus.1.4.0.nupkg"), "sibling");

            // Act
            var found = UploadCommand.FindNupkg(ProjectAt("Acme.Energy"));

            // Assert
            Assert.IsNull(found);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.5")]
        public void PreferMostRecentOfItsOwnPackages()
        {
            // Arrange
            var releaseDir = Path.Combine(_root, "bin", "Release");
            var older = Path.Combine(releaseDir, "Acme.Energy.1.3.0.nupkg");
            var newer = Path.Combine(releaseDir, "Acme.Energy.1.4.0.nupkg");
            File.WriteAllText(older, "older");
            File.WriteAllText(newer, "newer");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);

            // Act
            var found = UploadCommand.FindNupkg(ProjectAt("Acme.Energy"));

            // Assert
            Assert.AreEqual(newer, found);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.6")]
        public void ReportCompilerErrorsWhenPackFails()
        {
            // Arrange
            var packOutput = "  Determining projects to restore...\r\n" +
                             "C:\\work\\MyLib\\Thermostat.cs(12,9): error CS0103: The name 'Foo' does not exist in the current context [C:\\work\\MyLib\\MyLib.csproj]\r\n" +
                             "  Build FAILED.\r\n";

            // Act
            var described = UploadCommand.DescribePackFailure(packOutput);

            // Assert
            StringAssert.Contains(described, "error CS0103");
            StringAssert.Contains(described, "Thermostat.cs(12,9)");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.6")]
        public void ReportPlainFailureWhenPackOutputNamesNoError()
        {
            // Arrange / Act
            var described = UploadCommand.DescribePackFailure("  Determining projects to restore...\r\n  Build FAILED.\r\n");

            // Assert
            Assert.AreEqual("Pack failed.", described);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.7")]
        public void NestEndpointAnswerAsJsonDocument()
        {
            // Arrange / Act
            var document = UploadCommand.ReadResponseDocument("{\"libraryVersionId\":\"abc\"}");

            // Assert
            Assert.AreEqual("abc", document!["libraryVersionId"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.7")]
        public void NestUnparseableEndpointAnswerAsText()
        {
            // Arrange / Act
            var document = UploadCommand.ReadResponseDocument("<html>502</html>");

            // Assert
            Assert.AreEqual("<html>502</html>", document!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.7")]
        public void NestNothingWhenEndpointAnsweredWithEmptyBody()
        {
            // Arrange / Act
            var document = UploadCommand.ReadResponseDocument("   ");

            // Assert
            Assert.IsNull(document);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.4")]
        public void ReadVersionBackFromProducedPackage()
        {
            // Arrange
            var nupkgPath = Path.Combine(_root, "Acme.Energy.1.4.0.nupkg");
            using (var archive = ZipFile.Open(nupkgPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Acme.Energy.nuspec");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("""<?xml version="1.0"?><package><metadata><id>Acme.Energy</id><version>1.4.0</version></metadata></package>""");
            }

            // Act
            var version = UploadCommand.ReadNupkgVersion(nupkgPath);

            // Assert
            Assert.AreEqual("1.4.0", version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.4")]
        public void ReadNoVersionFromArchiveCarryingNoNuspec()
        {
            // Arrange
            var nupkgPath = Path.Combine(_root, "Empty.1.0.0.nupkg");
            using (var archive = ZipFile.Open(nupkgPath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("lib/netstandard2.1/Empty.dll");
            }

            // Act
            var version = UploadCommand.ReadNupkgVersion(nupkgPath);

            // Assert
            Assert.IsNull(version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.3")]
        public void PackWithVersionWhenOneWasGiven()
        {
            // Arrange / Act
            var args = UploadCommand.BuildPackArgs(ProjectAt("Acme.Energy"), "1.4.0");

            // Assert
            CollectionAssert.AreEqual(new[] { Path.Combine(_root, "Acme.Energy.csproj"), "-c", "Release", "-p:IsPackable=true", "-p:Version=1.4.0" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.3")]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void PackWithoutVersionWhenNoneWasGiven(string? version)
        {
            // Arrange / Act
            var args = UploadCommand.BuildPackArgs(ProjectAt("Acme.Energy"), version);

            // Assert
            CollectionAssert.AreEqual(new[] { Path.Combine(_root, "Acme.Energy.csproj"), "-c", "Release", "-p:IsPackable=true" }, args);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.8")]
        public void TreatDuplicateVersionConflictAsSkippable()
        {
            // Arrange / Act
            var skippable = UploadCommand.IsVersionAlreadyExistsConflict("""{"statusCode":409,"message":"Version 1.4.0 already exists for this library."}""");

            // Assert
            Assert.IsTrue(skippable);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.8")]
        [DataRow("""{"statusCode":409,"message":"The package id belongs to another integrator."}""")]
        [DataRow("""{"statusCode":409,"message":"Conflict."}""")]
        [DataRow("")]
        public void TreatAnyOtherConflictAsFailure(string body)
        {
            // Arrange / Act
            var skippable = UploadCommand.IsVersionAlreadyExistsConflict(body);

            // Assert
            Assert.IsFalse(skippable);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.9")]
        public void KeepParserNoticesAndNothingElseFromPackOutput()
        {
            // Arrange
            var packOutput = "  Determining projects to restore...\r\n" +
                             "Vion Dale: excluded 1 development-only logic block from the introspection document: Simulator\r\n" +
                             "  MyLib -> C:\\work\\MyLib\\bin\\Release\\MyLib.dll\r\n";

            // Act
            var notices = UploadCommand.ExtractPackNotices(packOutput);

            // Assert
            CollectionAssert.AreEqual(new[] { "excluded 1 development-only logic block from the introspection document: Simulator" }, notices);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-011.9")]
        public void KeepNoNoticesFromOrdinaryPackOutput()
        {
            // Arrange / Act
            var notices = UploadCommand.ExtractPackNotices("  Determining projects to restore...\r\n  MyLib -> bin\\Release\\MyLib.dll\r\n");

            // Assert
            Assert.AreEqual(0, notices.Count);
        }

        private DaleProject ProjectAt(string packageId)
        {
            return new DaleProject
                   {
                       CsprojPath = Path.Combine(_root, packageId + ".csproj"),
                       ProjectName = packageId,
                       ProjectDirectory = _root,
                       PackageId = packageId,
                   };
        }
    }
}
