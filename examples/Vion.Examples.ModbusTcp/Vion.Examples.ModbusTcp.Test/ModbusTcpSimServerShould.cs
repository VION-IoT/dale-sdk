using System;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.ModbusTcp.LogicBlocks;
using Xunit;

namespace Vion.Examples.ModbusTcp.Test
{
    /// <summary>
    ///     The bundled simulator. These assertions are at the byte level on purpose: the map's whole value is
    ///     that a reader can trust what is supposed to be at each address.
    /// </summary>
    public class ModbusTcpSimServerShould : IDisposable
    {
        public ModbusTcpSimServerShould()
        {
            _sut = new ModbusTcpSimServer(_harness.Server, LogicBlockTestHelper.CreateLoggerMock().Object);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        private readonly FakeModbusTcpServerHarness _harness = new();

        private readonly ModbusTcpSimServer _sut;

        [Fact]
        public void AdvanceItsCounterOnEveryTick()
        {
            _sut.CreateTestContext().Build();

            _sut.FireTimer(block => block.OnTick());
            Assert.Equal(new byte[] { 0x00, 0x01 }, _harness.Client.ReadInputRegistersRaw(0, 1));

            _sut.FireTimer(block => block.OnTick());
            _sut.FireTimer(block => block.OnTick());
            Assert.Equal(new byte[] { 0x00, 0x03 }, _harness.Client.ReadInputRegistersRaw(0, 1));

            // The 32-bit counter tracks the same tick, ten times over.
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x1E }, _harness.Client.ReadInputRegistersRaw(2, 2));
        }

        [Fact]
        public void CountUptimeInMilliseconds()
        {
            _sut.CreateTestContext().Build();

            _sut.FireTimer(block => block.OnTick());
            _sut.FireTimer(block => block.OnTick());

            Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07, 0xD0 }, _harness.Client.ReadInputRegistersRaw(12, 4));
        }

        [Fact]
        public void DriveTheCoilsAsABinaryCounter()
        {
            _sut.CreateTestContext().Build();

            _sut.FireTimer(block => block.OnTick());

            // Tick 1: only the least significant coil is set.
            Assert.Equal(new[] { true, false, false, false }, _harness.Client.ReadCoils(0, 4));

            _sut.FireTimer(block => block.OnTick());

            // Tick 2: carries into the next coil.
            Assert.Equal(new[] { false, true, false, false }, _harness.Client.ReadCoils(0, 4));
        }

        [Fact]
        public void EchoWhatAClientWroteIntoTheInputRegisters()
        {
            // Holding registers are client-writable, so seeing a value come back from the read-only input
            // area is proof the write actually landed rather than being read back out of a local cache.
            _sut.CreateTestContext().Build();

            _harness.Client.WriteMultipleHoldingRegistersRaw(0, new byte[] { 0xBE, 0xEF, 0xCA, 0xFE });
            _sut.FireTimer(block => block.OnTick());

            Assert.Equal(new byte[] { 0xBE, 0xEF, 0xCA, 0xFE }, _harness.Client.ReadInputRegistersRaw(40, 2));
        }

        [Fact]
        public void ListenOnceStarted()
        {
            _sut.CreateTestContext().Build();

            Assert.True(_sut.IsListening);
            Assert.Empty(_sut.LastError);
        }

        [Fact]
        public void PublishANegativeInt16AndInt32()
        {
            _sut.CreateTestContext().Build();

            Assert.Equal(new byte[] { 0xFB, 0x2E }, _harness.Client.ReadInputRegistersRaw(1, 1));
            Assert.Equal(new byte[] { 0xFF, 0xFE, 0x1D, 0xC0 }, _harness.Client.ReadInputRegistersRaw(4, 2));
        }

        [Fact]
        public void PublishItsNameAsAsciiText()
        {
            _sut.CreateTestContext().Build();

            var bytes = _harness.Client.ReadInputRegistersRaw(20, 8);

            Assert.Equal("DALE-SIM-SERVER!", System.Text.Encoding.ASCII.GetString(bytes));
        }

        [Fact]
        public void PublishPiAtInputRegisterSix()
        {
            _sut.CreateTestContext().Build();

            Assert.Equal(new byte[] { 0x40, 0x49, 0x0F, 0xDB }, _harness.Client.ReadInputRegistersRaw(6, 2));
        }

        [Fact]
        public void PublishTheSamePiWithSwappedWordsAtInputRegisterEight()
        {
            // The pair 6-7 and 8-9 hold the identical value under opposite word orders — reading one with
            // the other's setting is the fastest way to recognise a word-order mismatch on a real device.
            _sut.CreateTestContext().Build();

            Assert.Equal(new byte[] { 0x0F, 0xDB, 0x40, 0x49 }, _harness.Client.ReadInputRegistersRaw(8, 2));
        }

        [Fact]
        public void SeedAWritableFloatAtHoldingRegisterOneHundred()
        {
            _sut.CreateTestContext().Build();

            Assert.Equal(new byte[] { 0x40, 0x49, 0x0F, 0xDB }, _harness.Client.ReadHoldingRegistersRaw(100, 2));

            _harness.Client.WriteMultipleHoldingRegistersRaw(100, new byte[] { 0x40, 0x00, 0x00, 0x00 });

            Assert.Equal(new byte[] { 0x40, 0x00, 0x00, 0x00 }, _harness.Client.ReadHoldingRegistersRaw(100, 2));
        }

        [Fact]
        public void StopListeningWhenDisabled()
        {
            _sut.CreateTestContext().Build();

            _sut.ServerEnabled = false;

            Assert.False(_sut.IsListening);
        }

        [Fact]
        public void WalkASingleDiscreteInputAlong()
        {
            _sut.CreateTestContext().Build();

            _sut.FireTimer(block => block.OnTick());

            Assert.Equal(new[] { false, true, false }, _harness.Client.ReadDiscreteInputs(0, 3));

            _sut.FireTimer(block => block.OnTick());

            Assert.Equal(new[] { false, false, true }, _harness.Client.ReadDiscreteInputs(0, 3));
        }
    }
}