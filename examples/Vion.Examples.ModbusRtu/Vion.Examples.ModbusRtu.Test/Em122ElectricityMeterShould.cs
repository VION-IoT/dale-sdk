using System;
using Vion.Dale.Sdk.Modbus.Rtu.TestKit;
using Vion.Dale.Sdk.TestKit;
using Moq;
using Vion.Examples.ModbusRtu.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusRtu.Test
{
    public class Em122ElectricityMeterShould
    {
        public Em122ElectricityMeterShould()
        {
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            _sut = new Em122ElectricityMeter(loggerMock.Object);
        }

        private readonly Em122ElectricityMeter _sut;

        [Fact]
        public void HaveDefaultDemandPeriodOf60Minutes()
        {
            Assert.Equal(60f, _sut.DemandPeriodMinutes);
        }

        [Fact]
        public void HaveDefaultUnitIdOfOne()
        {
            Assert.Equal(1, _sut.UnitId);
        }

        [Fact]
        public void HavePollingEnabledByDefault()
        {
            Assert.True(_sut.PollingEnabled);
        }

        [Fact]
        public void HaveZeroInitialDiagnostics()
        {
            Assert.Equal(0, _sut.ReadCount);
            Assert.Equal(0, _sut.ErrorCount);
            Assert.Equal("", _sut.LastError);
        }

        [Fact]
        public void HaveZeroInitialMeasurements()
        {
            Assert.Equal(0f, _sut.VoltageL1);
            Assert.Equal(0f, _sut.CurrentL1);
            Assert.Equal(0f, _sut.TotalActivePower);
            Assert.Equal(0f, _sut.Frequency);
            Assert.Equal(0f, _sut.ImportEnergy);
        }

        [Fact]
        public void IncrementErrorCountOnReadError()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.FireTimer(lb => lb.Poll());
            _sut.Modbus.SimulateReadError(ctx, new TimeoutException("Device not responding"), 0);

            Assert.Equal(1, _sut.ErrorCount);
            Assert.Equal("Device not responding", _sut.LastError);
        }

        [Fact]
        public void NotSendReadRequestsWhenPollingIsDisabled()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.PollingEnabled = false;
            ctx.ClearRecordedMessages();
            _sut.FireTimer(lb => lb.Poll());

            ctx.VerifyModbusReadSent(times: Times.Never());
        }

        [Fact]
        public void ResetEnergyCountersPropertyAlwaysReturnsFalse()
        {
            Assert.False(_sut.ResetEnergyCounters);
        }

        [Fact]
        public void SendReadRequestsWhenPollingIsEnabled()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.FireTimer(lb => lb.Poll());

            // EM122 sends 11 read requests per poll cycle
            ctx.VerifyModbusReadSent(_sut.Modbus, 0, 6); // voltages (3 floats = 6 regs)
            ctx.VerifyModbusReadSent(_sut.Modbus, 6, 6); // currents
            ctx.VerifyModbusReadSent(_sut.Modbus, 12, 6); // active power
            ctx.VerifyModbusReadSent(_sut.Modbus, 52, 2); // total active power
            ctx.VerifyModbusReadSent(_sut.Modbus, 70, 2); // frequency
        }

        [Fact]
        public void SendWriteRequestWhenDemandPeriodChanges()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.DemandPeriodMinutes = 30f;

            ctx.VerifyModbusWriteSent(_sut.Modbus, 2);
        }

        [Fact]
        public void SendWriteRequestWhenResetEnergyCountersIsSet()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.ResetEnergyCounters = true;

            ctx.VerifyModbusWriteSent(_sut.Modbus, 0xF010);
        }

        [Fact]
        public void UpdateActivePowerWhenModbusResponds()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.FireTimer(lb => lb.Poll());
            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(1200f, 1100f, 1150f), 12);

            Assert.Equal(1200f, _sut.ActivePowerL1, 0.01f);
            Assert.Equal(1100f, _sut.ActivePowerL2, 0.01f);
            Assert.Equal(1150f, _sut.ActivePowerL3, 0.01f);
        }

        [Fact]
        public void UpdateCurrentsWhenModbusResponds()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.FireTimer(lb => lb.Poll());
            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(5.2f, 4.8f, 5.0f), 6);

            Assert.Equal(5.2f, _sut.CurrentL1, 0.01f);
            Assert.Equal(4.8f, _sut.CurrentL2, 0.01f);
            Assert.Equal(5.0f, _sut.CurrentL3, 0.01f);
        }

        [Fact]
        public void UpdateVoltagesWhenModbusResponds()
        {
            var ctx = _sut.CreateTestContext().Build();

            _sut.FireTimer(lb => lb.Poll());
            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(230.5f, 231.0f, 229.8f), 0);

            Assert.Equal(230.5f, _sut.VoltageL1, 0.01f);
            Assert.Equal(231.0f, _sut.VoltageL2, 0.01f);
            Assert.Equal(229.8f, _sut.VoltageL3, 0.01f);
            Assert.Equal(1, _sut.ReadCount);
        }
    }
}