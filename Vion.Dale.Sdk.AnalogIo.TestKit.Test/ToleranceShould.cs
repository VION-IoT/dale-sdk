using System;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     The analog comparison, which the digital kit has no counterpart for: a real number has near
    ///     misses and a truth value does not. The rows of every rule are the three verify helpers, because
    ///     they are three call sites of one comparison and a fix applied to one alone would pass a third
    ///     of each rule.
    /// </summary>
    [TestClass]
    public class ToleranceShould
    {
        /// <summary>Which helper of the family a row exercises; each is arranged by <see cref="Drive" />.</summary>
        public enum Helper
        {
            /// <summary><c>VerifyAnalogOutputSet</c>.</summary>
            OutputSet,

            /// <summary><c>VerifyAnalogOutputConfirmed</c>.</summary>
            OutputConfirmed,

            /// <summary><c>VerifyAnalogInputDriven</c>.</summary>
            InputDriven,
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void MatchExactValueAtDefaultTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert — the call shape a consumer writes when the value is exact: no tolerance at all
            fixture.AtDefaultTolerance(fixture.Expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void RejectNearMissAtDefaultTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => fixture.AtDefaultTolerance(fixture.Expected + 0.5));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void MatchValueInsideExplicitTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert
            fixture.WithTolerance(fixture.Expected + 0.4, 0.5);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void RejectValueOutsideExplicitTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert — the other half of the bound, without which the row above passes on a
            // comparison that accepts everything
            Assert.ThrowsExactly<TestKitVerificationException>(() => fixture.WithTolerance(fixture.Expected + 0.6, 0.5));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void MatchDifferenceExactlyEqualToTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert — the tolerance is a closed bound
            fixture.WithTolerance(fixture.Expected + 0.5, 0.5);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet, double.NaN)]
        [DataRow(Helper.OutputSet, double.PositiveInfinity)]
        [DataRow(Helper.OutputSet, double.NegativeInfinity)]
        [DataRow(Helper.OutputConfirmed, double.NaN)]
        [DataRow(Helper.OutputConfirmed, double.PositiveInfinity)]
        [DataRow(Helper.OutputConfirmed, double.NegativeInfinity)]
        [DataRow(Helper.InputDriven, double.NaN)]
        [DataRow(Helper.InputDriven, double.PositiveInfinity)]
        [DataRow(Helper.InputDriven, double.NegativeInfinity)]
        public void MatchNonFiniteValueThatIsBitIdenticalToExpectedOne(Helper helper, double written)
        {
            // Arrange — the value contract carries these to the wire unaltered, so a block that writes
            // one has to be assertable; the difference comparison alone is false for every pair here
            var fixture = Drive(helper, written);

            // Act / Assert
            fixture.AtDefaultTolerance(fixture.Expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void MatchSignedZeroAgainstUnsignedZero(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, -0.0);

            // Act / Assert — a signed zero does not survive the wire, and it compares equal either way
            fixture.AtDefaultTolerance(0.0);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.2")]
        [DataRow(Helper.OutputSet)]
        [DataRow(Helper.OutputConfirmed)]
        [DataRow(Helper.InputDriven)]
        public void MatchAnyFiniteValueAtInfiniteTolerance(Helper helper)
        {
            // Arrange
            var fixture = Drive(helper, 3.5);

            // Act / Assert — an infinite tolerance is a legal width, so the refusal below cannot be
            // written as "not finite"
            fixture.WithTolerance(fixture.Expected + 1e300, double.PositiveInfinity);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-007.3")]
        [DataRow(Helper.OutputSet, double.NaN, "NaN")]
        [DataRow(Helper.OutputSet, -1.0, "-1")]
        [DataRow(Helper.OutputConfirmed, double.NaN, "NaN")]
        [DataRow(Helper.OutputConfirmed, -1.0, "-1")]
        [DataRow(Helper.InputDriven, double.NaN, "NaN")]
        [DataRow(Helper.InputDriven, -1.0, "-1")]
        public void RefuseToleranceThatIsNotNumberOfAtLeastZero(Helper helper, double tolerance, string rendered)
        {
            // Arrange — such a tolerance makes the band empty, so it used to reject even an exact value
            // while the message spoke only of counts
            var fixture = Drive(helper, 3.5);

            // Act / Assert
            var thrown = Assert.ThrowsExactly<TestKitVerificationException>(() => fixture.WithTolerance(fixture.Expected, tolerance));
            Assert.AreEqual($"A verification tolerance must be a number of at least zero, but was {rendered}.", thrown.Message);
        }

        // Arranges the helper's fixture, drives one value through it and returns the two call shapes under
        // test. The driven value is written so it survives the block's own arithmetic unrounded, keeping
        // every row about the comparison rather than about float error.
        private static Fixture Drive(Helper helper, double value)
        {
            switch (helper)
            {
                case Helper.OutputSet:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogInput.RaiseInputChanged(value);

                    return new Fixture(value * 2,
                                       expected => testContext.VerifyAnalogOutputSet(logicBlock.AnalogOutput, expected),
                                       (expected, tolerance) => testContext.VerifyAnalogOutputSet(logicBlock.AnalogOutput, expected, tolerance));
                }
                case Helper.OutputConfirmed:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogOutputProvider.RaiseSetReceived(value);

                    return new Fixture(value,
                                       expected => testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, expected),
                                       (expected, tolerance) => testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, expected, tolerance));
                }
                case Helper.InputDriven:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogOutputProvider.RaiseSetReceived(value);

                    return new Fixture(value,
                                       expected => testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, expected),
                                       (expected, tolerance) => testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, expected, tolerance));
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(helper), helper, null);
            }
        }

        private sealed record Fixture(double Expected, Action<double> AtDefaultTolerance, Action<double, double> WithTolerance);
    }
}
