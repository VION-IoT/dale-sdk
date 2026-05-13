using System;
using Vion.Dale.Sdk.Modbus.Rtu.TestKit;
using Vion.Dale.Sdk.TestKit;
using Moq;
using Vion.Examples.ModbusRtu.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusRtu.Test
{
    public class ModbusThroughputTestShould
    {
        public ModbusThroughputTestShould()
        {
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            _sut = new ModbusThroughputTest(loggerMock.Object);
        }

        private readonly ModbusThroughputTest _sut;

        [Fact]
        public void CompleteTestWhenAllResponsesReceived()
        {
            _sut.BurstSize = 2;
            var ctx = _sut.CreateTestContext().Build();

            _sut.StartTest = true;

            // Simulate both responses (each reads 1 register = 2 bytes for float)
            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(230f), 0);
            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(230f), 0);

            Assert.False(_sut.TestRunning);
            Assert.Equal(2, _sut.CompletedReads);
            Assert.Equal(0, _sut.FailedReads);
        }

        [Fact]
        public void HaveDefaultBurstSizeOf100()
        {
            Assert.Equal(100, _sut.BurstSize);
        }

        [Fact]
        public void HaveDefaultRegisterCountOfOne()
        {
            Assert.Equal(1, _sut.RegisterCount);
        }

        [Fact]
        public void HaveDefaultUnitIdOfOne()
        {
            Assert.Equal(1, _sut.UnitId);
        }

        [Fact]
        public void NotBeRunningInitially()
        {
            Assert.False(_sut.TestRunning);
            Assert.Equal(0, _sut.CompletedReads);
            Assert.Equal(0, _sut.FailedReads);
            Assert.Equal(0d, _sut.ReadsPerSecond);
        }

        [Fact]
        public void SendBurstReadRequestsWhenTestStarts()
        {
            _sut.BurstSize = 3;
            var ctx = _sut.CreateTestContext().Build();

            _sut.StartTest = true;

            Assert.True(_sut.TestRunning);
            ctx.VerifyModbusReadSent(_sut.Modbus, 0, times: Times.Exactly(3));
        }

        [Fact]
        public void StartTestPropertyAlwaysReturnsFalse()
        {
            Assert.False(_sut.StartTest);
        }

        [Fact]
        public void TrackFailedReads()
        {
            _sut.BurstSize = 2;
            var ctx = _sut.CreateTestContext().Build();

            _sut.StartTest = true;

            _sut.Modbus.SimulateReadResponse(ctx, ModbusResponseBuilder.FromFloats(230f), 0);
            _sut.Modbus.SimulateReadError(ctx, new TimeoutException("timeout"), 0);

            Assert.False(_sut.TestRunning);
            Assert.Equal(1, _sut.CompletedReads);
            Assert.Equal(1, _sut.FailedReads);
        }
    }
}