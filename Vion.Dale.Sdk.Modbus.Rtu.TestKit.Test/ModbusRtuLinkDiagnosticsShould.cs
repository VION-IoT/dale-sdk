using System;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test
{
    /// <summary>
    ///     The link summary's recent window driven through a real <c>ModbusRtu</c> on the test context's virtual clock.
    ///     The simulated responses run the client's own callback chain, so what reaches the summary is what a block's
    ///     transactions would record.
    /// </summary>
    [TestClass]
    public class ModbusRtuLinkDiagnosticsShould
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
        [TestProperty("spec", "AC-MODB-016.7")]
        public void ForgetRoundTripSpikeOnceWindowPassesOnClientsClock()
        {
            // Arrange
            ReadWithRoundTrip(TimeSpan.FromMilliseconds(900));
            ReadWithRoundTrip(TimeSpan.FromMilliseconds(100));
            Assert.AreEqual(TimeSpan.FromMilliseconds(900), _sut.Modbus.Link.RecentMaxRoundTrip, "The spike is in the window before time passes.");

            // Act
            _context.AdvanceTime(TimeSpan.FromMinutes(16));

            // Assert
            var link = _sut.Modbus.Link;
            Assert.AreEqual(0L, link.RecentRoundTripCount);
            Assert.IsNull(link.RecentMeanRoundTrip);
            Assert.IsNull(link.RecentMaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.9")]
        public void KeepSinceStartRoundTripSpikeOnceWindowForgetsIt()
        {
            // Arrange
            ReadWithRoundTrip(TimeSpan.FromMilliseconds(100));
            _context.AdvanceTime(TimeSpan.FromMinutes(2));
            ReadWithRoundTrip(TimeSpan.FromMilliseconds(900));
            var spikeObservedAt = _sut.LastReadReceipt!.Value.ReceivedAt;

            // Act
            _context.AdvanceTime(TimeSpan.FromMinutes(16));

            // Assert
            var link = _sut.Modbus.Link;
            Assert.AreEqual(TimeSpan.FromMilliseconds(900), link.MaxRoundTrip);
            Assert.AreEqual(spikeObservedAt, link.MaxRoundTripAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.3")]
        public void KeepLinkStateOnceWindowEmpties()
        {
            // Arrange
            ReadWithRoundTrip(TimeSpan.FromMilliseconds(100));

            // Act
            _context.AdvanceTime(TimeSpan.FromHours(1));

            // Assert
            Assert.AreEqual(ModbusLinkState.Online, _sut.Modbus.Link.State);
        }

        // The simulated response is observed now and the request was created before the clock moved, so the time
        // advanced between the two is the round trip.
        private void ReadWithRoundTrip(TimeSpan roundTrip)
        {
            _sut.ReadVoltages();
            _context.AdvanceTime(roundTrip);
            _sut.Modbus.SimulateReadResponse(_context, ModbusResponseBuilder.FromFloats(230.5f, 231.0f, 229.8f), SampleLogicBlock.VoltagesAddress);
            _context.FlushPendingActions();
        }
    }
}
