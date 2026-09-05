using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class CommandHelpersShould
    {
        private const string DaleCsproj = """
                                          <Project Sdk="Microsoft.NET.Sdk">
                                            <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
                                            <ItemGroup><PackageReference Include="Vion.Dale.Sdk" Version="0.11.2"/></ItemGroup>
                                          </Project>
                                          """;

        private const string PlainCsproj = """
                                           <Project Sdk="Microsoft.NET.Sdk">
                                             <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                                           </Project>
                                           """;

        private string _root = null!;

        private string _originalDirectory = null!;

        [TestInitialize]
        public void Setup()
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            _root = Path.Combine(Path.GetTempPath(), "dale-cli-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
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
        [TestProperty("spec", "AC-CLI-003.4")]
        public void BuildProjectNamedByFlagRatherThanSolutionAboveIt()
        {
            // Arrange
            var library = Path.Combine(_root, "MyLib");
            Directory.CreateDirectory(library);
            var csproj = Path.Combine(library, "MyLib.csproj");
            File.WriteAllText(csproj, DaleCsproj);
            File.WriteAllText(Path.Combine(_root, "Everything.sln"), string.Empty);
            Directory.SetCurrentDirectory(_root);

            // Act
            var target = CommandHelpers.RequireBuildTarget(csproj);

            // Assert
            Assert.AreEqual(csproj, target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        public void BuildSolutionWhenNoProjectNamed()
        {
            // Arrange
            var library = Path.Combine(_root, "MyLib");
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library, "MyLib.csproj"), DaleCsproj);
            var solution = Path.Combine(_root, "Everything.sln");
            File.WriteAllText(solution, string.Empty);
            Directory.SetCurrentDirectory(_root);

            // Act
            var target = CommandHelpers.RequireBuildTarget(null);

            // Assert
            Assert.AreEqual(solution, target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        public void RefuseProjectPathThatNamesNoFile()
        {
            // Arrange
            var missing = Path.Combine(_root, "NotThere.csproj");
            Directory.SetCurrentDirectory(_root);

            // Act
            var project = CommandHelpers.RequireProject(missing);

            // Assert
            Assert.IsNull(project);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        public void RefuseProjectPathThatNamesNoDaleProject()
        {
            // Arrange — a Dale project sits beside it, so a fall-back would find one and succeed.
            var library = Path.Combine(_root, "MyLib");
            Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(library, "MyLib.csproj"), DaleCsproj);
            var plain = Path.Combine(_root, "Tooling.csproj");
            File.WriteAllText(plain, PlainCsproj);
            File.WriteAllText(Path.Combine(_root, "Everything.sln"), """Project("{X}") = "MyLib", "MyLib\MyLib.csproj", "{Y}" """);
            Directory.SetCurrentDirectory(_root);

            // Act
            var project = CommandHelpers.RequireProject(plain);

            // Assert
            Assert.IsNull(project);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.7")]
        public void NameSuppliedPathWhenItCannotBeUsed()
        {
            // Arrange
            var missing = Path.Combine(_root, "NotThere.csproj");
            var plain = Path.Combine(_root, "Tooling.csproj");
            File.WriteAllText(plain, PlainCsproj);

            // Act
            var missingDescription = CommandHelpers.DescribeUnusableProjectPath(missing);
            var plainDescription = CommandHelpers.DescribeUnusableProjectPath(plain);

            // Assert
            StringAssert.Contains(missingDescription, missing);
            StringAssert.Contains(plainDescription, plain);
            StringAssert.Contains(plainDescription, "not a Dale project");
            Assert.IsFalse(missingDescription.Contains("use --project"), "the reader already used --project");
            Assert.IsFalse(plainDescription.Contains("use --project"), "the reader already used --project");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.8")]
        public void ReadDaleProjectsOutOfClassicSolution()
        {
            // Arrange
            var solution = Path.Combine(_root, "Everything.sln");
            WriteProject("MyLib", DaleCsproj);
            WriteProject("MyLib.Test", PlainCsproj);
            WriteProject("MyLib.DevHost", PlainCsproj);
            File.WriteAllText(solution,
                              "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                              "Project(\"{FAE04EC0}\") = \"MyLib\", \"MyLib\\MyLib.csproj\", \"{A}\"\r\nEndProject\r\n" +
                              "Project(\"{FAE04EC0}\") = \"MyLib.Test\", \"MyLib.Test\\MyLib.Test.csproj\", \"{B}\"\r\nEndProject\r\n" +
                              "Project(\"{FAE04EC0}\") = \"MyLib.DevHost\", \"MyLib.DevHost\\MyLib.DevHost.csproj\", \"{C}\"\r\nEndProject\r\n");

            // Act
            var daleProjects = CommandHelpers.FindDaleProjectsInSolution(solution);

            // Assert
            CollectionAssert.AreEqual(new[] { Path.Combine("MyLib", "MyLib.csproj") }, daleProjects);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.8")]
        public void ReadDaleProjectsOutOfXmlSolution()
        {
            // Arrange
            var solution = Path.Combine(_root, "Everything.slnx");
            WriteProject("MyLib", DaleCsproj);
            WriteProject("Tooling", PlainCsproj);
            File.WriteAllText(solution,
                              """
                              <Solution>
                                <Folder Name="/libraries/">
                                  <Project Path="MyLib/MyLib.csproj" />
                                </Folder>
                                <Project Path="Tooling/Tooling.csproj" />
                              </Solution>
                              """);

            // Act
            var daleProjects = CommandHelpers.FindDaleProjectsInSolution(solution);

            // Assert
            CollectionAssert.AreEqual(new[] { Path.Combine("MyLib", "MyLib.csproj") }, daleProjects);
        }

        private void WriteProject(string name, string content)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name + ".csproj"), content);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        public void ResolveProjectByWalkingUpWhenNoProjectNamed()
        {
            // Arrange
            var library = Path.Combine(_root, "MyLib");
            var nested = Path.Combine(library, "Blocks");
            Directory.CreateDirectory(nested);
            var csproj = Path.Combine(library, "MyLib.csproj");
            File.WriteAllText(csproj, DaleCsproj);
            Directory.SetCurrentDirectory(nested);

            // Act
            var project = CommandHelpers.RequireProject(null);

            // Assert
            Assert.AreEqual(csproj, project!.CsprojPath);
        }
    }
}
