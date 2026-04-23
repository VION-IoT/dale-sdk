using System;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    [TestClass]
    public class IModbusRtuExtensionsShould
    {
        private SampleLogicBlock _sut = null!;

        private LogicBlockTestContext<SampleLogicBlock> _context = null!;

        [TestInitialize]
        public void Initialize()
        {
            // Arrange
            _sut = LogicBlockTestHelper.Create<SampleLogicBlock>();
            _context = _sut.InitializeForTest();
        }

        [TestMethod]
        public void InvokeSuccessCallbackOnSimulatedReadResponse()
        {
            // Arrange
            _sut.ReadVoltages();

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(230.5f, 231.0f, 229.8f), SampleLogicBlock.VoltagesAddress);

            // Assert
            Assert.HasCount(3, _sut.LastVoltages);
            Assert.AreEqual(230.5f, _sut.LastVoltages[0], 0.01f);
            Assert.AreEqual(231.0f, _sut.LastVoltages[1], 0.01f);
            Assert.AreEqual(229.8f, _sut.LastVoltages[2], 0.01f);
        }

        [TestMethod]
        public void MatchReadResponseByStartingAddressWhenMultipleReadsArePending()
        {
            // Arrange
            _sut.ReadVoltages();
            _sut.ReadCurrents();

            // Act
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(5.2f, 4.8f, 5.0f), SampleLogicBlock.CurrentsAddress);

            // Assert
            Assert.HasCount(3, _sut.LastCurrents);
            Assert.IsEmpty(_sut.LastVoltages);
        }

        [TestMethod]
        public void InvokeErrorCallbackOnSimulatedReadError()
        {
            // Arrange
            var expectedError = new TimeoutException("Device not responding");
            _sut.ReadVoltages();

            // Act
            _sut.Modbus.SimulateReadError(_context, expectedError, SampleLogicBlock.VoltagesAddress);

            // Assert
            Assert.AreSame(expectedError, _sut.LastError);
        }

        [TestMethod]
        public void InvokeSuccessCallbackOnSimulatedWriteResponse()
        {
            // Arrange
            _sut.WriteSetpoint(42);

            // Act
            _sut.Modbus.SimulateWriteResponse(_context, SampleLogicBlock.SetpointAddress);

            // Assert
            Assert.AreEqual(1, _sut.WriteSuccessCount);
        }

        [TestMethod]
        public void InvokeErrorCallbackOnSimulatedWriteError()
        {
            // Arrange
            var expectedError = new InvalidOperationException("Write rejected");
            _sut.WriteSetpoint(42);

            // Act
            _sut.Modbus.SimulateWriteError(_context, expectedError, SampleLogicBlock.SetpointAddress);

            // Assert
            Assert.AreSame(expectedError, _sut.LastError);
            Assert.AreEqual(0, _sut.WriteSuccessCount);
        }

        [TestMethod]
        public void ThrowWhenSimulatingReadResponseWithNoPendingRequest()
        {
            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateReadResponse(_context, new byte[] { 0, 0 }));
        }

        [TestMethod]
        public void ThrowWhenSimulatingReadResponseForUnmatchedAddress()
        {
            // Arrange
            _sut.ReadVoltages();

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateReadResponse(_context, new byte[] { 0, 0 }, startingAddress: 999));
        }

        [TestMethod]
        public void ThrowWhenSimulatingWriteResponseWithNoPendingRequest()
        {
            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateWriteResponse(_context));
        }

        [TestMethod]
        public void ThrowWhenSimulatingWriteResponseForUnmatchedAddress()
        {
            // Arrange
            _sut.WriteSetpoint(42);

            // Act / Assert
            Assert.Throws<InvalidOperationException>(() => _sut.Modbus.SimulateWriteResponse(_context, address: 999));
        }
    }
}
