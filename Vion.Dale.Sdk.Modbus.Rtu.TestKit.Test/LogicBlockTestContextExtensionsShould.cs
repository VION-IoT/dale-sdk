using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    /// <summary>
    ///     This kit's two verification helpers, which filter the recorded contract messages the way every
    ///     other verify helper in the family does — by data type, by contract identifier, and by the
    ///     request's own fields.
    /// </summary>
    [TestClass]
    public class LogicBlockTestContextExtensionsShould
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
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusReadSentWithAllFilters()
        {
            // Act
            _sut.ReadVoltages();

            // Assert
            _context.VerifyModbusReadSent(_sut.Modbus, SampleLogicBlock.VoltagesAddress, 6);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusReadSentWithoutFilters()
        {
            // Act
            _sut.ReadVoltages();

            // Assert
            _context.VerifyModbusReadSent();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
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
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusReadSentNeverWhenNothingHappens()
        {
            // Act / Assert
            _context.VerifyModbusReadSent(times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusWriteSentWithAddress()
        {
            // Act
            _sut.WriteSetpoint(42);

            // Assert
            _context.VerifyModbusWriteSent(_sut.Modbus, SampleLogicBlock.SetpointAddress);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusWriteSentWithoutFilters()
        {
            // Act
            _sut.WriteSetpoint(42);

            // Assert
            _context.VerifyModbusWriteSent();
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-005.4")]
        public void VerifyModbusWriteSentNeverWhenNothingHappens()
        {
            // Act / Assert
            _context.VerifyModbusWriteSent(times: Times.Never());
        }
    }
}