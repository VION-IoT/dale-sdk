using System;
using Vion.Dale.Sdk.MBus.Test.TestHelpers;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.MBus.TestKit.Test
{
    /// <summary>
    ///     Answering a logic block's pending M-Bus read. Every simulation runs through the contract message path the
    ///     runtime uses, so the frame checks, the decoder, the callbacks and the receipt are the SDK's own code against a
    ///     fixture block — no line, no provider, no meter.
    /// </summary>
    [TestClass]
    public class IMBusExtensionsShould
    {
        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        private SampleLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _context = _sut.InitializeForTest();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.1")]
        public void AnswerPendingReadThroughDecoderStampedFromVirtualClock()
        {
            // Arrange
            var issuedAt = _context.TimeProvider.GetUtcNow().UtcDateTime;
            _sut.Poll();
            _context.TimeProvider.Advance(TimeSpan.FromSeconds(4));

            // Act
            _sut.MBus.SimulateReadResponse(_context, MBusVectors.Em530);
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(50.0, _sut.Frequency);
            Assert.AreEqual(MBusOutcome.Success, _sut.LastReceipt!.Value.Outcome);
            Assert.AreEqual(issuedAt.AddSeconds(4), _sut.LastReceipt.Value.ReceivedAt);
            Assert.AreEqual(TimeSpan.FromSeconds(4), _sut.LastReceipt.Value.RoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.1")]
        public void CheckSimulatedTelegramsAsProductionDoes()
        {
            // Arrange
            _sut.Poll();

            // Act
            _sut.MBus.SimulateReadResponse(_context, MBusVectors.Em530Telegram5.Replace("86 16", "87 16"));
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(MBusOutcome.ProtocolError, _sut.LastReceipt!.Value.Outcome);
            Assert.IsNull(_sut.Frequency);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.2")]
        [DataRow(MBusOutcome.NoResponse)]
        [DataRow(MBusOutcome.Timeout)]
        [DataRow(MBusOutcome.Expired)]
        public void CompletePendingReadWithNamedOutcome(MBusOutcome outcome)
        {
            // Arrange
            _sut.Poll();

            // Act
            _sut.MBus.SimulateReadError(_context, outcome);
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(outcome, ((MBusException)_sut.LastError!).Outcome);
            Assert.AreEqual(outcome, _sut.LastReceipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.2")]
        public void RefuseSuccessNamingResponseSimulation()
        {
            // Arrange
            _sut.Poll();

            // Act / Assert
            var exception = Assert.ThrowsExactly<ArgumentException>(() => _sut.MBus.SimulateReadError(_context, MBusOutcome.Success));
            StringAssert.Contains(exception.Message, nameof(IMBusExtensions.SimulateReadResponse));
            Assert.IsNull(_sut.LastReceipt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.4")]
        public void RefuseSimulationBeforeAnyRead()
        {
            // Arrange

            // Act / Assert
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => _sut.MBus.SimulateReadResponse(_context, MBusVectors.Em530Telegram5));
            StringAssert.Contains(exception.Message, "call Read before simulating its answer");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.4")]
        public void RefuseSecondSimulationOfAnsweredRead()
        {
            // Arrange
            _sut.Poll();
            _sut.MBus.SimulateReadError(_context, MBusOutcome.NoResponse);

            // Act / Assert
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => _sut.MBus.SimulateReadError(_context, MBusOutcome.NoResponse));
            StringAssert.Contains(exception.Message, "call Read before simulating its answer");
        }
    }
}