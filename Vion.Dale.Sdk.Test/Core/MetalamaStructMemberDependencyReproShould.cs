using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Runtime watchdog for the Metalama [Observable]-aspect gap where a computed observable property
    ///     deriving from a MEMBER of a struct value is woven without a dependency on that value, so it never
    ///     re-publishes (VION-81). Publication is gated entirely on <c>PropertyChanged</c> matching the member
    ///     name (<c>ServiceBinder.cs:269</c> for properties, <c>:298</c> for measuring points), so
    ///     <c>INotifyPropertyChanged</c> on the instance is the layer that decides the bug.
    ///     <para>
    ///         The companion <c>DALE031</c> analyzer prevents the antipattern at edit time; this test exists so
    ///         a future Metalama upgrade can be verified end-to-end — remove the <c>[Ignore]</c>s and watch them
    ///         go green when the upstream aspect learns to track struct-member reads. Verified still broken in
    ///         Metalama.Patterns.Observability <b>2026.1.18 and 2026.1.25</b>, with no LAMA51xx warning emitted
    ///         for any of these shapes at build time.
    ///     </para>
    ///     <para>
    ///         Structure follows <see cref="MetalamaFieldKeywordReproShould" />: every ignored probe has a
    ///         positive control asserted on the same instance in the same run, so a green control with a red
    ///         probe is the discriminating result. If a control ever fails, the harness is wrong — not Metalama.
    ///     </para>
    /// </summary>
    [TestClass]
    public class MetalamaStructMemberDependencyReproShould
    {
        [TestMethod]
        public void ObservableAutoProperty_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Control = 42;

            CollectionAssert.Contains(raised, "Control", "Plain observable auto-property must raise PropertyChanged (positive control for the watchdogs below).");
        }

        [TestMethod]
        public void ComputedFromScalarProperty_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Control = 42;

            CollectionAssert.Contains(raised, "ControlDoubled", "A computed getter deriving from a scalar observable property is tracked (the shape DALE031 points authors at).");
        }

        [TestMethod]
        public void ComputedFromWholeStructField_RaisesPropertyChanged()
        {
            // Establishes that a private field IS a tracked dependency root — which is why the failures below
            // are about the struct-member read and not about fields.
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.StoreReading(new MeterReading(33.5));

            CollectionAssert.Contains(raised, "Stored", "Reading the whole field is tracked. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        public void ComputedFromScalarField_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.StoreScalar(9);

            CollectionAssert.Contains(raised, "ScalarFromField", "Reading a scalar field is tracked. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        public void MethodCallOnStructProperty_RaisesPropertyChanged()
        {
            // DALE031's documented exemption, verified rather than asserted: method calls ARE tracked.
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Plan = new Bands(7.5);

            CollectionAssert.Contains(raised, "PlanSum", "A method call on the struct property is tracked. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        [Ignore("Documents the Metalama [Observable] struct-member-read gap as filed on VION-81. Verified still broken in Metalama.Patterns.Observability 2026.1.18 and 2026.1.25. DALE031 guards against the antipattern in user code; re-enable this test after a future Metalama upgrade to verify the upstream fix.")]
        public void ComputedFromNullableStructField_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Control = 1;
            CollectionAssert.Contains(raised, "Control", "Positive control must fire in this same run.");
            raised.Clear();

            // Currently failing: only "Stored" is raised — the sibling property that reads the whole field.
            // The member read off the struct value creates no dependency, so this member is permanently stale.
            sut.StoreReading(new MeterReading(11.5));

            CollectionAssert.Contains(raised,
                                      "ActivePowerTotalKw",
                                      "double? ActivePowerTotalKw => _stored?.ActivePowerTotalKw must re-publish. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        [Ignore("Same gap as ComputedFromNullableStructField_RaisesPropertyChanged, without the null-conditional operator — establishing that '?.' is not the operative factor. Verified still broken in 2026.1.18 and 2026.1.25.")]
        public void ComputedFromNonNullableStructField_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Control = 1;
            CollectionAssert.Contains(raised, "Control", "Positive control must fire in this same run.");
            raised.Clear();

            sut.StorePlainReading(new MeterReading(22.5));

            CollectionAssert.Contains(raised,
                                      "PlainActivePowerTotalKw",
                                      "double PlainActivePowerTotalKw => _plainStored.ActivePowerTotalKw must re-publish. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        [Ignore("The same gap rooted in an observable struct property rather than a field — the original DALE031 shape. Verified still broken in 2026.1.18 and 2026.1.25.")]
        public void ComputedFromObservableStructPropertyMember_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.Plan = new Bands(7.5);

            CollectionAssert.Contains(raised, "Plan", "Positive control: assigning the struct property itself must raise. Raised: " + string.Join(", ", raised));
            CollectionAssert.Contains(raised, "Total", "double Total => Plan.Load must re-publish. Raised: " + string.Join(", ", raised));
        }

        [TestMethod]
        [Ignore("The same gap rooted in an UNMARKED struct property. The aspect weaves every auto-property of the type, not only the [ServiceProperty] ones, so this shape is broken identically — which is why DALE031 no longer exempts it. Verified still broken in 2026.1.18 and 2026.1.25.")]
        public void ComputedFromPlainStructPropertyMember_RaisesPropertyChanged()
        {
            var sut = new StructMemberDependencyRepro();
            var raised = Subscribe(sut);

            sut.PlainPlan = new Bands(4.5);

            CollectionAssert.Contains(raised,
                                      "PlainPlanCopy",
                                      "Positive control at the same root: a whole-value read of the unmarked property must raise. Raised: " + string.Join(", ", raised));
            CollectionAssert.Contains(raised, "PlainPlanTotal", "double PlainPlanTotal => PlainPlan.Load must re-publish. Raised: " + string.Join(", ", raised));
        }

        private static List<string> Subscribe(StructMemberDependencyRepro sut)
        {
            var raised = new List<string>();
            ((INotifyPropertyChanged)sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "<null>");
            return raised;
        }

        public readonly record struct MeterReading(double ActivePowerTotalKw);

        public readonly record struct Bands(double Load)
        {
            public double Sum()
            {
                return Load;
            }
        }

#pragma warning disable DALE031 // Intentional antipattern under test — see the [Ignore] notes above.
        private sealed class StructMemberDependencyRepro : LogicBlockBase
        {
            private MeterReading _plainStored;

            [ServiceProperty]
            public int Control { get; set; }

            [ServiceProperty]
            public int ControlDoubled
            {
                get => Control * 2;
            }

            [ServiceProperty]
            public MeterReading? Stored { get; private set; }

            [ServiceProperty]
            public int ScalarFromField { get; private set; }

            [ServiceProperty]
            public double? ActivePowerTotalKw
            {
                get => Stored?.ActivePowerTotalKw;
            }

            [ServiceProperty]
            public double PlainActivePowerTotalKw
            {
                get => _plainStored.ActivePowerTotalKw;
            }

            [ServiceProperty]
            public Bands Plan { get; set; }

            [ServiceProperty]
            public double PlanSum
            {
                get => Plan.Sum();
            }

            [ServiceMeasuringPoint]
            public double Total
            {
                get => Plan.Load;
            }

            public Bands PlainPlan { get; set; }

            [ServiceProperty]
            public Bands PlainPlanCopy
            {
                get => PlainPlan;
            }

            [ServiceMeasuringPoint]
            public double PlainPlanTotal
            {
                get => PlainPlan.Load;
            }

            public StructMemberDependencyRepro() : base(new Mock<ILogger>().Object)
            {
            }

            public void StoreReading(MeterReading reading)
            {
                Stored = reading;
            }

            public void StorePlainReading(MeterReading reading)
            {
                _plainStored = reading;
            }

            public void StoreScalar(int value)
            {
                ScalarFromField = value;
            }

            protected override void Ready()
            {
            }
        }
#pragma warning restore DALE031
    }
}