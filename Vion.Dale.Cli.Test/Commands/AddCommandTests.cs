using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands.Add;

namespace Vion.Dale.Cli.Test.Commands
{
    [TestClass]
    public class AddCommandTests
    {
        [TestMethod]
        public void BuildPropertySnippet_PrivateSetter_Default()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Temperature", "double", "private", null, false);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Temperature\")]"));
            Assert.IsTrue(snippet.Contains("public double Temperature { get; private set; }"));
        }

        [TestMethod]
        public void BuildPropertySnippet_PublicSetter()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "public", null, false);

            Assert.IsTrue(snippet.Contains("public string Mode { get; set; }"));
        }

        [TestMethod]
        public void BuildPropertySnippet_CustomDefaultName()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Temp", "double", "private", "Temperature", false);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Temperature\")]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_WithPersistent()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, true);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Mode\")]"));
            Assert.IsTrue(snippet.Contains("[Persistent]"));
        }

        [TestMethod]
        public void BuildTimerSnippet_GeneratesCorrectCode()
        {
            var snippet = AddTimerCommand.BuildTimerSnippet("HeartbeatTick", 5);

            Assert.IsTrue(snippet.Contains("[Timer(5)]"));
            Assert.IsTrue(snippet.Contains("private void HeartbeatTick()"));
        }

        [TestMethod]
        public void BuildTimerSnippet_DecimalInterval()
        {
            var snippet = AddTimerCommand.BuildTimerSnippet("FastTick", 0.5);

            Assert.IsTrue(snippet.Contains("[Timer(0.5)]"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_Default()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Temperature", "double", null, false);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Temperature\")]"));
            Assert.IsTrue(snippet.Contains("public double Temperature { get; private set; }"));
            Assert.IsFalse(snippet.Contains("[Persistent]"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_CustomDefaultName()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Temp", "double", "Temperature Sensor", false);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Temperature Sensor\")]"));
            Assert.IsTrue(snippet.Contains("public double Temp { get; private set; }"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_WithPersistent()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("TotalCount", "int", null, true);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"TotalCount\")]"));
            Assert.IsTrue(snippet.Contains("[Persistent]"));
            Assert.IsTrue(snippet.Contains("public int TotalCount { get; private set; }"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_AlwaysPrivateSet()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Value", "bool", null, false);

            // Measuring points always have private set — no public setter option
            Assert.IsTrue(snippet.Contains("private set;"));
            Assert.IsFalse(snippet.Contains("{ get; set; }"));
        }

        [TestMethod]
        public void AddServicePropertyCommand_DefaultNameOption_DescriptionReferencesTitle()
        {
            var command = AddServicePropertyCommand.Create();
            var option = command.Options.SingleOrDefault(o => o.Name == "--default-name");

            Assert.IsNotNull(option, "Expected a --default-name option on the serviceproperty command.");
            Assert.IsNotNull(option!.Description);
            StringAssert.Contains(option.Description, "Title");
            Assert.IsFalse(
                option.Description!.Contains("DefaultName parameter"),
                "Description still references the obsolete DefaultName attribute member.");
        }

        [TestMethod]
        public void AddMeasuringPointCommand_DefaultNameOption_DescriptionReferencesTitle()
        {
            var command = AddMeasuringPointCommand.Create();
            var option = command.Options.SingleOrDefault(o => o.Name == "--default-name");

            Assert.IsNotNull(option, "Expected a --default-name option on the measuringpoint command.");
            Assert.IsNotNull(option!.Description);
            StringAssert.Contains(option.Description, "Title");
            Assert.IsFalse(
                option.Description!.Contains("DefaultName parameter"),
                "Description still references the obsolete DefaultName attribute member.");
        }
    }
}
