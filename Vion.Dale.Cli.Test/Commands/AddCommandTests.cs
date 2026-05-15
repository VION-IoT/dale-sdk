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
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Temperature", "double", "private", null, false, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Temperature\")]"));
            Assert.IsTrue(snippet.Contains("public double Temperature { get; private set; }"));
        }

        [TestMethod]
        public void BuildPropertySnippet_PublicSetter()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "public", null, false, null, null, null, null);

            Assert.IsTrue(snippet.Contains("public string Mode { get; set; }"));
        }

        [TestMethod]
        public void BuildPropertySnippet_CustomDefaultName()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Temp", "double", "private", "Temperature", false, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Temperature\")]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_WithPersistent()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, true, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceProperty(Title = \"Mode\")]"));
            Assert.IsTrue(snippet.Contains("[Persistent]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_NoPresentationFlags_OmitsPresentationAttribute()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, null, null, null, null);

            Assert.IsFalse(snippet.Contains("[Presentation"));
        }

        [TestMethod]
        public void BuildPropertySnippet_GroupKnownName_EmitsConstantReference()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, "Status", null, null, null);

            Assert.IsTrue(snippet.Contains("[Presentation(Group = PropertyGroup.Status)]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_GroupRawString_EmitsStringLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, "Custom", null, null, null);

            Assert.IsTrue(snippet.Contains("[Presentation(Group = \"Custom\")]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_Importance_EmitsEnumReference()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, null, "Primary", null, null);

            Assert.IsTrue(snippet.Contains("[Presentation(Importance = Importance.Primary)]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_Decimals_EmitsIntLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "double", "private", null, false, null, null, 2, null);

            Assert.IsTrue(snippet.Contains("[Presentation(Decimals = 2)]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_Format_EmitsStringLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, null, null, null, "X");

            Assert.IsTrue(snippet.Contains("[Presentation(Format = \"X\")]"));
        }

        [TestMethod]
        public void BuildPropertySnippet_AllPresentationFlags_StableOrder()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "double", "private", null, false, "Metric", "Secondary", 1, "iso");

            Assert.IsTrue(
                snippet.Contains("[Presentation(Group = PropertyGroup.Metric, Importance = Importance.Secondary, Decimals = 1, Format = \"iso\")]"),
                $"Unexpected presentation attribute. Snippet was:\n{snippet}");
        }

        [TestMethod]
        public void BuildPropertySnippet_PresentationAfterServicePropertyBeforeDeclaration()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, "Status", null, null, null);

            var spIndex = snippet.IndexOf("[ServiceProperty(", System.StringComparison.Ordinal);
            var presIndex = snippet.IndexOf("[Presentation(", System.StringComparison.Ordinal);
            var declIndex = snippet.IndexOf("public string Mode", System.StringComparison.Ordinal);

            Assert.IsTrue(spIndex >= 0 && presIndex > spIndex && declIndex > presIndex,
                          $"Expected [ServiceProperty] then [Presentation] then declaration. Snippet:\n{snippet}");
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
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Temperature", "double", null, false, null, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Temperature\")]"));
            Assert.IsTrue(snippet.Contains("public double Temperature { get; private set; }"));
            Assert.IsFalse(snippet.Contains("[Persistent]"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_CustomDefaultName()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Temp", "double", "Temperature Sensor", false, null, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Temperature Sensor\")]"));
            Assert.IsTrue(snippet.Contains("public double Temp { get; private set; }"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_WithPersistent()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("TotalCount", "int", null, true, null, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"TotalCount\")]"));
            Assert.IsTrue(snippet.Contains("[Persistent]"));
            Assert.IsTrue(snippet.Contains("public int TotalCount { get; private set; }"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_AlwaysPrivateSet()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Value", "bool", null, false, null, null, null, null, null);

            // Measuring points always have private set — no public setter option
            Assert.IsTrue(snippet.Contains("private set;"));
            Assert.IsFalse(snippet.Contains("{ get; set; }"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_NoKind_ServiceMeasuringPointUnchanged()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Energy", "double", null, false, null, null, null, null, null);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Energy\")]"));
            Assert.IsFalse(snippet.Contains("Kind ="));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_Kind_EmittedInsideServiceMeasuringPoint()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Energy", "double", null, false, "Total", null, null, null, null);

            Assert.IsTrue(
                snippet.Contains("[ServiceMeasuringPoint(Title = \"Energy\", Kind = MeasuringPointKind.Total)]"),
                $"Unexpected attribute. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_KindAndPresentationFlags_Combined()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Energy", "double", null, false, "TotalIncreasing", "Metric", "Secondary", 1, null);

            Assert.IsTrue(snippet.Contains("[ServiceMeasuringPoint(Title = \"Energy\", Kind = MeasuringPointKind.TotalIncreasing)]"));
            Assert.IsTrue(snippet.Contains("[Presentation(Group = PropertyGroup.Metric, Importance = Importance.Secondary, Decimals = 1)]"));
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_Group_RawString()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Energy", "double", null, false, null, "acme.power", null, null, null);

            Assert.IsTrue(snippet.Contains("[Presentation(Group = \"acme.power\")]"));
        }

        [TestMethod]
        public void GenerateLogicBlock_NoNameOrIcon_NoLogicBlockAttribute()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", null, null);

            Assert.IsFalse(content.Contains("[LogicBlock"));
            Assert.IsTrue(content.Contains("public class Foo : LogicBlockBase"));
        }

        [TestMethod]
        public void GenerateLogicBlock_NameAndIcon_EmitsLogicBlockAttribute()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", "Foo Display", "bar");

            Assert.IsTrue(
                content.Contains("[LogicBlock(Name = \"Foo Display\", Icon = \"bar\")]"),
                $"Unexpected content:\n{content}");
            Assert.IsTrue(content.Contains("public class Foo : LogicBlockBase"));
        }

        [TestMethod]
        public void GenerateLogicBlock_NameOnly_EmitsNameOnly()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", "Foo Display", null);

            Assert.IsTrue(content.Contains("[LogicBlock(Name = \"Foo Display\")]"));
            Assert.IsFalse(content.Contains("Icon ="));
        }

        [TestMethod]
        public void GenerateLogicBlock_IconOnly_EmitsIconOnly()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", null, "bar");

            Assert.IsTrue(content.Contains("[LogicBlock(Icon = \"bar\")]"));
            Assert.IsFalse(content.Contains("Name ="));
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

        // --- FIX 2: emitted-string-literal escaping (prevents Critical codegen regression) ---

        [TestMethod]
        public void EscapeCsString_EscapesBackslashAndDoubleQuote()
        {
            Assert.AreEqual("My \\\"X\\\\Y\\\"", PresentationSnippet.EscapeCsString("My \"X\\Y\""));
        }

        [TestMethod]
        public void BuildPropertySnippet_Format_WithQuoteAndBackslash_EscapedLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, null, null, null, "My \"X\\Y\"");

            Assert.IsTrue(
                snippet.Contains("[Presentation(Format = \"My \\\"X\\\\Y\\\"\")]"),
                $"Unexpected presentation attribute. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void BuildPropertySnippet_RawGroup_WithQuoteAndBackslash_EscapedLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, "My \"X\\Y\"", null, null, null);

            Assert.IsTrue(
                snippet.Contains("[Presentation(Group = \"My \\\"X\\\\Y\\\"\")]"),
                $"Unexpected presentation attribute. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void GenerateLogicBlock_NameAndIcon_WithQuoteAndBackslash_EscapedLiteral()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", "My \"X\\Y\"", "ab\\\"cd");

            Assert.IsTrue(
                content.Contains("[LogicBlock(Name = \"My \\\"X\\\\Y\\\"\", Icon = \"ab\\\\\\\"cd\")]"),
                $"Unexpected content:\n{content}");
        }

        [TestMethod]
        public void BuildPropertySnippet_Title_WithQuoteAndBackslash_EscapedLiteral()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", "My \"X\\Y\"", false, null, null, null, null);

            Assert.IsTrue(
                snippet.Contains("[ServiceProperty(Title = \"My \\\"X\\\\Y\\\"\")]"),
                $"Unexpected attribute. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void BuildMeasuringPointSnippet_Title_WithQuoteAndBackslash_EscapedLiteral()
        {
            var snippet = AddMeasuringPointCommand.BuildMeasuringPointSnippet("Mp", "double", "My \"X\\Y\"", false, null, null, null, null, null);

            Assert.IsTrue(
                snippet.Contains("[ServiceMeasuringPoint(Title = \"My \\\"X\\\\Y\\\"\")]"),
                $"Unexpected attribute. Snippet:\n{snippet}");
        }

        // --- FIX 3: KnownGroups drift guard (change-detector test) ---

        [TestMethod]
        public void PresentationSnippet_KnownGroups_PinnedToPropertyGroupConstantNames()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Status", "Configuration", "Metric", "Diagnostics", "Identity", "Alarm" },
                PresentationSnippet.KnownGroups,
                "KnownGroups drifted from Vion.Dale.Sdk.Core.PropertyGroup constant names. " +
                "Keep them in lockstep (CLI has no SDK ref). 'None' is intentionally excluded.");
        }

        // --- FIX 4: empty/whitespace input must not emit empty literals ---

        [TestMethod]
        public void BuildPropertySnippet_EmptyGroup_OmitsPresentationAttribute()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, "", null, null, null);

            Assert.IsFalse(snippet.Contains("[Presentation"), $"Expected no [Presentation]. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void BuildPropertySnippet_WhitespaceFormat_OmitsPresentationAttribute()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, false, null, null, null, "   ");

            Assert.IsFalse(snippet.Contains("[Presentation"), $"Expected no [Presentation]. Snippet:\n{snippet}");
        }

        [TestMethod]
        public void GenerateLogicBlock_EmptyNameAndIcon_NoLogicBlockAttribute()
        {
            var content = AddLogicBlockCommand.GenerateLogicBlock("Foo", "My.Ns", "", "   ");

            Assert.IsFalse(content.Contains("[LogicBlock"), $"Expected no [LogicBlock]. Content:\n{content}");
            Assert.IsTrue(content.Contains("public class Foo : LogicBlockBase"));
        }

        // --- FIX 5: full attribute ordering ---

        [TestMethod]
        public void BuildPropertySnippet_ServicePropertyPresentationPersistent_StableOrder()
        {
            var snippet = AddServicePropertyCommand.BuildPropertySnippet("Mode", "string", "private", null, true, "Status", null, null, null);

            var spIndex = snippet.IndexOf("[ServiceProperty(", System.StringComparison.Ordinal);
            var presIndex = snippet.IndexOf("[Presentation(", System.StringComparison.Ordinal);
            var persIndex = snippet.IndexOf("[Persistent]", System.StringComparison.Ordinal);
            var declIndex = snippet.IndexOf("public string Mode", System.StringComparison.Ordinal);

            Assert.IsTrue(
                spIndex >= 0 && presIndex > spIndex && persIndex > presIndex && declIndex > persIndex,
                $"Expected [ServiceProperty] then [Presentation] then [Persistent] then declaration. Snippet:\n{snippet}");
        }
    }
}
