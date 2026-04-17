using System.Collections.Generic;
using System.IO;
using Vion.Dale.Cli.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        public void InsertIntoClass_InsertsBeforeClosingBrace()
        {
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
        public void InsertIntoClass_AllmanStyleBraces()
        {
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
        public void InsertIntoClass_EmptyClassBody_CorrectIndentation()
        {
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
            Assert.IsTrue(content.Contains("        [Timer(5)]"), "Timer attribute should have 8-space indent");
            Assert.IsTrue(content.Contains("        private void Tick()"), "Method should have 8-space indent");
        }

        [TestMethod]
        public void InsertIntoClass_PreservesIndentation()
        {
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
            Assert.IsTrue(content.Contains("        [Timer(5)]"));
            Assert.IsTrue(content.Contains("        private void Tick()"));
        }

        [TestMethod]
        public void EnsureUsing_AddsIfMissing()
        {
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
            Assert.IsTrue(content.Contains("using Vion.Dale.Sdk.Core;"));
        }

        [TestMethod]
        public void EnsureUsing_DoesNotDuplicateExisting()
        {
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
            Assert.AreEqual(1, count);
        }

        [TestMethod]
        public void ResolveTarget_SingleBlock_AutoDetects()
        {
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "MyBlock", FilePath = "/path/MyBlock.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, null);

            Assert.IsNotNull(result);
            Assert.AreEqual("MyBlock", result.ClassName);
        }

        [TestMethod]
        public void ResolveTarget_MultipleBlocks_RequiresTo()
        {
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "BlockA", FilePath = "/path/A.cs" },
                             new() { ClassName = "BlockB", FilePath = "/path/B.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, null);

            Assert.IsNull(result);
        }

        [TestMethod]
        public void ResolveTarget_MultipleBlocks_WithToOption()
        {
            var blocks = new List<LogicBlockInfo>
                         {
                             new() { ClassName = "BlockA", FilePath = "/path/A.cs" },
                             new() { ClassName = "BlockB", FilePath = "/path/B.cs" },
                         };

            var result = SourceInserter.ResolveTarget(blocks, "BlockB");

            Assert.IsNotNull(result);
            Assert.AreEqual("BlockB", result.ClassName);
        }

        [TestMethod]
        public void InsertIntoClass_HandlesMethodBodiesWithBraces()
        {
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