using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands.Add;

namespace Vion.Dale.Cli.Test.Commands.Add
{
    [TestClass]
    public class PresentationSnippetShould
    {
        private CultureInfo _originalCulture = null!;

        [TestInitialize]
        public void Setup()
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
        }

        [TestCleanup]
        public void Cleanup()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.8")]
        [DataRow("sv-SE")]
        [DataRow("de-DE")]
        [DataRow("fi-FI")]
        [DataRow("en-US")]
        public void EmitNegativeDecimalsInvariantly(string culture)
        {
            // Arrange
            // On sv-SE, fi-FI and their neighbours the negative sign is U+2212, which does not compile.
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);

            // Act
            var attribute = PresentationSnippet.Build(null, null, -2, null);

            // Assert
            Assert.AreEqual("[Presentation(Decimals = -2)]", attribute);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.9")]
        [DataRow(1.0, true)]
        [DataRow(0.5, true)]
        [DataRow(0.0, false)]
        [DataRow(-1.0, false)]
        [DataRow(double.NaN, false)]
        [DataRow(double.PositiveInfinity, false)]
        [DataRow(double.NegativeInfinity, false)]
        public void AcceptOnlyPositiveFiniteTimerInterval(double interval, bool expectedAccepted)
        {
            // Arrange / Act
            var accepted = AddTimerCommand.IsValidInterval(interval);

            // Assert
            Assert.AreEqual(expectedAccepted, accepted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.8")]
        [DataRow("sv-SE")]
        [DataRow("de-DE")]
        public void EmitTimerIntervalInvariantly(string culture)
        {
            // Arrange
            // On a comma-decimal culture 0.5 renders as "0,5", which is two attribute arguments.
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);

            // Act
            var snippet = AddTimerCommand.BuildTimerSnippet("Poll", 0.5);

            // Assert
            StringAssert.Contains(snippet, "[Timer(0.5)]");
        }
    }
}