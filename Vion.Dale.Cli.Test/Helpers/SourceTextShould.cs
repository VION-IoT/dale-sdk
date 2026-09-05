using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    /// <summary>
    ///     What <c>dale add</c> leaves of a file it did not otherwise change: the bytes that are not the
    ///     inserted lines.
    /// </summary>
    [TestClass]
    public class SourceTextShould
    {
        private const string BlockBody =
            "using Vion.Dale.Sdk.Core;\nNL\nnamespace MyLib\n{\n    public class Thermostat : LogicBlockBase\n    {\n        private int _count;\n    }\n}\n";

        private string _root = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "dale-cli-bytes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
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
        [TestProperty("spec", "AC-CLI-007.3")]
        [DataRow("\n", false)]
        [DataRow("\r\n", false)]
        [DataRow("\n", true)]
        [DataRow("\r\n", true)]
        public void PreserveLineEndingsAndByteOrderMarkAroundInsertedLines(string newLine, bool withByteOrderMark)
        {
            // Arrange
            var path = WriteBlock(newLine, withByteOrderMark);
            var before = File.ReadAllBytes(path);

            // Act
            var inserted = SourceInserter.InsertIntoClass(path, "Thermostat", "[ServiceProperty(Title = \"Power\")]\npublic double Power { get; private set; }");

            // Assert
            Assert.IsTrue(inserted);
            var after = File.ReadAllBytes(path);
            Assert.AreEqual(withByteOrderMark, StartsWithByteOrderMark(after));
            Assert.AreEqual(CountOf(before, newLine) + 3, CountOf(after, newLine));
            if (newLine == "\n")
            {
                Assert.AreEqual(0, CountOf(after, "\r\n"), "a line-feed file must not gain carriage returns");
            }
            else
            {
                Assert.AreEqual(CountOf(after, "\r\n"), CountOf(after, "\n"), "a carriage-return file must not gain bare line feeds");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.3")]
        public void LeaveEveryLineItDidNotInsertByteIdentical()
        {
            // Arrange
            var path = WriteBlock("\n", false);
            var before = File.ReadAllText(path).Split('\n');

            // Act
            SourceInserter.InsertIntoClass(path, "Thermostat", "public double Power { get; private set; }");

            // Assert
            var after = File.ReadAllText(path).Split('\n');
            CollectionAssert.IsSubsetOf(before, after);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        public void AddUsingOnceOnly()
        {
            // Arrange
            var path = WriteBlock("\n", false);

            // Act
            SourceInserter.EnsureUsing(path, "Vion.Dale.Sdk.Core");
            SourceInserter.EnsureUsing(path, "Vion.Dale.Sdk.Core");

            // Assert
            Assert.AreEqual(1, CountOf(File.ReadAllBytes(path), "using Vion.Dale.Sdk.Core;"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        public void FindMemberWithItsTypeAndAttributes()
        {
            // Arrange
            var source = "    [ServiceProperty(Title = \"Power\")]\n    [Persistent]\n    public double Power { get; private set; }\n";

            // Act
            var member = SourceInserter.FindMember(source, "Power");

            // Assert
            Assert.IsNotNull(member);
            Assert.IsTrue(member!.IsPropertyOfType("double"));
            Assert.IsTrue(member.CarriesAttribute("ServiceProperty"));
            Assert.IsFalse(member.CarriesAttribute("ServiceMeasuringPoint"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        public void FindNoMemberWhereSourceDeclaresNone()
        {
            // Arrange / Act
            var member = SourceInserter.FindMember("    public double Power { get; private set; }\n", "Torque");

            // Assert
            Assert.IsNull(member);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.7")]
        public void AddAttributeAboveMemberAtItsOwnIndentation()
        {
            // Arrange
            var path = Path.Combine(_root, "Block.cs");
            File.WriteAllText(path,
                              "namespace MyLib\n{\n    public class Thermostat : LogicBlockBase\n    {\n        [ServiceProperty(Title = \"Power\")]\n        public double Power { get; private set; }\n    }\n}\n");

            // Act
            var added = SourceInserter.AddAttributeToMember(path, "Power", "[ServiceMeasuringPoint(Title = \"Power\")]");

            // Assert
            Assert.IsTrue(added);
            var text = File.ReadAllText(path);
            StringAssert.Contains(text, "        [ServiceMeasuringPoint(Title = \"Power\")]\n        [ServiceProperty(Title = \"Power\")]\n        public double Power");
        }

        private static bool StartsWithByteOrderMark(byte[] bytes)
        {
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        private static int CountOf(byte[] bytes, string value)
        {
            return CountOf(new UTF8Encoding(false).GetString(bytes), value);
        }

        private static int CountOf(string text, string value)
        {
            var count = 0;
            for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private string WriteBlock(string newLine, bool withByteOrderMark)
        {
            var path = Path.Combine(_root, "Thermostat.cs");
            var text = BlockBody.Replace("NL\n", string.Empty).Replace("\n", newLine);
            File.WriteAllText(path, text, new UTF8Encoding(withByteOrderMark));
            return path;
        }
    }
}