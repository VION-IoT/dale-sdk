using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.MBus.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextExtensionsShould
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
        [TestProperty("spec", "AC-TKIT-016.3")]
        public void PassWhenReadMatchesEveryNamedField()
        {
            // Arrange
            _sut.Poll();

            // Act / Assert
            _context.VerifyMBusReadSent(_sut.MBus, SampleLogicBlock.PrimaryAddress, 5);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.3")]
        [DataRow(2, 5, DisplayName = "another address")]
        [DataRow(1, 4, DisplayName = "another telegram count")]
        public void FailWhenReadDiffersInNamedField(int primaryAddress, int maxTelegrams)
        {
            // Arrange
            _sut.Poll();

            // Act / Assert
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyMBusReadSent(_sut.MBus, primaryAddress, maxTelegrams));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-016.3")]
        public void CountReadsSent()
        {
            // Arrange
            _sut.Poll();
            _sut.MBus.SimulateReadError(_context, MBusOutcome.NoResponse);
            _sut.Poll();

            // Act / Assert
            _context.VerifyMBusReadSent(times: Times.Exactly(2));
            Assert.ThrowsExactly<TestKitVerificationException>(() => _context.VerifyMBusReadSent(times: Times.Once()));
        }
    }
}
