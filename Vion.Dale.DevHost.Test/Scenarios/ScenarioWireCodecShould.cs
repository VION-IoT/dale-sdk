using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The codec behind a handler's declared wire structs: the exact closed contract message a drive
    ///     builds, the scalar unwrap a single-field struct round-trips through, and the addressable leaves a
    ///     <c>serviceProviderExpect</c> field selector may name.
    /// </summary>
    [TestClass]
    public class ScenarioWireCodecShould
    {
        private static readonly LogicBlockContractId ContractId = new(new LogicBlockId("lb1"), "c1");

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.3")]
        [TestProperty("spec", "AC-SCEN-008.2")]
        public void DriveInputFromScalarValueIntoExactClosedMessage()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(ScalarInputHandlerStub))!;

            // Assert
            Assert.IsTrue(codec.CanDrive);
            Assert.IsFalse(codec.CanAssert);

            var message = codec.MakeInbound(ContractId, Json("true"));

            Assert.IsInstanceOfType<ContractMessage<ScalarChanged>>(message);
            Assert.IsTrue(((ContractMessage<ScalarChanged>)message).Data.On);
            Assert.AreEqual(ContractId, message.LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.4")]
        public void DriveInputFromStructValueIncludingEnum()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(DemandInputHandlerStub))!;

            var message = codec.MakeInbound(ContractId, Json("""{ "valid": true, "scope": "PerPhase", "activePowerW": 1500 }"""));

            var demand = ((ContractMessage<DemandChanged>)message).Data;

            // Assert
            Assert.IsTrue(demand.Valid);
            Assert.AreEqual(DemandScope.PerPhase, demand.Scope);
            Assert.AreEqual(1500d, demand.ActivePowerW);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.3")]
        public void AssertOutputCommandBackToItsScalarValue()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(ScalarOutputHandlerStub))!;

            // Assert
            Assert.IsTrue(codec.CanAssert);
            Assert.IsFalse(codec.CanDrive);

            var value = codec.ReadCommand(new ContractMessage<SetScalar>(ContractId, new SetScalar(true)));

            Assert.AreEqual(JsonValueKind.True, value.ValueKind);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.1")]
        public void YieldNoCodecForUndeclaredHandler()
        {
            // Arrange / Act
            // Assert
            Assert.IsNull(ScenarioWireCodec.ForHandler(typeof(UndecoratedHandlerStub)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.11")]
        public void ReadWireStructsDeclaredOnBaseHandler()
        {
            // Arrange / Act — a handler hierarchy sharing one wire pair is the natural shape, so an
            // inherited declaration is read. Stated because a later Inherited = false would be a silent
            // change to which contracts a scenario can address.
            var inherited = ScenarioWireCodec.ForHandler(typeof(DerivedInputHandlerStub));

            // Assert
            Assert.IsNotNull(inherited);
            Assert.IsTrue(inherited!.CanDrive);
            CollectionAssert.AreEqual(new[] { typeof(ScalarChanged) }, inherited.DeclaredInbound.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.5")]
        public void ReportAddressableLeavesInDeclarationOrder()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(NestedOutputHandlerStub))!;

            // Assert — declaration order, dotted through the nested wire struct, in the camelCase wire keys
            // a scenario's `field` selector names.
            CollectionAssert.AreEqual(new[] { "enforced", "limits.activePowerW", "limits.reactivePowerVar", "issuedAt" }, codec.OutputFieldPaths!.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.5")]
        public void ExcludeCollectionFieldsFromAddressableLeaves()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(CollectionOutputHandlerStub))!;

            // Assert — an array is not comparable in v1, so offering it as a field would offer a selector the
            // read can never satisfy. A DateTimeOffset has its own converter and stays one scalar leaf.
            CollectionAssert.AreEqual(new[] { "label", "issuedAt" }, codec.OutputFieldPaths!.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.3")]
        public void ReportNoAddressableLeafForSingleFieldStruct()
        {
            // Arrange / Act
            var codec = ScenarioWireCodec.ForHandler(typeof(ScalarOutputHandlerStub))!;

            // Assert — such an output round-trips as its bare scalar, so it is asserted with no field at all.
            Assert.IsEmpty(codec.OutputFieldPaths!);
            Assert.IsNull(codec.InputFieldPaths);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-008.6")]
        public void DescribeWireStructThatDeclaresNoConstructor()
        {
            // Arrange / Act — leaves are enumerated for every discovered handler when a configuration is
            // built, not only when a block writes, so an init-only struct must not throw there.
            var codec = ScenarioWireCodec.ForHandler(typeof(InitOnlyOutputHandlerStub))!;

            // Assert
            Assert.IsEmpty(codec.OutputFieldPaths!);
        }

        private static JsonElement Json(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        [ScenarioWire(Inbound = typeof(ScalarChanged))]
        private sealed class ScalarInputHandlerStub
        {
        }

        [ScenarioWire(Inbound = typeof(DemandChanged))]
        private sealed class DemandInputHandlerStub
        {
        }

        [ScenarioWire(Outbound = typeof(SetScalar))]
        private sealed class ScalarOutputHandlerStub
        {
        }

        private sealed class UndecoratedHandlerStub
        {
        }

        // The base carries the declaration; the derived handler inherits it.
        [ScenarioWire(Inbound = typeof(ScalarChanged))]
        private class BaseInputHandlerStub
        {
        }

        private sealed class DerivedInputHandlerStub : BaseInputHandlerStub
        {
        }

        [ScenarioWire(Outbound = typeof(SetNestedLimits))]
        private sealed class NestedOutputHandlerStub
        {
        }

        // Deliberately illegal: DALE046 refuses a collection-typed wire member at compile time, which is the
        // analyzer doing its job. This fixture exists to prove the codec ALSO excludes such a member from the
        // addressable leaves, so a handler that predates the analyzer offers no field a read cannot satisfy.
#pragma warning disable DALE046
        [ScenarioWire(Outbound = typeof(SetWithCollection))]
        private sealed class CollectionOutputHandlerStub
        {
        }
#pragma warning restore DALE046

        [ScenarioWire(Outbound = typeof(SetInitOnly))]
        private sealed class InitOnlyOutputHandlerStub
        {
        }

        private readonly record struct ScalarChanged(bool On);

        private readonly record struct DemandChanged(bool Valid, DemandScope Scope, double ActivePowerW);

        private readonly record struct SetScalar(bool Value);

        private readonly record struct Limits(double ActivePowerW, double ReactivePowerVar);

        private readonly record struct SetNestedLimits(bool Enforced, Limits Limits, System.DateTimeOffset IssuedAt);

        private readonly record struct SetWithCollection(string Label, IReadOnlyList<double> Steps, System.DateTimeOffset IssuedAt);

        // No constructor at all: a struct with only init-only properties serializes as an object like any
        // other, and describing it must not throw.
        private readonly record struct SetInitOnly
        {
            public bool Enforced { get; init; }
        }

        private enum DemandScope
        {
            Total,

            PerPhase,
        }
    }
}