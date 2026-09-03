using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
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

        private readonly record struct ScalarChanged(bool On);

        private readonly record struct DemandChanged(bool Valid, DemandScope Scope, double ActivePowerW);

        private readonly record struct SetScalar(bool Value);

        private enum DemandScope
        {
            Total,

            PerPhase,
        }
    }
}