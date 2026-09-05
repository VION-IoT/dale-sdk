using System;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    /// <summary>
    ///     Simulating a Modbus RTU device's answer to a request the block already issued. Every simulation
    ///     runs through the contract message path the runtime uses, so the callback chain, the byte
    ///     conversion and the receipt are real SDK code against a fixture block — no serial port, no
    ///     runtime, no device.
    /// </summary>
    [TestClass]
    public class IModbusRtuExtensionsShould
    {
        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        private SampleLogicBlock _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            // Arrange
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _context = _sut.InitializeForTest();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.1")]
        public void InvokeSuccessCallbackOnSimulatedReadResponse()
        {
            // Arrange
            _sut.ReadVoltages();

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(230.5f, 231.0f, 229.8f), SampleLogicBlock.VoltagesAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.HasCount(3, _sut.LastVoltages);
            Assert.IsNotNull(_sut.LastReadReceipt);
            Assert.AreEqual(ModbusOutcome.Success, _sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(230.5f, _sut.LastVoltages[0], 0.01f);
            Assert.AreEqual(231.0f, _sut.LastVoltages[1], 0.01f);
            Assert.AreEqual(229.8f, _sut.LastVoltages[2], 0.01f);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.3")]
        public void MatchReadResponseByStartingAddressWhenMultipleReadsArePending()
        {
            // Arrange
            _sut.ReadVoltages();
            _sut.ReadCurrents();

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(5.2f, 4.8f, 5.0f), SampleLogicBlock.CurrentsAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.HasCount(3, _sut.LastCurrents);
            Assert.IsEmpty(_sut.LastVoltages);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.2")]
        public void InvokeErrorCallbackOnSimulatedReadError()
        {
            // Arrange
            var expectedError = new TimeoutException("Device not responding");
            _sut.ReadVoltages();

            // Act
            _sut.Modbus.SimulateReadError(_context, expectedError, SampleLogicBlock.VoltagesAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.AreSame(expectedError, _sut.LastError);
            Assert.AreEqual(ModbusOutcome.TransportError, _sut.LastReadReceipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.1")]
        public void InvokeSuccessCallbackOnSimulatedWriteResponse()
        {
            // Arrange
            _sut.WriteSetpoint(42);

            // Act
            _sut.Modbus.SimulateWriteResponse(_context, SampleLogicBlock.SetpointAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(1, _sut.WriteSuccessCount);
            Assert.AreEqual(ModbusOutcome.Success, _sut.LastWriteReceipt!.Value.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.2")]
        public void InvokeErrorCallbackOnSimulatedWriteError()
        {
            // Arrange
            var expectedError = new InvalidOperationException("Write rejected");
            _sut.WriteSetpoint(42);

            // Act
            _sut.Modbus.SimulateWriteError(_context, expectedError, SampleLogicBlock.SetpointAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.AreSame(expectedError, _sut.LastError);
            Assert.AreEqual(0, _sut.WriteSuccessCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.3")]
        public void AnswerSameRequestAgainWhenSimulatedTwice()
        {
            // Arrange — a simulation reads the recording and does not consume it, so the request the
            // block issued once can be answered as often as a test asks
            _sut.ReadVoltages();

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(1f, 2f, 3f), SampleLogicBlock.VoltagesAddress);
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(4f, 5f, 6f), SampleLogicBlock.VoltagesAddress);
            _context.FlushPendingActions();

            // Assert — the second answer is the one the block kept
            Assert.AreEqual(4f, _sut.LastVoltages[0], 0.01f);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.4")]
        public void ThrowWhenSimulatingResponseOnForeignContract()
        {
            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => new Mock<IModbusRtu>().Object.SimulateReadResponse(_context, new byte[] { 0, 0 }));
            StringAssert.Contains(thrown.Message, "Unable to simulate response");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.2")]
        public void StampSimulatedReceiptFromVirtualClock()
        {
            // Arrange
            _sut.ReadVoltages();
            _context.AdvanceTime(TimeSpan.FromSeconds(3));

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(1f, 2f, 3f), SampleLogicBlock.VoltagesAddress);
            _context.FlushPendingActions();

            // Assert
            Assert.AreEqual(_context.VirtualNow, _sut.LastReadReceipt!.Value.ReceivedAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.4")]
        public void ThrowWhenSimulatingReadResponseWithNoPendingRequest()
        {
            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateReadResponse(_context, new byte[] { 0, 0 }));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.4")]
        public void ThrowWhenSimulatingReadResponseForUnmatchedAddress()
        {
            // Arrange
            _sut.ReadVoltages();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateReadResponse(_context, new byte[] { 0, 0 }, 999));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.4")]
        public void ThrowWhenSimulatingWriteResponseWithNoPendingRequest()
        {
            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateWriteResponse(_context));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-009.4")]
        public void ThrowWhenSimulatingWriteResponseForUnmatchedAddress()
        {
            // Arrange
            _sut.WriteSetpoint(42);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateWriteResponse(_context, 999));
        }
    }
}