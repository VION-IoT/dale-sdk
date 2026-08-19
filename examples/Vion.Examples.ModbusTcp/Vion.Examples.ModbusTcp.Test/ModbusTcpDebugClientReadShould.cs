using System;
using System.Linq;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using System.Net.Sockets;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.ModbusTcp.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusTcp.Test
{
    /// <summary>
    ///     The read pane, including the part that matters most on a real device: whether the bytes that came
    ///     back are being interpreted the way the device meant them.
    /// </summary>
    public class ModbusTcpDebugClientReadShould : IDisposable
    {
        public void Dispose()
        {
            _fixture.Dispose();
        }

        /// <summary>Single-precision π in Modbus wire order (big-endian bytes, most significant word first).</summary>
        private static readonly byte[] PiMswFirst = { 0x40, 0x49, 0x0F, 0xDB };

        /// <summary>The same π with its two registers exchanged — the classic word-order trap.</summary>
        private static readonly byte[] PiLswFirst = { 0x0F, 0xDB, 0x40, 0x49 };

        private readonly DebugClientFixture _fixture = new();

        private ModbusTcpDebugClient Sut
        {
            get => _fixture.Sut;
        }

        /// <summary>
        ///     Configures a one-shot read and starts the block with polling off, so the only traffic a test
        ///     sees is the one it triggers.
        /// </summary>
        private LogicBlockTestContext<ModbusTcpDebugClient> ArrangeRead(ReadFunction function, int address, int quantity, RegisterFieldType fieldType)
        {
            Sut.ReadFunction = function;
            Sut.Address = address;
            Sut.Quantity = quantity;
            Sut.ReadFieldType = fieldType;
            Sut.PollingEnabled = false;

            return Sut.CreateTestContext().Build();
        }

        [Fact]
        public void AllowUpToTwoThousandBitsButNotMore()
        {
            var ctx = ArrangeRead(ReadFunction.Coils, 0, 2001, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("2000", Sut.LastReadError);
            Assert.Empty(_fixture.CommandProxy.ReadHistory);
        }

        [Fact]
        public void DecodeAsciiStringOntoTheFirstRow()
        {
            _fixture.SeedInputRegisters(20, System.Text.Encoding.ASCII.GetBytes("DALE-SIM"));
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 20, 4, RegisterFieldType.String);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal("DALE-SIM", Sut.Registers[0].Interpreted);
            Assert.All(Sut.Registers.Skip(1), row => Assert.Empty(row.Interpreted));
        }

        [Fact]
        public void DecodeFloat32UnderTheConfiguredWordOrder()
        {
            _fixture.SeedInputRegisters(6, PiMswFirst);
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 6, 2, RegisterFieldType.Float32);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal(3.14159274f, float.Parse(Sut.Registers[0].Interpreted, System.Globalization.CultureInfo.InvariantCulture), 5);

            // The interpreted value belongs to the register the field starts at; the second register of the
            // pair carries no field of its own.
            Assert.Empty(Sut.Registers[1].Interpreted);
        }

        [Fact]
        public void DecodeFloat64AcrossFourRegisters()
        {
            // Euler's number, 0x4005BF0A8B145769 in IEEE-754 double precision.
            _fixture.SeedInputRegisters(16, new byte[] { 0x40, 0x05, 0xBF, 0x0A, 0x8B, 0x14, 0x57, 0x69 });
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 16, 4, RegisterFieldType.Float64);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal(Math.E, double.Parse(Sut.Registers[0].Interpreted, System.Globalization.CultureInfo.InvariantCulture), 12);
            Assert.All(Sut.Registers.Skip(1), row => Assert.Empty(row.Interpreted));
        }

        [Fact]
        public void DecodeNegativeInt32()
        {
            // -123456 == 0xFFFE1DC0
            _fixture.SeedInputRegisters(4, new byte[] { 0xFF, 0xFE, 0x1D, 0xC0 });
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 4, 2, RegisterFieldType.Int32);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal("-123456", Sut.Registers[0].Interpreted);
        }

        [Fact]
        public void DecodeTheSameBytesDifferentlyWhenTheWordOrderIsFlipped()
        {
            // This is the whole point of the tool: identical bytes on the wire, two readings, and only the
            // matching word order produces the value the device meant.
            _fixture.SeedInputRegisters(8, PiLswFirst);
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 8, 2, RegisterFieldType.Float32);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();
            var withStandardOrder = float.Parse(Sut.Registers[0].Interpreted, System.Globalization.CultureInfo.InvariantCulture);

            Sut.ReadWordOrder32 = WordOrder32.LswToMsw;
            Sut.ReadOnce = true;
            ctx.FlushPendingActions();
            var withSwappedOrder = float.Parse(Sut.Registers[0].Interpreted, System.Globalization.CultureInfo.InvariantCulture);

            Assert.NotEqual(3.14159274f, withStandardOrder, 5);
            Assert.Equal(3.14159274f, withSwappedOrder, 5);
        }

        [Fact]
        public void KeepPollingAtOneReadPerTickWhileTheDeviceKeepsFailing()
        {
            // Polling is gated on the previous read having finished, so that a device which stops answering
            // cannot leave a growing backlog behind. The gate has to open again on the failure path too:
            // if it did not, the first timeout would stall polling for good and only one read would appear.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                _fixture.PollProxy.EnqueueReadTimeout(1, 0);
            }

            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x01 });
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;

            // Hold the faulted interval at the healthy one, so this test measures the in-flight gate rather
            // than the slow-down (which has its own test below).
            Sut.FaultedPollIntervalMs = 500;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1600));

            Assert.Equal(3, _fixture.PollProxy.ReadHistory.Count);

            // The SDK's own accumulation, not a counter this block keeps: three timeouts, nothing left
            // queued, and a link the client has decided is faulted.
            Assert.Equal(3, Sut.Link.TimeoutCount);
            Assert.Equal(0, Sut.Link.QueueDepth);
            Assert.Equal(ModbusLinkState.Faulted, Sut.Link.State);
        }

        [Fact]
        public void LeaveTheInterpretedColumnBlankWhereAFieldWouldNotFit()
        {
            // Three registers cannot hold two 32-bit values, so only the first row gets a reading.
            _fixture.SeedInputRegisters(0, new byte[] { 0x40, 0x49, 0x0F, 0xDB, 0x00, 0x01 });
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 0, 3, RegisterFieldType.Float32);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.NotEmpty(Sut.Registers[0].Interpreted);
            Assert.Empty(Sut.Registers[1].Interpreted);
            Assert.Empty(Sut.Registers[2].Interpreted);
        }

        [Fact]
        public void NameTheModbusExceptionCodeInsteadOfJustFailing()
        {
            _fixture.CommandProxy.EnqueueReadModbusException(1, 100, ModbusExceptionCode.IllegalDataAddress);
            var ctx = ArrangeRead(ReadFunction.HoldingRegisters, 100, 2, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("IllegalDataAddress", Sut.LastReadError);
            Assert.Contains("0x02", Sut.LastReadError);

            // A device that answers with an exception code is a *reachable* device: the outcome is a device
            // error and the link stays Online. Only a timeout or a transport failure faults it.
            Assert.Equal(1, Sut.CommandLink.DeviceErrorCount);
            Assert.Equal(ModbusLinkState.Online, Sut.CommandLink.State);
        }

        [Fact]
        public void NotPollWhilePollingIsDisabled()
        {
            Sut.PollingEnabled = false;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromSeconds(5));

            Assert.Empty(_fixture.PollProxy.ReadHistory);
        }

        [Fact]
        public void PollRepeatedlyOnThePollingConnection()
        {
            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x01 });
            Sut.ConnectionSettings = Sut.ConnectionSettings with { Port = 15020 };
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.ReadFieldType = RegisterFieldType.None;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1600));

            _fixture.PollProxy.VerifyReadSent(times: Times.Exactly(3));
            Assert.Empty(_fixture.CommandProxy.ReadHistory);
        }

        [Fact]
        public void PollSlowerOnceTheLinkFaults()
        {
            // The unattended pattern: the client reconnects on its own schedule, so polling a device that is
            // not answering at full rate only adds failed requests. Link.State is what the block reads to
            // decide — it is the SDK's verdict on the device, not a guess from the last exception.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                _fixture.PollProxy.EnqueueReadTimeout(1, 0);
            }

            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x01 });
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            Sut.FaultedPollIntervalMs = 5000;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1600));

            // The first poll faults the link; the next one is 5 s out, so it has not happened yet.
            Assert.Equal(ModbusLinkState.Faulted, Sut.Link.State);
            Assert.Single(_fixture.PollProxy.ReadHistory);

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(4000));

            Assert.Equal(2, _fixture.PollProxy.ReadHistory.Count);
        }

        [Fact]
        public void PopulateBitsRatherThanRegistersForCoilReads()
        {
            _fixture.SeedCoil(0, true);
            _fixture.SeedCoil(1, false);
            _fixture.SeedCoil(2, true);
            var ctx = ArrangeRead(ReadFunction.Coils, 0, 3, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal(new[] { true, false, true }, Sut.Bits.Select(bit => bit.Value));
            Assert.Equal(new[] { 0, 1, 2 }, Sut.Bits.Select(bit => bit.Address));
            Assert.Empty(Sut.Registers);
        }

        [Fact]
        public void ReadDiscreteInputsWithTheirOwnFunctionCode()
        {
            _fixture.SeedDiscreteInput(5, true);
            var ctx = ArrangeRead(ReadFunction.DiscreteInputs, 5, 1, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            _fixture.CommandProxy.VerifyReadSent(1, 5, 1, ReadEventKind.DiscreteInputs);
            Assert.True(Sut.Bits[0].Value);
        }

        [Fact]
        public void RejectAQuantityAboveTheModbusRegisterLimitWithoutTouchingTheWire()
        {
            var ctx = ArrangeRead(ReadFunction.HoldingRegisters, 0, 126, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Contains("125", Sut.LastReadError);
            Assert.Empty(_fixture.CommandProxy.ReadHistory);
        }

        [Fact]
        public void ReportATimeoutAsSuch()
        {
            _fixture.CommandProxy.EnqueueReadTimeout(1, 100);
            var ctx = ArrangeRead(ReadFunction.HoldingRegisters, 100, 2, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            // The error string leads with the SDK's outcome, so a timeout never reads as a device error.
            Assert.StartsWith("Timeout:", Sut.LastReadError);
            Assert.Equal(ModbusLinkState.Faulted, Sut.CommandLink.State);
        }

        [Fact]
        public void ReportBackingOffAheadOfFaultedSoTheReasonForFastFailuresIsVisible()
        {
            // Both states mean the device is not answering, but backing off additionally means the client has
            // stopped trying for now — the state whose symptom (instant failures) looks least like its cause.
            _fixture.PollProxy.EnqueueConnectFailure(new SocketException(10061));
            _fixture.PollProxy.EnqueueConnectFailure(new SocketException(10061));
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            Sut.FaultedPollIntervalMs = 500;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(1600));

            Assert.Equal(ModbusTcpConnectionState.BackingOff, Sut.Connection.State);
            Assert.Equal(ModbusLinkState.Faulted, Sut.Link.State);
            Assert.Equal(LinkStatus.BackingOff, Sut.LinkHealth);
        }

        [Fact]
        public void SayDisabledRatherThanKeepClaimingTheLastVerdictOnceTheConnectionIsSwitchedOff()
        {
            // A switched-off client issues nothing, so the SDK's Link keeps its last verdict — correctly, as
            // there is no newer evidence. Only the block knows the silence is deliberate, so only the block
            // can say so; mirroring Link here would leave the pill claiming a link nobody is using.
            _fixture.PollProxy.EnqueueConnectFailure(new SocketException(10061));
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            Sut.FaultedPollIntervalMs = 500;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(600));
            Assert.Equal(LinkStatus.Faulted, Sut.LinkHealth);

            Sut.ConnectionEnabled = false;

            Assert.Equal(LinkStatus.Disabled, Sut.LinkHealth);

            // The SDK's own snapshot is untouched — the block overlays the pill, it does not rewrite history.
            Assert.Equal(ModbusLinkState.Faulted, Sut.Link.State);

            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x01 });
            Sut.ConnectionEnabled = true;
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(600));

            Assert.Equal(LinkStatus.Online, Sut.LinkHealth);
        }

        [Fact]
        public void ShowRawRegisterColumnsExactlyAsReceived()
        {
            _fixture.SeedHoldingRegisters(100, new byte[] { 0x12, 0x34, 0xFB, 0x2E });
            var ctx = ArrangeRead(ReadFunction.HoldingRegisters, 100, 2, RegisterFieldType.None);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.Equal(2, Sut.Registers.Length);
            Assert.Equal(100, Sut.Registers[0].Address);
            Assert.Equal("12 34", Sut.Registers[0].Hex);
            Assert.Equal(0x1234, Sut.Registers[0].Unsigned);
            Assert.Equal(0x1234, Sut.Registers[0].Signed);

            // 0xFB2E is 64302 unsigned and -1234 signed — the two columns exist precisely so you do not
            // have to work that out in your head.
            Assert.Equal("FB 2E", Sut.Registers[1].Hex);
            Assert.Equal(64302, Sut.Registers[1].Unsigned);
            Assert.Equal(-1234, Sut.Registers[1].Signed);
            Assert.Empty(Sut.LastReadError);
        }

        [Fact]
        public void ShowTheSdksVerdictAsTheHeadlineRatherThanACountOfItsOwn()
        {
            // The pill is folded from Link.State and Connection.State in RefreshDiagnostics — it is not set
            // anywhere a transaction succeeds or fails, so it cannot drift from what the client thinks.
            _fixture.SeedHoldingRegisters(0, new byte[] { 0x00, 0x01 });
            Sut.ReadFunction = ReadFunction.HoldingRegisters;
            Sut.Address = 0;
            Sut.Quantity = 1;
            Sut.PollingEnabled = true;
            Sut.PollIntervalMs = 500;
            Sut.FaultedPollIntervalMs = 500;
            var ctx = Sut.CreateTestContext().Build();

            ctx.AdvanceTime(TimeSpan.FromMilliseconds(600));
            Assert.Equal(LinkStatus.Online, Sut.LinkHealth);

            _fixture.PollProxy.EnqueueReadTimeout(1, 0);
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(500));

            Assert.Equal(LinkStatus.Faulted, Sut.LinkHealth);
        }

        [Fact]
        public void SummarizeASuccessfulReadIncludingItsFunctionCode()
        {
            _fixture.SeedInputRegisters(10, new byte[] { 0x00, 0x2A });
            var ctx = ArrangeRead(ReadFunction.InputRegisters, 10, 1, RegisterFieldType.UInt16);

            Sut.ReadOnce = true;
            ctx.FlushPendingActions();

            Assert.StartsWith("FC4 @ 10 x 1", Sut.LastReadInfo);
            Assert.Equal(1, Sut.CommandLink.SuccessCount);
            Assert.NotNull(Sut.LastReadAt);
            Assert.Equal("42", Sut.Registers[0].Interpreted);
        }
    }
}