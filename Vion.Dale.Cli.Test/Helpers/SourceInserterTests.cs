using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class SourceInserterTests
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
        [TestProperty("spec", "AC-CLI-007.1")]
        public void InsertIntoClass_InsertsBeforeClosingBrace()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(filePath,
                              @"namespace MyLib
{
    public class MyBlock : LogicBlockBase
    {
        public int Existing { get; set; }
    }
}
");

            var result = SourceInserter.InsertIntoClass(filePath, "MyBlock", "[ServiceProperty]\npublic double Temp { get; private set; }");

            // Assert
            Assert.IsTrue(result);
            var content = File.ReadAllText(filePath);
            Assert.IsTrue(content.Contains("[ServiceProperty]"));
            Assert.IsTrue(content.Contains("public double Temp { get; private set; }"));

            // Property must be inside the class body (after opening {, before closing })
            var classOpenBrace = content.IndexOf('{', content.IndexOf("class MyBlock"));
            var propIndex = content.IndexOf("[ServiceProperty]");
            var lastBrace = content.LastIndexOf('}');
            var classCloseBrace = content.LastIndexOf('}', lastBrace - 1);
            Assert.IsTrue(propIndex > classOpenBrace, "Snippet should be after the class opening brace");
            Assert.IsTrue(propIndex < classCloseBrace, "Snippet should be before the class closing brace");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public void InsertIntoClass_AllmanStyleBraces()
        {
            // Arrange / Act
            // Allman style: opening { on separate line from class declaration
            var filePath = Path.Combine(_tempDir, "Allman.cs");
            File.WriteAllText(filePath,
                              @"namespace MyLib
{
    public class MyBlock
        : LogicBlockBase
    {
        public int Existing { get; set; }
    }
}
");

            var result = SourceInserter.InsertIntoClass(filePath, "MyBlock", "[ServiceProperty]\npublic double Temp { get; private set; }");

            // Assert
            Assert.IsTrue(result);
            var content = File.ReadAllText(filePath);
            var classOpenBrace = content.IndexOf('{', content.IndexOf("class MyBlock"));
            var propIndex = content.IndexOf("[ServiceProperty]");
            var lastBrace = content.LastIndexOf('}');
            var classCloseBrace = content.LastIndexOf('}', lastBrace - 1);
            Assert.IsTrue(propIndex > classOpenBrace, "Snippet should be after the class opening brace");
            Assert.IsTrue(propIndex < classCloseBrace, "Snippet should be before the class closing brace");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public void InsertIntoClass_EmptyClassBody_CorrectIndentation()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "EmptyBlock.cs");
            File.WriteAllText(filePath,
                              @"namespace MyLib
{
    public class MyBlock : LogicBlockBase
    {
    }
}
");

            SourceInserter.InsertIntoClass(filePath, "MyBlock", "[Timer(5)]\nprivate void Tick()\n{\n}");

            var content = File.ReadAllText(filePath);

            // Should be at member-level indentation (8 spaces), not class-level (4 spaces)

            // Assert
            Assert.IsTrue(content.Contains("        [Timer(5)]"), "Timer attribute should have 8-space indent");
            Assert.IsTrue(content.Contains("        private void Tick()"), "Method should have 8-space indent");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public void InsertIntoClass_PreservesIndentation()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(filePath,
                              @"namespace MyLib
{
    public class MyBlock : LogicBlockBase
    {
        public int Existing { get; set; }
    }
}
");

            SourceInserter.InsertIntoClass(filePath, "MyBlock", "[Timer(5)]\nprivate void Tick()\n{\n}");

            var content = File.ReadAllText(filePath);

            // Check that the inserted code has proper indentation (matching existing members)

            // Assert
            Assert.IsTrue(content.Contains("        [Timer(5)]"));
            Assert.IsTrue(content.Contains("        private void Tick()"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        public void EnsureUsing_AddsIfMissing()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(filePath,
                              @"using System;

namespace MyLib
{
    public class MyBlock { }
}
");

            SourceInserter.EnsureUsing(filePath, "Vion.Dale.Sdk.Core");

            var content = File.ReadAllText(filePath);

            // Assert
            Assert.IsTrue(content.Contains("using Vion.Dale.Sdk.Core;"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        public void EnsureUsing_DoesNotDuplicateExisting()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(filePath,
                              @"using Vion.Dale.Sdk.Core;

namespace MyLib
{
    public class MyBlock { }
}
");

            SourceInserter.EnsureUsing(filePath, "Vion.Dale.Sdk.Core");

            var content = File.ReadAllText(filePath);
            var count = content.Split("using Vion.Dale.Sdk.Core;").Length - 1;

            // Assert
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.2")]
        public void ResolveTarget_SingleBlock_AutoDetects()
        {
            // Arrange / Act
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "MyBlock", FilePath = "/path/MyBlock.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, null);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("MyBlock", result.ClassName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.2")]
        public void ResolveTarget_MultipleBlocks_RequiresTo()
        {
            // Arrange / Act
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "BlockA", FilePath = "/path/A.cs" },
                             new() { ClassName = "BlockB", FilePath = "/path/B.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.2")]
        public void ResolveTarget_MultipleBlocks_WithToOption()
        {
            // Arrange / Act
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "BlockA", FilePath = "/path/A.cs" },
                             new() { ClassName = "BlockB", FilePath = "/path/B.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, "BlockB");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("BlockB", result.ClassName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public void InsertIntoClass_HandlesMethodBodiesWithBraces()
        {
            // Arrange / Act
            var filePath = Path.Combine(_tempDir, "MyBlock.cs");
            File.WriteAllText(filePath,
                              @"namespace MyLib
{
    public class MyBlock : LogicBlockBase
    {
        public void DoStuff()
        {
            if (true)
            {
                var x = 1;
            }
        }
    }
}
");

            var result = SourceInserter.InsertIntoClass(filePath, "MyBlock", "[Timer(5)]\nprivate void Tick()\n{\n}");

            // Assert
            Assert.IsTrue(result);
            var content = File.ReadAllText(filePath);
            Assert.IsTrue(content.Contains("[Timer(5)]"));

            // Verify the insertion is inside MyBlock, not after namespace
            var timerIndex = content.IndexOf("[Timer(5)]");
            var classCloseIndex = content.LastIndexOf('}', content.LastIndexOf('}') - 1);
            Assert.IsTrue(timerIndex < classCloseIndex);
        }
    }
}