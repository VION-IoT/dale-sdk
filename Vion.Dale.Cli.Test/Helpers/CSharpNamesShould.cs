using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Helpers;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class CSharpNamesShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.4")]
        [DataRow("Thermostat", true)]
        [DataRow("_private", true)]
        [DataRow("Block2", true)]
        [DataRow("My Block", false)]
        [DataRow("2Blocks", false)]
        [DataRow("My-Block", false)]
        [DataRow("My.Block", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void AcceptOnlyIdentifierAsMemberName(string? candidate, bool expectedAccepted)
        {
            // Arrange / Act
            var accepted = CSharpNames.IsIdentifier(candidate);

            // Assert
            Assert.AreEqual(expectedAccepted, accepted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        [DataRow("double", true)]
        [DataRow("string?", true)]
        [DataRow("int[]", true)]
        [DataRow("double?[]", true)]
        [DataRow("System.DateTimeOffset", true)]
        [DataRow("MyNamespace.MyEnum", true)]
        [DataRow("List<double>", true)]
        [DataRow("Dictionary<string,int>", true)]
        [DataRow("not a type", false)]
        [DataRow("double;", false)]
        [DataRow("double x", false)]
        [DataRow("List<>", false)]
        [DataRow("List<double", false)]
        [DataRow("", false)]
        [DataRow(null, false)]
        public void AcceptOnlyTypeReferenceAsMemberType(string? candidate, bool expectedAccepted)
        {
            // Arrange / Act
            var accepted = CSharpNames.IsTypeReference(candidate);

            // Assert
            Assert.AreEqual(expectedAccepted, accepted);
        }
    }
}