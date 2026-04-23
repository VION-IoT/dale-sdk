using Vion.Dale.Sdk.TestKit;
using Moq;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextShould
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
        public void VerifyModbusReadSentWithAllFilters()
        {
            // Act
            _sut.ReadVoltages();

            // Assert
            _context.VerifyModbusReadSent(_sut.Modbus, SampleLogicBlock.VoltagesAddress, quantity: 6);
        }

        [TestMethod]
        public void VerifyModbusReadSentWithoutFilters()
        {
            // Act
            _sut.ReadVoltages();

            // Assert
            _context.VerifyModbusReadSent();
        }

        [TestMethod]
        public void VerifyModbusReadSentForMultipleRequests()
        {
            // Act
            _sut.ReadVoltages();
            _sut.ReadCurrents();

            // Assert
            _context.VerifyModbusReadSent(_sut.Modbus, SampleLogicBlock.VoltagesAddress);
            _context.VerifyModbusReadSent(_sut.Modbus, SampleLogicBlock.CurrentsAddress);
            _context.VerifyModbusReadSent(times: Times.Exactly(2));
        }

        [TestMethod]
        public void VerifyModbusReadSentNeverWhenNothingHappens()
        {
            // Act / Assert
            _context.VerifyModbusReadSent(times: Times.Never());
        }

        [TestMethod]
        public void VerifyModbusWriteSentWithAddress()
        {
            // Act
            _sut.WriteSetpoint(42);

            // Assert
            _context.VerifyModbusWriteSent(_sut.Modbus, SampleLogicBlock.SetpointAddress);
        }

        [TestMethod]
        public void VerifyModbusWriteSentWithoutFilters()
        {
            // Act
            _sut.WriteSetpoint(42);

            // Assert
            _context.VerifyModbusWriteSent();
        }

        [TestMethod]
        public void VerifyModbusWriteSentNeverWhenNothingHappens()
        {
            // Act / Assert
            _context.VerifyModbusWriteSent(times: Times.Never());
        }
    }
}
