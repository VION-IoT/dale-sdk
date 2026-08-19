using System;
using System.Linq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.ModbusTcp.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusTcp.Test
{
    /// <summary>
    ///     The write pane. Assertions are on the bytes the fake recorded rather than on a decoded value,
    ///     because the encoding is exactly what a write is supposed to get right.
    /// </summary>
    public class ModbusTcpDebugClientWriteShould : IDisposable
    {
        public void Dispose()
        {
            _fixture.Dispose();
        }

        private readonly DebugClientFixture _fixture = new();

        private ModbusTcpDebugClient Sut
        {
            get => _fixture.Sut;
        }

        private LogicBlockTestContext<ModbusTcpDebugClient> Arrange(WriteFunction function, int address, string value)
        {
            Sut.PollingEnabled = false;
            Sut.WriteFunction = function;
            Sut.WriteAddress = address;
            Sut.WriteValue = value;

            return Sut.CreateTestContext().Build();
        }

        [Fact]
        public void AcceptANegativeSingleRegisterValue()
        {
            var ctx = Arrange(WriteFunction.SingleRegister, 10, "-1234");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 10, new byte[] { 0xFB, 0x2E }, WriteEventKind.SingleRegister);
        }

        [Fact]
        public void AcceptHexForASingleRegisterWrite()
        {
            var ctx = Arrange(WriteFunction.SingleRegister, 10, "0x12AB");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 10, new byte[] { 0x12, 0xAB }, WriteEventKind.SingleRegister);
        }

        [Fact]
        public void EncodeAFloat32AcrossTwoRegistersInTheConfiguredWordOrder()
        {
            var ctx = Arrange(WriteFunction.MultipleRegisters, 100, "3.14159274");
            Sut.WriteFieldType = RegisterFieldType.Float32;

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 100, new byte[] { 0x40, 0x49, 0x0F, 0xDB }, WriteEventKind.MultipleRegisters);
        }

        [Fact]
        public void EncodeAListOfValuesAcrossConsecutiveRegisters()
        {
            var ctx = Arrange(WriteFunction.MultipleRegisters, 0, "1, 2, 3");
            Sut.WriteFieldType = RegisterFieldType.UInt16;

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 0, new byte[] { 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 }, WriteEventKind.MultipleRegisters);
        }

        [Fact]
        public void EncodeASingleRegisterWrite()
        {
            var ctx = Arrange(WriteFunction.SingleRegister, 10, "4660");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 10, new byte[] { 0x12, 0x34 }, WriteEventKind.SingleRegister);
            Assert.Empty(Sut.LastWriteError);
            Assert.StartsWith("FC6 @ 10", Sut.LastWriteInfo);
        }

        [Fact]
        public void RefuseACoilValueThatIsNotABoolean()
        {
            var ctx = Arrange(WriteFunction.SingleCoil, 0, "42");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("coil value", Sut.LastWriteError);
            Assert.Empty(_fixture.CommandProxy.WriteHistory);
        }

        [Fact]
        public void RefuseAMalformedValueBeforeAnythingReachesTheDevice()
        {
            var ctx = Arrange(WriteFunction.MultipleRegisters, 0, "not-a-number");
            Sut.WriteFieldType = RegisterFieldType.UInt16;

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            Assert.NotEmpty(Sut.LastWriteError);
            Assert.Empty(_fixture.CommandProxy.WriteHistory);

            // A value that could not be parsed never left the block, so it says nothing about the device:
            // the headline stays where it was rather than reporting a link fault that did not happen.
            Assert.Equal(LinkStatus.Unknown, Sut.LinkHealth);
        }

        [Fact]
        public void RefuseARegisterValueThatDoesNotFitIn16Bits()
        {
            var ctx = Arrange(WriteFunction.SingleRegister, 0, "70000");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("16-bit", Sut.LastWriteError);
            Assert.Empty(_fixture.CommandProxy.WriteHistory);
        }

        [Fact]
        public void RefuseAWriteLongerThanOneFc16Request()
        {
            var ctx = Arrange(WriteFunction.MultipleRegisters, 0, string.Join(",", Enumerable.Repeat("1", 124)));
            Sut.WriteFieldType = RegisterFieldType.UInt16;

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("123", Sut.LastWriteError);
            Assert.Empty(_fixture.CommandProxy.WriteHistory);
        }

        [Fact]
        public void SendWritesOnTheCommandConnectionWhilePollsUseTheOther()
        {
            // The split exists so an unresponsive poll cannot delay a manual write; this pins that the
            // block really does use two connections rather than one shared queue.
            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x00 });
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            Sut.WriteFunction = WriteFunction.SingleRegister;
            Sut.WriteAddress = 0;
            Sut.WriteValue = "7";
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(600));
            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            Assert.NotEmpty(_fixture.PollProxy.ReadHistory);
            Assert.Empty(_fixture.PollProxy.WriteHistory);
            Assert.NotEmpty(_fixture.CommandProxy.WriteHistory);
        }

        [Fact]
        public void SwapTheWordsOfAFloat32WriteWhenAskedTo()
        {
            var ctx = Arrange(WriteFunction.MultipleRegisters, 100, "3.14159274");
            Sut.WriteFieldType = RegisterFieldType.Float32;
            Sut.WriteWordOrder32 = WordOrder32.LswToMsw;

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            // Same value, the two registers exchanged — what a device documented as "little-endian word
            // order for 32-bit values" expects to receive.
            _fixture.CommandProxy.VerifyWriteSent(1, 100, new byte[] { 0x0F, 0xDB, 0x40, 0x49 }, WriteEventKind.MultipleRegisters);
        }

        [Fact]
        public void WriteASingleCoil()
        {
            var ctx = Arrange(WriteFunction.SingleCoil, 3, "true");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 3, kind: WriteEventKind.SingleCoil);
            Assert.StartsWith("FC5 @ 3", Sut.LastWriteInfo);
        }

        [Fact]
        public void WriteMultipleCoilsFromACommaSeparatedList()
        {
            var ctx = Arrange(WriteFunction.MultipleCoils, 0, "1, 0, on, off");

            Sut.WriteOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyWriteSent(1, 0, kind: WriteEventKind.MultipleCoils);
            Assert.Empty(Sut.LastWriteError);
        }
    }
}