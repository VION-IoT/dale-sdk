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
        public void FindProject_WithDaleSdkPackageReference_ReturnsProject()
        {
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

            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
            Assert.AreEqual("1.2.3", project.Version);
            Assert.AreEqual("0.1.50", project.SdkVersion);
            Assert.AreEqual("MyLib.Namespace", project.RootNamespace);
        }

        [TestMethod]
        public void FindProject_WithDaleSdkProjectReference_ReturnsProject()
        {
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

            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
            Assert.IsNull(project.SdkVersion);
        }

        [TestMethod]
        public void FindProject_NoDaleSdkReference_ReturnsNull()
        {
            var csproj = Path.Combine(_tempDir, "OtherLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            Assert.IsNull(project);
        }

        [TestMethod]
        public void FindProject_WalksUpDirectoryTree()
        {
            var subDir = Path.Combine(_tempDir, "src", "deep");
            Directory.CreateDirectory(subDir);

            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.50"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: subDir);

            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.ProjectName);
        }

        [TestMethod]
        public void FindProject_ExplicitProjectPath()
        {
            var csproj = Path.Combine(_tempDir, "Explicit.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.42"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(csproj);

            Assert.IsNotNull(project);
            Assert.AreEqual("Explicit", project.ProjectName);
            Assert.AreEqual("0.1.42", project.SdkVersion);
        }

        [TestMethod]
        public void FindLogicBlocks_FindsClassesExtendingLogicBlockBase()
        {
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

            Assert.AreEqual(1, blocks.Count);
            Assert.AreEqual("TemperatureSensor", blocks[0].ClassName);
            Assert.AreEqual(csFile, blocks[0].FilePath);
        }

        [TestMethod]
        public void FindLogicBlocks_IgnosBinAndObjDirectories()
        {
            var objDir = Path.Combine(_tempDir, "obj");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "Generated.cs"), "class Foo : LogicBlockBase {}");

            var binDir = Path.Combine(_tempDir, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "Built.cs"), "class Bar : LogicBlockBase {}");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            Assert.AreEqual(0, blocks.Count);
        }

        [TestMethod]
        public void FindLogicBlocks_MultipleBlocksInDifferentFiles()
        {
            File.WriteAllText(Path.Combine(_tempDir, "BlockA.cs"), "public class BlockA : LogicBlockBase { }");
            File.WriteAllText(Path.Combine(_tempDir, "BlockB.cs"), "public class BlockB : LogicBlockBase { }");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            Assert.AreEqual(2, blocks.Count);
        }

        [TestMethod]
        public void FindLogicBlocks_MultipleBlocksInSameFile()
        {
            File.WriteAllText(Path.Combine(_tempDir, "Blocks.cs"),
                              @"
public class BlockA : LogicBlockBase { }
public class BlockB : LogicBlockBase { }
");

            var blocks = ProjectDiscovery.FindLogicBlocks(_tempDir);

            Assert.AreEqual(2, blocks.Count);
            Assert.IsTrue(blocks.Exists(b => b.ClassName == "BlockA"));
            Assert.IsTrue(blocks.Exists(b => b.ClassName == "BlockB"));
        }

        [TestMethod]
        public void FindSolution_FindsSlnFile()
        {
            File.WriteAllText(Path.Combine(_tempDir, "MyApp.sln"), "solution content");

            var sln = ProjectDiscovery.FindSolution(_tempDir);

            Assert.IsNotNull(sln);
            Assert.IsTrue(sln.EndsWith("MyApp.sln"));
        }

        [TestMethod]
        public void FindProject_PackageIdFallsBackToProjectName()
        {
            var csproj = Path.Combine(_tempDir, "MyLib.csproj");
            File.WriteAllText(csproj,
                              @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include=""Vion.Dale.Sdk"" Version=""0.1.50"" /></ItemGroup>
</Project>");

            var project = ProjectDiscovery.FindProject(startDirectory: _tempDir);

            Assert.IsNotNull(project);
            Assert.AreEqual("MyLib", project.PackageId);
        }
    }
}
