using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class ProjectDiscoveryTests
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
        [TestProperty("spec", "AC-CLI-003.1")]
        public void FindProject_WithDaleSdkPackageReference_ReturnsProject()
        {
            // Arrange / Act
            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Version>1.2.3</Version>
    <RootNamespace>MyLib.Namespace</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.50"" />
  </ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            // Assert
            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
            Assert.AreEqual("1.2.3", project.Version);
            Assert.AreEqual("0.1.50", project.SdkVersion);
            Assert.AreEqual("MyLib.Namespace", project.RootNamespace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public void FindProject_WithDaleSdkProjectReference_ReturnsProject()
        {
            // Arrange / Act
            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Vion.Dale.Sdk\Vion.Dale.Sdk.csproj"" />
  </ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            // Assert
            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
            Assert.IsNull(project.SdkVersion);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public void FindProject_NoDaleSdkReference_ReturnsNull()
        {
            // Arrange / Act
            var csproj = Path.Combine(_tempDir, "OtherLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            // Assert
            Assert.IsNull(project);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.2")]
        public void FindProject_WalksUpDirectoryTree()
        {
            // Arrange / Act
            var subDir = Path.Combine(_tempDir, "src", "deep");
            Directory.CreateDirectory(subDir);

            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.50"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: subDir);

            // Assert
            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.2")]
        public void FindProject_ExplicitProjectPath()
        {
            // Arrange / Act
            var csproj = Path.Combine(_tempDir, "Explicit.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.42"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(csproj);

            // Assert
            Assert.IsNotNull(project);
            Assert.AreEqual("Explicit", project.ProjectName);
            Assert.AreEqual("0.1.42", project.SdkVersion);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public void FindLogicBlocks_FindsClassesExtendingLogicBlockBase()
        {
            // Arrange / Act
            var csFile = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(csFile,
                              @"
using Vion.Dale.Sdk.Core;

namespace MyLib
{
    public class TemperatureSensor : LogicBlockBase
    {
    }
}");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            // Assert
            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("TemperatureSensor", blocks[0].ClassName);
            Assert.AreEqual(csFile, blocks[0].FilePath);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public void FindLogicBlocks_IgnosBinAndObjDirectories()
        {
            // Arrange / Act
            var objDir = Path.Combine(_tempDir, "obj");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "Generated.cs"), "class Foo : LogicBlockBase {}");

            var binDir = Path.Combine(_tempDir, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "Built.cs"), "class Bar : LogicBlockBase {}");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            // Assert
            Assert.AreEqual(0, blocks.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public void FindLogicBlocks_MultipleBlocksInDifferentFiles()
        {
            // Arrange / Act
            File.WriteAllText(Path.Combine(_tempDir, "BlockA.cs"), "public class BlockA : LogicBlockBase { }");
            File.WriteAllText(Path.Combine(_tempDir, "BlockB.cs"), "public class BlockB : LogicBlockBase { }");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            // Assert
            Assert.AreEqual(2, blocks.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public void FindLogicBlocks_MultipleBlocksInSameFile()
        {
            // Arrange / Act
            File.WriteAllText(Path.Combine(_tempDir, "Blocks.cs"),
                              @"
public class BlockA : LogicBlockBase { }
public class BlockB : LogicBlockBase { }
");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            // Assert
            Assert.AreEqual(2, blocks.Count);
            Assert.IsTrue(blocks.Exists(b => b.ClassName == "BlockA"));
            Assert.IsTrue(blocks.Exists(b => b.ClassName == "BlockB"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_FindsSlnFile()
        {
            // Arrange / Act
            File.WriteAllText(Path.Combine(_tempDir, "MyApp.sln"), "solution content");

            var sln = ProjectDiscovery.FindSolution(_tempDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("MyApp.sln"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_FindsSlnxFile()
        {
            // Arrange / Act
            // .NET 10 `dotnet new sln` creates .slnx (XML solution format) by default.
            File.WriteAllText(Path.Combine(_tempDir, "MyApp.slnx"), "<Solution />");

            var sln = ProjectDiscovery.FindSolution(_tempDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("MyApp.slnx"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_PrefersSlnOverSlnxInSameDirectory()
        {
            // Arrange / Act
            // The .slnx sorts alphabetically before the .sln — preference must come from
            // the extension, not from file name ordering.
            File.WriteAllText(Path.Combine(_tempDir, "AAA.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(_tempDir, "ZZZ.sln"), "solution content");

            var sln = ProjectDiscovery.FindSolution(_tempDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("ZZZ.sln"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_NearestDirectoryWinsOverExtensionPreference()
        {
            // Arrange / Act
            // A .slnx in the starting directory beats a .sln further up the tree.
            var subDir = Path.Combine(_tempDir, "inner");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(_tempDir, "Outer.sln"), "solution content");
            File.WriteAllText(Path.Combine(subDir, "Inner.slnx"), "<Solution />");

            var sln = ProjectDiscovery.FindSolution(subDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("Inner.slnx"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_WalksUpToFindSlnx()
        {
            // Arrange / Act
            var subDir = Path.Combine(_tempDir, "src", "deep");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(_tempDir, "MyApp.slnx"), "<Solution />");

            var sln = ProjectDiscovery.FindSolution(subDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("MyApp.slnx"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_MultipleSolutionsOfSameType_ReturnsAlphabeticallyFirst()
        {
            // Arrange / Act
            File.WriteAllText(Path.Combine(_tempDir, "Zeta.sln"), "solution content");
            File.WriteAllText(Path.Combine(_tempDir, "Alpha.sln"), "solution content");

            var sln = ProjectDiscovery.FindSolution(_tempDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("Alpha.sln"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public void FindSolution_IgnoresLongerExtensionsStartingWithSln()
        {
            // Arrange / Act
            // Guards the explicit extension match: "*.sln"-style patterns can match longer
            // extensions on some platforms (legacy 8.3 short-name quirk). The decoy in the
            // nearer directory must be skipped in favor of the real solution above it.
            var subDir = Path.Combine(_tempDir, "inner");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(_tempDir, "Real.sln"), "solution content");
            File.WriteAllText(Path.Combine(subDir, "Decoy.slnxbak"), "not a solution");

            var sln = ProjectDiscovery.FindSolution(subDir);

            // Assert
            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("Real.sln"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.9")]
        public void FindProject_PackageIdFallsBackToProjectName()
        {
            // Arrange / Act
            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.50"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            // Assert
            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.PackageId);
        }
    }
}