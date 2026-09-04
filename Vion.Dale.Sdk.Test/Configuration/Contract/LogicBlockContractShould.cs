using System;
using System.Linq;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Contract
{
    /// <summary>
    ///     What a contract instance guarantees the block that holds it: the identity it carries, the handler
    ///     it addresses, and the two different answers it gives when there is nowhere to send. The contract is
    ///     driven directly against a recording actor context, which is where its sends are observable.
    /// </summary>
    [TestClass]
    public class LogicBlockContractShould
    {
        private const string Identifier = "probe";

        private readonly LifecycleHarness.RecordingActorContext _context = new();

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.1")]
        public void CarryEndpointIdentifierItWasConstructedWith()
        {
            // Arrange / Act
            var contract = new BindProbeContract(Identifier, _context);

            // Assert
            Assert.AreEqual(Identifier, contract.Identifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.2")]
        public void TakeOwningBlockHalfOfIdentityFromRuntime()
        {
            // Arrange
            var contract = new BindProbeContract(Identifier, _context);
            contract.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId("block-1"), Identifier));
            contract.SetLinkedContractHandler(new LifecycleHarness.NamedReference(nameof(BindProbeHandler)));

            // Act
            contract.Poke(3);

            // Assert
            var message = _context.Sent.Select(sent => sent.Message).OfType<ContractMessage<PokeBindProbe>>().Single();
            Assert.AreEqual(new LogicBlockContractId(new LogicBlockId("block-1"), Identifier), message.LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.3")]
        public void RefuseIdentityNamingDifferentContract()
        {
            // Arrange
            var contract = new BindProbeContract(Identifier, _context);

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => contract.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId("block-1"), "elsewhere")));
            StringAssert.Contains(exception.Message, Identifier);
            StringAssert.Contains(exception.Message, "elsewhere");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.4")]
        public void NameHandlerActorItExchangesMessagesWith()
        {
            // Arrange / Act
            var contract = new BindProbeContract(Identifier, _context);

            // Assert
            Assert.AreEqual(nameof(BindProbeHandler), contract.ContractHandlerActorName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.4")]
        public void AddressHandlerReferenceRuntimeLinked()
        {
            // Arrange
            var contract = new BindProbeContract(Identifier, _context);
            contract.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId("block-1"), Identifier));
            contract.SetLinkedContractHandler(new LifecycleHarness.NamedReference(nameof(BindProbeHandler)));

            // Act
            contract.Poke(3);

            // Assert
            Assert.AreEqual(nameof(BindProbeHandler), _context.Sent.Single().Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.5")]
        public void DropMessageOfContractWithNoMapping()
        {
            // Arrange
            var contract = new BindProbeContract(Identifier, _context);
            contract.SetLinkedContractHandler(new LifecycleHarness.NamedReference(nameof(BindProbeHandler)));

            // Act
            contract.Poke(3);

            // Assert
            Assert.IsEmpty(_context.Sent);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-009.6")]
        public void RefuseSendOfMappedContractBeforeHandlerLink()
        {
            // Arrange
            var contract = new BindProbeContract(Identifier, _context);
            contract.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId("block-1"), Identifier));

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => contract.Poke(3));
            StringAssert.Contains(exception.Message, Identifier);
            StringAssert.Contains(exception.Message, nameof(BindProbeHandler));
        }
    }
}