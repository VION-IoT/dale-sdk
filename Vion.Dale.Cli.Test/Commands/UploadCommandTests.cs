using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class UploadCommandTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DaleCliTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [TestMethod]
        public void ReadNupkgVersion_ReadsVersionFromNuspec()
        {
            // A real .nuspec carries the packaging namespace; the reader must be namespace-agnostic.
            var nupkg = CreateNupkg("Foo.1.2.3-preview.nupkg",
                                    @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"">
  <metadata>
    <id>Foo</id>
    <version>1.2.3-preview</version>
  </metadata>
</package>");

            Assert.AreEqual("1.2.3-preview", UploadCommand.ReadNupkgVersion(nupkg));
        }

        [TestMethod]
        public void ReadNupkgVersion_ReturnsNullWhenNoNuspec()
        {
            var nupkg = Path.Combine(_tempDir, "Empty.nupkg");
            using (var zip = ZipFile.Open(nupkg, ZipArchiveMode.Create))
            {
                zip.CreateEntry("lib/netstandard2.1/Foo.dll");
            }

            Assert.IsNull(UploadCommand.ReadNupkgVersion(nupkg));
        }

        [TestMethod]
        public void BuildPackArgs_WithVersion_InjectsVersionProperty()
        {
            var project = new DaleProject { CsprojPath = Path.Combine(_tempDir, "Foo.csproj"), ProjectDirectory = _tempDir, PackageId = "Foo" };

            var args = UploadCommand.BuildPackArgs(project, "9.9.9");

            Assert.AreEqual(project.CsprojPath, args[0]);
            CollectionAssert.Contains(args, "-c");
            CollectionAssert.Contains(args, "Release");
            CollectionAssert.Contains(args, "-p:IsPackable=true");
            CollectionAssert.Contains(args, "-p:Version=9.9.9");
        }

        [TestMethod]
        public void BuildPackArgs_WithoutVersion_OmitsVersionProperty()
        {
            var project = new DaleProject { CsprojPath = Path.Combine(_tempDir, "Foo.csproj"), ProjectDirectory = _tempDir, PackageId = "Foo" };

            var args = UploadCommand.BuildPackArgs(project, null);

            CollectionAssert.Contains(args, "-p:IsPackable=true");
            Assert.IsFalse(args.Any(a => a.StartsWith("-p:Version=", StringComparison.Ordinal)), "No version property when --version is not passed.");
        }

        private string CreateNupkg(string fileName, string nuspec)
        {
            var path = Path.Combine(_tempDir, fileName);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("Foo.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(nuspec);
            return path;
        }
    }
}