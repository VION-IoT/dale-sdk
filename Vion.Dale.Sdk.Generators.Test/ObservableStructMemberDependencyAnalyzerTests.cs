using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ObservableStructMemberDependencyAnalyzerTests
    {
        // --- The trap: computed observable property reads a member of a struct observable property → DALE031 ---

        [TestMethod]
        public async Task ExpressionBodied_ReadsStructMember_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public double Total => {|#0:Plan.OffGrid|} + Plan.Load;
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked)
                                           .WithLocation(0)
                                           .WithArguments("Total", "Plan", "OffGrid");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task BlockBodiedGetter_ReadsStructMember_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceProperty]
    public double Total
    {
        get { return {|#0:Plan.Load|}; }
    }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked).WithLocation(0).WithArguments("Total", "Plan", "Load");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task SystemStructMemberRead_ReportsDiagnostic()
        {
            // Even deeply-immutable System structs (DateTime/TimeSpan) drop member reads — verified against the
            // Metalama-weaved code: the `When` setter re-fires only "When", not a computed property reading
            // `When.Hour`. So this is a real trap, not a false positive — it must be flagged like any other struct.
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public DateTime When { get; set; }

    [ServiceMeasuringPoint] public int CurrentHour => {|#0:When.Hour|};
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked)
                                           .WithLocation(0)
                                           .WithArguments("CurrentHour", "When", "Hour");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        // --- Exempt: method calls on the struct property ARE tracked by the aspect ---

        [TestMethod]
        public async Task MethodCallOnStructProperty_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load)
{
    public double Sum() => OffGrid + Load;
}

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public double Total => Plan.Sum();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: nameof(struct.member) is a compile-time constant, not a dependency ---

        [TestMethod]
        public async Task NameofStructMember_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public string Label => nameof(Plan.OffGrid);
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: scalar observable dependencies are tracked ---

        [TestMethod]
        public async Task ScalarObservableDependencies_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public double Area { get; set; }
    [ServiceProperty] public double Efficiency { get; set; }

    [ServiceMeasuringPoint] public double Power => Area * Efficiency;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: reading the struct property as a whole (no member access) is tracked ---

        [TestMethod]
        public async Task WholeStructRead_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public Bands PlanCopy => Plan;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: member read of a reference-type observable property is tracked by the aspect ---

        [TestMethod]
        public async Task ReferenceTypeMemberRead_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class Settings { public double Threshold { get; set; } }

public class MyBlock
{
    [ServiceProperty] public Settings Config { get; set; }

    [ServiceMeasuringPoint] public double Threshold => Config.Threshold;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- The trap, third root: an UNMARKED struct property. Asserted exempt until VION-81; the
        //     probe showed the shape is broken exactly like the marked one, because the aspect weaves
        //     every auto-property of the type, not only the [ServiceProperty] ones. ---

        [TestMethod]
        public async Task NonObservableStructProperty_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public double Total => {|#0:Plan.OffGrid|};
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked)
                                           .WithLocation(0)
                                           .WithArguments("Total", "Plan", "OffGrid");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        // --- Exempt: the computed property is not observable, so staleness doesn't matter ---

        [TestMethod]
        public async Task NonObservableComputedProperty_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    public double Total => Plan.OffGrid;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: auto-property has no getter body to derive from ---

        [TestMethod]
        public async Task AutoProperty_NoDiagnostic()
        {
            var source = @"
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- The trap, second root: a private FIELD (VION-81). The aspect tracks the field itself as
        //     a dependency root, but drops the member read off its struct value. ---

        [TestMethod]
        public async Task FieldRootStructMemberRead_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct MeterReading(double ActivePowerTotalKw);

public class MyBlock
{
    private MeterReading _stored;

    [ServiceProperty] public double ActivePowerTotalKw => {|#0:_stored.ActivePowerTotalKw|};
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked)
                                           .WithLocation(0)
                                           .WithArguments("ActivePowerTotalKw", "_stored", "ActivePowerTotalKw");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NullableFieldRootStructMemberRead_ReportsDiagnostic()
        {
            // The shape exactly as filed. `_stored?.X` is a ConditionalAccessExpression, not a
            // MemberAccessExpression, so it needs its own syntax path.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct MeterReading(double ActivePowerTotalKw);

public class MyBlock
{
    private MeterReading? _stored;

    [ServiceProperty] public double? ActivePowerTotalKw => {|#0:_stored?.ActivePowerTotalKw|};
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE031_ObservableStructMemberDependencyNotTracked)
                                           .WithLocation(0)
                                           .WithArguments("ActivePowerTotalKw", "_stored", "ActivePowerTotalKw");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source, expected);
        }

        // --- Exempt: reading the whole field is tracked (probe Q5) ---

        [TestMethod]
        public async Task WholeFieldRead_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct MeterReading(double ActivePowerTotalKw);

public class MyBlock
{
    private MeterReading? _stored;

    [ServiceProperty] public MeterReading? Stored => _stored;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Exempt: a method call through a nullable field root is still a method call (probe Q6) ---

        [TestMethod]
        public async Task MethodCallOnNullableStructField_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct MeterReading(double ActivePowerTotalKw)
{
    public double Scaled() => ActivePowerTotalKw * 2;
}

public class MyBlock
{
    private MeterReading? _stored;

    [ServiceProperty] public double? Scaled => _stored?.Scaled();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        // --- Over-fire guards the widening past the property root introduces ---

        [TestMethod]
        public async Task ReferenceTypeFieldMemberRead_NoDiagnostic()
        {
            // The aspect tracks child objects, so a member read off a reference-typed field is fine.
            var source = @"
using Vion.Dale.Sdk.Core;

public class Settings { public double Threshold { get; set; } }

public class MyBlock
{
    private Settings _config = new Settings();

    [ServiceMeasuringPoint] public double Threshold => _config.Threshold;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        [TestMethod]
        public async Task ReadOnlyStructFieldMemberRead_NoDiagnostic()
        {
            // A readonly field cannot be reassigned after construction, so what it feeds can never go stale.
            // Not on the widening's required list — added because `private readonly TimeSpan _interval;` is a
            // common shape and warning about it would be pure noise in a consumer build.
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    [ServiceMeasuringPoint] public double IntervalSeconds => _interval.TotalSeconds;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        [TestMethod]
        public async Task StaticStructFieldMemberRead_NoDiagnostic()
        {
            // A static is outside the instance's dependency graph entirely — flagging it is noise.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    private static Bands _shared;

    [ServiceMeasuringPoint] public double Total => _shared.Load;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        [TestMethod]
        public async Task BaseClassStructFieldMemberRead_NoDiagnostic()
        {
            // A field declared by a base type belongs to that type's dependency graph, and the base
            // may not even be woven — it can live in an assembly the aspect never touches.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBase
{
    protected Bands _plan;
}

public class MyBlock : MyBase
{
    [ServiceMeasuringPoint] public double Total => _plan.Load;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        [TestMethod]
        public async Task LocalVariableStructMemberRead_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    [ServiceProperty] public Bands Plan { get; set; }

    [ServiceMeasuringPoint]
    public double Total
    {
        get
        {
            var snapshot = Plan;
            return snapshot.Load;
        }
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }

        [TestMethod]
        public async Task StructMemberReadThroughAnotherObject_NoDiagnostic()
        {
            // Not this-relative: this type's aspect does not track another object's struct anyway.
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class Holder { public Bands Plan { get; set; } }

public class MyBlock
{
    private Holder _other = new Holder();

    [ServiceMeasuringPoint] public double Total => _other.Plan.Load;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
        }
    }
}