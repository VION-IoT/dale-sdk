using System;
using System.Linq;
using Moq;
using Vion.Dale.Sdk.MBus;
using Vion.Dale.Sdk.MBus.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.MBus.LogicBlocks;
using Xunit;

namespace Vion.Examples.MBus.Test
{
    /// <summary>
    ///     The debug client against the captured EM530 read: what an engineer commissioning the meter sees.
    /// </summary>
    public class MBusDebugClientShould
    {
        private readonly LogicBlockTestContext<MBusDebugClient> _context;

        private readonly MBusDebugClient _sut;

        public MBusDebugClientShould()
        {
            _sut = LogicBlockTestHelper.Create<MBusDebugClient>();
            _context = _sut.InitializeForTest();
        }

        [Fact]
        public void ShowEveryRecordWithSelectorThatFindsIt()
        {
            // Arrange
            _sut.PrimaryAddress = 1;
            _sut.ReadNow = true;

            // Act
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams);
            _context.FlushPendingActions();

            // Assert
            Assert.Equal(47, _sut.Records.Length);
            var operatingTime = _sut.Records.Single(row => row.Dib == "84 80 40" && row.Vib == "A6 74");
            Assert.Equal(4, operatingTime.Telegram);
            Assert.Equal("92.61 h", operatingTime.Value);
            Assert.Equal("MBusSelector.Of(MBusQuantity.OperatingTime, subunit: 2)", operatingTime.Selector);
            Assert.Equal("00006613", _sut.Header.Identification);
            Assert.Equal("GAV", _sut.Header.Manufacturer);
            Assert.True(_sut.Complete);
        }

        [Fact]
        public void ShowTelegramsAsHexReadyToReplay()
        {
            // Arrange
            _sut.PrimaryAddress = 1;
            _sut.ReadNow = true;
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams[0]);
            _context.FlushPendingActions();
            var replayed = _sut.Telegrams[0];
            _sut.ReadNow = true;

            // Act
            _sut.MBus.SimulateReadResponse(_context, replayed);
            _context.FlushPendingActions();

            // Assert
            Assert.Equal(Em530Capture.Telegrams[0], replayed);
            Assert.Equal(11, _sut.Records.Length);
            Assert.False(_sut.Complete);
        }

        [Fact]
        public void ReportFailedReadWithCapture()
        {
            // Arrange
            _sut.ReadNow = true;

            // Act
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams[4].Replace("86 16", "87 16"));
            _context.FlushPendingActions();

            // Assert
            Assert.StartsWith("ProtocolError", _sut.LastReadError);
            Assert.Contains("Telegrams as received", _sut.LastReadError);
        }

        [Fact]
        public void PollOnlyWhenEnabled()
        {
            // Arrange
            _context.AdvanceTime(TimeSpan.FromSeconds(_sut.PollIntervalSeconds));
            _context.VerifyMBusReadSent(times: Times.Never());
            _sut.PollingEnabled = true;

            // Act
            _context.AdvanceTime(TimeSpan.FromSeconds(_sut.PollIntervalSeconds));

            // Assert
            _context.VerifyMBusReadSent(times: Times.Once());
        }

        [Fact]
        public void KeepPollIntervalAtLeastDefaultOperationTimeout()
        {
            // Arrange

            // Act
            _sut.PollIntervalSeconds = 10;

            // Assert
            Assert.Equal((int)_sut.MBus.DefaultOperationTimeout.TotalSeconds, _sut.PollIntervalSeconds);
        }
    }
}
