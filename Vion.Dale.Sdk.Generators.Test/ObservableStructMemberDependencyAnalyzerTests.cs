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

        // --- Exempt: the struct property is not observable (no [ServiceProperty]) ---

        [TestMethod]
        public async Task NonObservableStructProperty_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct Bands(double OffGrid, double Load);

public class MyBlock
{
    public Bands Plan { get; set; }

    [ServiceMeasuringPoint] public double Total => Plan.OffGrid;
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ObservableStructMemberDependencyAnalyzer>(source);
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
    }
}