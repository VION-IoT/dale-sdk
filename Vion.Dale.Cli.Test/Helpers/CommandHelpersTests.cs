using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class CommandHelpersTests
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
        public void FindDaleProjectsInSolution_SelectsLibraryAndExcludesTestAndDevHost()
        {
            // Mirrors a real example solution: a library that references Vion.Dale.Sdk,
            // a test project that references the TestKit, and a DevHost project.
            WriteProject("MyLib", @"<PackageReference Include=""Vion.Dale.Sdk"" Version=""0.7.0"" />");
            WriteProject("MyLib.Test",
                         @"<PackageReference Include=""Vion.Dale.Sdk.TestKit"" Version=""0.7.0"" />
    <ProjectReference Include=""..\MyLib\MyLib.csproj"" />");
            WriteProject("MyLib.DevHost",
                         @"<PackageReference Include=""Vion.Dale.DevHost.Web"" Version=""0.7.0"" />
    <ProjectReference Include=""..\MyLib\MyLib.csproj"" />");

            var slnPath = WriteSolution("MyLib", "MyLib.Test", "MyLib.DevHost");

            var daleProjects = CommandHelpers.FindDaleProjectsInSolution(slnPath);

            Assert.AreEqual(1, daleProjects.Count, "Only the library references Vion.Dale.Sdk directly.");
            Assert.AreEqual("MyLib.csproj", Path.GetFileName(daleProjects[0]));
        }

        private void WriteProject(string name, string itemGroupBody)
        {
            var projectDir = Path.Combine(_tempDir, name);
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, name + ".csproj"),
                              $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup>
  <ItemGroup>
    {itemGroupBody}
  </ItemGroup>
</Project>");
        }

        private string WriteSolution(params string[] projectNames)
        {
            var slnPath = Path.Combine(_tempDir, "MyApp.sln");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            foreach (var name in projectNames)
            {
                sb.AppendLine($@"Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{name}"", ""{name}\{name}.csproj"", ""{{{System.Guid.Empty}}}""");
                sb.AppendLine("EndProject");
            }

            File.WriteAllText(slnPath, sb.ToString());
            return slnPath;
        }
    }
}