using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE046 — a <c>[ScenarioWire]</c> type argument must be a value struct the scenario codec can build
    ///     from a JSON value. The rows below cover the shapes shipped in this repo (which must stay silent) and
    ///     the delegate-bearing request/response shape the rule exists to catch.
    /// </summary>
    [TestClass]
    public class ScenarioWireTypeAnalyzerTests
    {
        // ── Shapes that must stay silent ──

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task StaySilentOnSingleFieldScalarWireStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

public readonly record struct DigitalInputChanged(bool Value);

[ScenarioWire(Inbound = typeof(DigitalInputChanged))]
public class DigitalInputHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source);
        }

        // The SmokeHost's richest shape: multi-field, an enum, a NULLABLE nested struct and a timestamp.
        // The codec descends nested readonly record structs, so this must not be judged by the flat-struct rule.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task StaySilentOnRepresentableCompositeWireStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Abstractions;

public enum DemandScope { Total, PerPhase }
public readonly record struct SetpointLimits(double ActivePowerW, double ReactivePowerVar);
public readonly record struct SetGridSetpoint(bool Enforced, DemandScope Scope, SetpointLimits? Limits, DateTimeOffset IssuedAt);

[ScenarioWire(Outbound = typeof(SetGridSetpoint))]
public class GridSetpointHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task StaySilentWhenBothDirectionsAreRepresentable()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

public readonly record struct SetDigitalOutput(bool Value);
public readonly record struct DigitalOutputChanged(bool Value);

[ScenarioWire(Inbound = typeof(SetDigitalOutput), Outbound = typeof(DigitalOutputChanged))]
public class DigitalOutputProviderHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source);
        }

        // A payload-free struct round-trips as an empty object; there is nothing unrepresentable about it.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task StaySilentOnFieldlessWireStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

public readonly record struct TogglePressed;

[ScenarioWire(Inbound = typeof(TogglePressed))]
public class TogglingHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task StaySilentOnHandlerWithoutWireAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
public class Callback { }

public readonly record struct ReadRequested(int CorrelationId, Callback Respond);

public class ModbusRtuHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source);
        }

        // ── Shapes DALE046 must catch ──

        // The rule's reason to exist: a request/response wire struct holding a pending-operation callback.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task ReportDelegateMemberInWireStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Abstractions;

public readonly record struct ReadRequested(int CorrelationId, Action<byte[]> Respond);

[{|#0:ScenarioWire(Inbound = typeof(ReadRequested))|}]
public class ModbusRtuHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task ReportDelegateMemberNestedInWireStruct()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Abstractions;

public readonly record struct Pending(int CorrelationId, Func<bool> Complete);
public readonly record struct WriteRequested(bool Valid, Pending Operation);

[{|#0:ScenarioWire(Outbound = typeof(WriteRequested))|}]
public class ModbusRtuHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task ReportClassWireType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

public class DemandReceived { public bool Valid { get; set; } }

[{|#0:ScenarioWire(Inbound = typeof(DemandReceived))|}]
public class DemandHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, Error().WithLocation(0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task ReportReferenceTypedWireMember()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

public class Payload { }
public readonly record struct FrameReceived(int UnitId, Payload Body);

[{|#0:ScenarioWire(Inbound = typeof(FrameReceived))|}]
public class FrameHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, Error().WithLocation(0));
        }

        // Both directions are judged, and each reports on its own — the outbound half is not a blind spot.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.1")]
        public async Task ReportEachUnrepresentableDirection()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Abstractions;

public readonly record struct In(Action Callback);
public readonly record struct Out(Action Callback);

[{|#0:ScenarioWire(Inbound = typeof(In), Outbound = typeof(Out))|}]
public class BothHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, Error().WithLocation(0), Error().WithLocation(0));
        }

        // ── The unresolved-type case ──

        // A wire type the compilation cannot resolve is already a compile error; DALE046 must stay quiet
        // rather than pile a second, misleading message onto it.
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-017.2")]
        public async Task StaySilentOnUnresolvedWireType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Abstractions;

[ScenarioWire(Inbound = typeof({|#0:MissingChanged|}))]
public class MissingHandler { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ScenarioWireTypeAnalyzer>(source, DiagnosticResult.CompilerError("CS0246").WithLocation(0).WithArguments("MissingChanged"));
        }

        private static DiagnosticResult Error()
        {
            return new DiagnosticResult(DaleDiagnostics.DALE046_ScenarioWireTypeNotRepresentable.Id, DiagnosticSeverity.Error);
        }
    }
}