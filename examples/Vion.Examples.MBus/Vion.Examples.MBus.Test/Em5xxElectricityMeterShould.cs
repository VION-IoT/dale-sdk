using System;
using Moq;
using Vion.Dale.Sdk.MBus;
using Vion.Dale.Sdk.MBus.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.MBus.LogicBlocks;
using Xunit;

namespace Vion.Examples.MBus.Test
{
    /// <summary>
    ///     The meter block against the captured EM530 read, replayed through the SDK's frame checks and decoder.
    /// </summary>
    public class Em5xxElectricityMeterShould
    {
        private readonly LogicBlockTestContext<Em5xxElectricityMeter> _context;

        private readonly Em5xxElectricityMeter _sut;

        public Em5xxElectricityMeterShould()
        {
            _sut = LogicBlockTestHelper.Create<Em5xxElectricityMeter>();
            _context = _sut.InitializeForTest();
        }

        [Fact]
        public void PublishCapturedValuesInTheirUnits()
        {
            // Arrange
            _sut.Poll();

            // Act
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams);
            _context.FlushPendingActions();

            // Assert
            Assert.Equal(0.0, _sut.ImportEnergy);
            Assert.Equal(0.0, _sut.ActivePower);
            Assert.Equal(238.6, _sut.VoltageL1!.Value, 9);
            Assert.Equal(50.0, _sut.Frequency);
            Assert.Equal(92.61, _sut.RunHours!.Value, 9);
            Assert.Equal(0.0, _sut.ImportT1);
            Assert.False(_sut.MeterError);
            Assert.Equal(MBusLinkState.Online, _sut.Link.State);
        }

        [Fact]
        public void LeaveLaterTelegramsValuesEmptyWhenProviderReadsFirstOnly()
        {
            // Arrange
            _sut.Poll();

            // Act
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams[0]);
            _context.FlushPendingActions();

            // Assert
            Assert.Equal(0.0, _sut.ImportEnergy);
            Assert.Null(_sut.Frequency);
            Assert.Null(_sut.ImportT1);
        }

        [Fact]
        public void EmptyValuesWhenReadFails()
        {
            // Arrange
            _sut.Poll();
            _sut.MBus.SimulateReadResponse(_context, Em530Capture.Telegrams);
            _context.FlushPendingActions();
            _sut.Poll();

            // Act
            _sut.MBus.SimulateReadError(_context, MBusOutcome.NoResponse);
            _context.FlushPendingActions();

            // Assert
            Assert.Null(_sut.ImportEnergy);
            Assert.Null(_sut.Frequency);
            Assert.StartsWith("NoResponse", _sut.LastError);
            Assert.Equal(MBusLinkState.Faulted, _sut.Link.State);
        }

        [Fact]
        public void ReadAllFiveTelegramsFromConfiguredAddress()
        {
            // Arrange
            _sut.PrimaryAddress = 7;

            // Act
            _sut.Poll();

            // Assert
            _context.VerifyMBusReadSent(_sut.MBus, 7, 5);
        }

        [Fact]
        public void SendNoSecondReadWhileFirstIsPending()
        {
            // Arrange
            _sut.Poll();

            // Act
            _sut.Poll();

            // Assert
            _context.VerifyMBusReadSent(times: Times.Once());
            Assert.Equal(1L, _sut.MBus.Link.BusyCount);
        }
    }
}
