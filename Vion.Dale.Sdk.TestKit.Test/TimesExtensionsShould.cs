using System;
using System.Globalization;
using Moq;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The occurrence-count check every verification helper in every kit ends in. The rows are the
    ///     forms the mocking library expresses; the assertion is whether the count is accepted.
    /// </summary>
    [TestClass]
    public class TimesExtensionsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.2")]
        [DataRow(1, 1, DisplayName = "Once matches one")]
        [DataRow(2, 0, DisplayName = "Never matches none")]
        [DataRow(3, 4, DisplayName = "AtLeastOnce matches four")]
        [DataRow(4, 0, DisplayName = "AtMostOnce matches none")]
        [DataRow(5, 1, DisplayName = "AtMostOnce matches one")]
        [DataRow(6, 2, DisplayName = "Exactly(2) matches two")]
        [DataRow(7, 3, DisplayName = "AtLeast(2) matches three")]
        [DataRow(8, 1, DisplayName = "AtMost(2) matches one")]
        [DataRow(9, 1, DisplayName = "Between(1,3) inclusive matches one")]
        [DataRow(10, 2, DisplayName = "Between(1,3) inclusive matches two")]
        [DataRow(11, 3, DisplayName = "Between(1,3) inclusive matches three")]
        [DataRow(12, 2, DisplayName = "Between(1,3) exclusive matches two")]
        public void AcceptCountInsideEveryOccurrenceForm(int form, int actualCount)
        {
            // Arrange
            var times = Form(form);

            // Act / Assert — an accepted count is a call that returns
            times.AssertCount(actualCount, "probe");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.2")]
        [DataRow(1, 2, DisplayName = "Once rejects two")]
        [DataRow(2, 1, DisplayName = "Never rejects one")]
        [DataRow(3, 0, DisplayName = "AtLeastOnce rejects none")]
        [DataRow(4, 2, DisplayName = "AtMostOnce rejects two")]
        [DataRow(6, 3, DisplayName = "Exactly(2) rejects three")]
        [DataRow(7, 1, DisplayName = "AtLeast(2) rejects one")]
        [DataRow(8, 3, DisplayName = "AtMost(2) rejects three")]
        [DataRow(9, 0, DisplayName = "Between(1,3) inclusive rejects none")]
        [DataRow(11, 4, DisplayName = "Between(1,3) inclusive rejects four")]
        [DataRow(12, 1, DisplayName = "Between(1,3) exclusive rejects one")]
        public void RejectCountOutsideEveryOccurrenceForm(int form, int actualCount)
        {
            // Arrange
            var times = Form(form);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => times.AssertCount(actualCount, "probe"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.3")]
        public void NameVerificationExpectationAndActualCountWhenCountRejected()
        {
            // Act
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => Times.Exactly(2).AssertCount(5, "Power verification failed"));

            // Assert — the whole rendered text, so a substring cannot stand in for a different count
            Assert.AreEqual("Power verification failed: Expected Exactly(2) but found 5.", thrown.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.3")]
        public void RenderRejectedCountInvariantlyUnderAnyCulture()
        {
            // Arrange
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("de-CH");

            try
            {
                // Act
                var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => Times.Never().AssertCount(1234567, "probe"));

                // Assert
                Assert.AreEqual("probe: Expected Never but found 1234567.", thrown.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        // Object-typed rows are not available to [DataRow], and Times is a struct the runner cannot render,
        // so the rows select a form by number. The mapping lives here rather than in a [DynamicData] member
        // because both test methods index it and the numbers appear in their DisplayNames.
        private static Times Form(int form)
        {
            return form switch
                   {
                       1 => Times.Once(),
                       2 => Times.Never(),
                       3 => Times.AtLeastOnce(),
                       4 or 5 => Times.AtMostOnce(),
                       6 => Times.Exactly(2),
                       7 => Times.AtLeast(2),
                       8 => Times.AtMost(2),
                       9 or 10 or 11 => Times.Between(1, 3, Moq.Range.Inclusive),
                       12 => Times.Between(1, 3, Moq.Range.Exclusive),
                       _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
                   };
        }
    }
}
