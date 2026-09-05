using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Output;
using Vion.Dale.Cli.Test.TestHelpers;

namespace Vion.Dale.Cli.Test.Commands.Add
{
    /// <summary>
    ///     The four <c>add</c> verbs driven through the parser against a project on disk. The snippet
    ///     builders are covered by <c>AddCommandTests</c>; what only this shape can show is what the
    ///     command writes — or refuses to write — into a real file.
    /// </summary>
    [TestClass]
    public class AddCommandsShould
    {
        private static readonly byte[] ByteOrderMark = { 0xEF, 0xBB, 0xBF };

        private TextWriter _originalOut = null!;

        private StringWriter _standardOutput = null!;

        [TestInitialize]
        public void Setup()
        {
            _originalOut = Console.Out;
            _standardOutput = new StringWriter();
            Console.SetOut(_standardOutput);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(_originalOut);
            DaleConsole.JsonMode = false;
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.4")]
        [DataRow("logicblock", "My Block", "")]
        [DataRow("serviceproperty", "My Prop", "--type|double")]
        [DataRow("measuringpoint", "My Point", "--type|double")]
        [DataRow("timer", "My Timer", "--interval|5")]
        public async Task RefuseNonIdentifierNameBeforeWritingAnything(string verb, string name, string extraArguments)
        {
            // Arrange
            using var project = new TemporaryDaleProject();
            var blockBefore = project.ReadBytes("MyBlock.cs");
            var filesBefore = Directory.GetFiles(project.Directory).Length;
            var arguments = new[] { "add", verb, name }.Concat(extraArguments.Split('|', StringSplitOptions.RemoveEmptyEntries))
                                                       .Concat(new[] { "--project", project.CsprojPath })
                                                       .ToArray();

            // Act
            var exit = await Program.BuildRootCommand().Parse(arguments).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
            CollectionAssert.AreEqual(blockBefore, project.ReadBytes("MyBlock.cs"));
            Assert.AreEqual(filesBefore, Directory.GetFiles(project.Directory).Length);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        [DataRow("serviceproperty")]
        [DataRow("measuringpoint")]
        public async Task RefuseNonTypeReferenceBeforeWritingAnything(string verb)
        {
            // Arrange
            using var project = new TemporaryDaleProject();
            var blockBefore = project.ReadBytes("MyBlock.cs");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "add", verb, "Torque", "--type", "not a type", "--project", project.CsprojPath }).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
            CollectionAssert.AreEqual(blockBefore, project.ReadBytes("MyBlock.cs"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.11")]
        [DataRow("serviceproperty", "[ServiceProperty(Title = \"Power\")]")]
        [DataRow("measuringpoint", "[ServiceMeasuringPoint(Title = \"Power\")]")]
        public async Task EmitPresentationAttributeOntoAnnotatedMember(string verb, string expectedAnnotation)
        {
            // Arrange
            using var project = new TemporaryDaleProject();

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "add", verb, "Power", "--type", "double", "--decimals", "2", "--project", project.CsprojPath }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(project.ReadText("MyBlock.cs"), $"        {expectedAnnotation}\n        [Presentation(Decimals = 2)]\n        public double Power");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.11")]
        public async Task SkipPresentationAttributeMemberAlreadyCarries()
        {
            // Arrange
            using var project = new TemporaryDaleProject("""
                                                         using Vion.Dale.Sdk.Core;

                                                         namespace MyLib
                                                         {
                                                             public class MyBlock : LogicBlockBase
                                                             {
                                                                 [ServiceProperty(Title = "Power")]
                                                                 [Presentation(Decimals = 1)]
                                                                 public double Power { get; private set; }
                                                             }
                                                         }
                                                         """);

            // Act
            var exit = await Program.BuildRootCommand()
                                    .Parse(new[] { "add", "measuringpoint", "Power", "--type", "double", "--decimals", "2", "--project", project.CsprojPath })
                                    .InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            var source = project.ReadText("MyBlock.cs");
            Assert.AreEqual(1, CountOccurrences(source, "[Presentation("), "a second [Presentation] on one member does not compile");
            StringAssert.Contains(source, "[Presentation(Decimals = 1)]");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.13")]
        [DataRow("serviceproperty")]
        [DataRow("measuringpoint")]
        public async Task CarryPersistenceOntoAnnotatedMember(string verb)
        {
            // Arrange
            using var project = new TemporaryDaleProject();

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "add", verb, "Power", "--type", "double", "--persistent", "--project", project.CsprojPath }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(project.ReadText("MyBlock.cs"), "[Persistent]");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.5")]
        [DataRow("serviceproperty", "\"annotated\": \"ServiceProperty\"")]
        [DataRow("measuringpoint", "\"annotated\": \"ServiceMeasuringPoint\"")]
        public async Task ReportAnnotatedMemberAsJsonDocument(string verb, string expectedAnnotationMember)
        {
            // Arrange
            using var project = new TemporaryDaleProject();
            DaleConsole.JsonMode = true;

            // Act
            var exit = await Program.BuildRootCommand()
                                    .Parse(new[] { "add", verb, "Power", "--type", "double", "--output", "json", "--project", project.CsprojPath })
                                    .InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            StringAssert.Contains(_standardOutput.ToString(), expectedAnnotationMember);
            StringAssert.Contains(_standardOutput.ToString(), "\"logicBlock\": \"MyBlock\"");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.3")]
        public async Task PreserveLineEndingAndByteOrderMarkOfRegistrationFile()
        {
            // Arrange
            using var project = new TemporaryDaleProject();
            const string dependencyInjection = """
                                               namespace MyLib
                                               {
                                                   public static class DependencyInjection
                                                   {
                                                       public static void ConfigureServices(IServiceCollection services)
                                                       {
                                                           services.AddTransient<MyBlock>();
                                                       }
                                                   }
                                               }
                                               """;
            project.WriteDependencyInjection(dependencyInjection, "\r\n", true);
            var before = project.ReadBytes("DependencyInjection.cs");
            const string registration = "            services.AddTransient<Widget>();\r\n";

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "add", "logicblock", "Widget", "--project", project.CsprojPath }).InvokeAsync();

            // Assert
            Assert.AreEqual(0, exit);
            var after = project.ReadBytes("DependencyInjection.cs");
            Assert.AreEqual(before.Length + Encoding.UTF8.GetByteCount(registration), after.Length, "only the registration line's bytes were added");
            var beforeText = Encoding.UTF8.GetString(before, ByteOrderMark.Length, before.Length - ByteOrderMark.Length);
            var expected = ByteOrderMark.Concat(Encoding.UTF8.GetBytes(beforeText.Replace("            services.AddTransient<MyBlock>();\r\n",
                                                                                          "            services.AddTransient<MyBlock>();\r\n" + registration)))
                                        .ToArray();
            CollectionAssert.AreEqual(expected, after);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}