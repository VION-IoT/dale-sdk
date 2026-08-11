using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.ModbusTcp.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusTcp.Test
{
    /// <summary>
    ///     Watch slots: pinned registers that keep being re-read so their value can be charted.
    /// </summary>
    public class ModbusTcpDebugClientWatchShould : IDisposable
    {
        public void Dispose()
        {
            _fixture.Dispose();
        }

        private static readonly byte[] PiMswFirst = { 0x40, 0x49, 0x0F, 0xDB };

        private readonly DebugClientFixture _fixture = new();

        private ModbusTcpDebugClient Sut
        {
            get => _fixture.Sut;
        }

        [Fact]
        public void DecodeAWatchedRegisterOnEveryTick()
        {
            _fixture.SeedInputRegisters(6, PiMswFirst);
            Sut.PollingEnabled = false;
            Sut.WatchIntervalMs = 1000;
            Sut.Watch1.Enabled = true;
            Sut.Watch1.Label = "Pi";
            Sut.Watch1.Function = WatchFunction.InputRegisters;
            Sut.Watch1.Address = 6;
            Sut.Watch1.FieldType = WatchFieldType.Float32;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 2).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1100));

            Assert.Equal(Math.PI, Sut.Watch1.Value, 5);
            Assert.Equal("OK", Sut.Watch1.Status);
        }

        [Fact]
        public void FollowAChangingValueAcrossTicks()
        {
            _fixture.SeedInputRegisters(0, new byte[] { 0x00, 0x0A });
            Sut.PollingEnabled = false;
            Sut.WatchIntervalMs = 1000;
            Sut.Watch1.Enabled = true;
            Sut.Watch1.Function = WatchFunction.InputRegisters;
            Sut.Watch1.Address = 0;
            Sut.Watch1.FieldType = WatchFieldType.UInt16;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 1).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1100));
            Assert.Equal(10d, Sut.Watch1.Value);

            _fixture.SeedInputRegisters(0, new byte[] { 0x00, 0x14 });
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1000));

            Assert.Equal(20d, Sut.Watch1.Value);
        }

        [Fact]
        public void HonorThePerSlotWordOrder()
        {
            // The slot carries its own byte and word order, so two slots can read the same device with
            // different conventions — which is what mixed-endianness devices force you to do.
            _fixture.SeedInputRegisters(8, new byte[] { 0x0F, 0xDB, 0x40, 0x49 });
            Sut.PollingEnabled = false;
            Sut.Watch1.Enabled = true;
            Sut.Watch1.Function = WatchFunction.InputRegisters;
            Sut.Watch1.Address = 8;
            Sut.Watch1.FieldType = WatchFieldType.Float32;
            Sut.Watch1.WordOrder32 = WordOrder32.LswToMsw;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 1).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1100));

            Assert.Equal(Math.PI, Sut.Watch1.Value, 5);
        }

        [Fact]
        public void IgnoreSlotsBeyondTheConfiguredCount()
        {
            // Slots above WatchSlotCount are not part of this instance, so configuring one must have no
            // effect at all — no reads, no traffic.
            _fixture.SeedInputRegisters(6, PiMswFirst);
            Sut.PollingEnabled = false;
            Sut.Watch3.Enabled = true;
            Sut.Watch3.Function = WatchFunction.InputRegisters;
            Sut.Watch3.Address = 6;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 2).Build();

            ctx.AdvanceTime(TimeSpan.FromSeconds(3));

            Assert.Empty(_fixture.PollProxy.ReadHistory);
            Assert.Equal(0d, Sut.Watch3.Value);
        }

        [Fact]
        public void KeepOtherSlotsRunningWhenOneOfThemFails()
        {
            _fixture.SeedInputRegisters(6, PiMswFirst);
            _fixture.PollProxy.EnqueueReadModbusException(1, 999, ModbusExceptionCode.IllegalDataAddress);
            Sut.PollingEnabled = false;
            Sut.Watch1.Enabled = true;
            Sut.Watch1.Function = WatchFunction.InputRegisters;
            Sut.Watch1.Address = 999;
            Sut.Watch1.FieldType = WatchFieldType.Float32;
            Sut.Watch2.Enabled = true;
            Sut.Watch2.Function = WatchFunction.InputRegisters;
            Sut.Watch2.Address = 6;
            Sut.Watch2.FieldType = WatchFieldType.Float32;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 2).Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1100));

            Assert.Contains("IllegalDataAddress", Sut.Watch1.Status);
            Assert.Equal("OK", Sut.Watch2.Status);
            Assert.Equal(Math.PI, Sut.Watch2.Value, 5);
        }

        [Fact]
        public void ReadNothingForADisabledSlot()
        {
            Sut.PollingEnabled = false;
            Sut.Watch1.Enabled = false;
            Sut.Watch1.Address = 6;
            var ctx = Sut.CreateTestContext().WithInstantiationParameter(block => block.WatchSlotCount, 3).Build();

            ctx.AdvanceTime(TimeSpan.FromSeconds(3));

            Assert.Empty(_fixture.PollProxy.ReadHistory);
        }
    }
}