using System;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     The <c>Verify…</c> family's default tolerance means exact equality, and an explicit tolerance is a
    ///     real bound in both directions. The default used to be unsatisfiable — the comparison was
    ///     <c>Math.Abs(diff) &lt; tolerance</c> against a tolerance of 0 — so every call that passed a value
    ///     and no tolerance failed however exact the value was.
    /// </summary>
    [TestClass]
    public class ToleranceDefaultsShould
    {
        /// <summary>Which member of the family a row exercises; each is arranged by <see cref="Drive" />.</summary>
        public enum Face
        {
            /// <summary><c>VerifyAnalogOutputSet</c>.</summary>
            OutputSet,

            /// <summary><c>VerifyAnalogOutputConfirmed</c>.</summary>
            OutputConfirmed,

            /// <summary><c>VerifyAnalogInputDriven</c>.</summary>
            InputDriven,
        }

        [TestMethod]
        [DataRow(Face.OutputSet)]
        [DataRow(Face.OutputConfirmed)]
        [DataRow(Face.InputDriven)]
        public void Match_an_exact_value_at_the_default_tolerance(Face face)
        {
            var fixture = Drive(face);

            // The call shape a consumer writes when the value is exact: no tolerance argument at all.
            fixture.AtDefaultTolerance(fixture.Expected);
        }

        [TestMethod]
        [DataRow(Face.OutputSet)]
        [DataRow(Face.OutputConfirmed)]
        [DataRow(Face.InputDriven)]
        public void Reject_a_near_miss_at_the_default_tolerance(Face face)
        {
            var fixture = Drive(face);

            Assert.ThrowsExactly<TestKitVerificationException>(() => fixture.AtDefaultTolerance(fixture.Expected + 0.5));
        }

        [TestMethod]
        [DataRow(Face.OutputSet)]
        [DataRow(Face.OutputConfirmed)]
        [DataRow(Face.InputDriven)]
        public void Admit_a_near_miss_within_an_explicit_tolerance(Face face)
        {
            var fixture = Drive(face);

            fixture.WithTolerance(fixture.Expected + 0.4, 0.5);
        }

        // The other half of the bound: a tolerance admits what is inside it and nothing else, so the row
        // above cannot pass on a comparison that accepts everything.
        [TestMethod]
        [DataRow(Face.OutputSet)]
        [DataRow(Face.OutputConfirmed)]
        [DataRow(Face.InputDriven)]
        public void Reject_a_miss_outside_an_explicit_tolerance(Face face)
        {
            var fixture = Drive(face);

            Assert.ThrowsExactly<TestKitVerificationException>(() => fixture.WithTolerance(fixture.Expected + 0.6, 0.5));
        }

        // A tolerance is inclusive: a difference exactly equal to it is inside the bound, matching the
        // scenario runner's `equals` + `tolerance` comparison.
        [TestMethod]
        [DataRow(Face.OutputSet)]
        [DataRow(Face.OutputConfirmed)]
        [DataRow(Face.InputDriven)]
        public void Admit_a_difference_exactly_equal_to_the_tolerance(Face face)
        {
            var fixture = Drive(face);

            fixture.WithTolerance(fixture.Expected + 0.5, 0.5);
        }

        // Arranges the face's fixture, drives one value through it, and returns the two call shapes under
        // test — the expected value is written so it is exactly representable and survives the block's own
        // arithmetic, keeping "exact equality" about the comparison rather than about float error.
        private static Fixture Drive(Face face)
        {
            switch (face)
            {
                case Face.OutputSet:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogInput.RaiseInputChanged(5.0); // the block doubles the input onto the output

                    return new Fixture(10.0,
                                       value => testContext.VerifyAnalogOutputSet(logicBlock.AnalogOutput, value),
                                       (value, tolerance) => testContext.VerifyAnalogOutputSet(logicBlock.AnalogOutput, value, tolerance));
                }
                case Face.OutputConfirmed:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogOutputProvider.RaiseSetReceived(3.3);

                    return new Fixture(3.3,
                                       value => testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, value),
                                       (value, tolerance) => testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, value, tolerance));
                }
                case Face.InputDriven:
                {
                    var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
                    var testContext = logicBlock.InitializeForTest();
                    logicBlock.AnalogOutputProvider.RaiseSetReceived(3.3); // the block drives what it confirmed

                    return new Fixture(3.3,
                                       value => testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, value),
                                       (value, tolerance) => testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, value, tolerance));
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(face), face, null);
            }
        }

        private sealed record Fixture(double Expected, Action<double> AtDefaultTolerance, Action<double, double> WithTolerance);
    }
}