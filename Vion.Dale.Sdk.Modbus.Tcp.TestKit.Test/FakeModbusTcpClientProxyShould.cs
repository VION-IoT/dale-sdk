using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    [TestClass]
    public class FakeModbusTcpClientProxyShould
    {
        [TestMethod]
        public void DecodeHoldingRegisterReadEndToEnd_WithMsbToLsb_AndMswToLsw_WordOrder()
        {
            // Architecture proof: bytes 0x12 0x34 at addr 40000 and 0x56 0x78 at addr 40001, read as
            // one UInt32 with MsbToLsb byte order + MswToLsw word order, should decode to 0x12345678.
            // All the byte/word-order math runs in real SDK code (ModbusTcpClientWrapper +
            // ModbusDataConverter); the fake only stores raw bytes.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: new byte[] { 0x12, 0x34, 0x56, 0x78 });

            var loggerMock = new Mock<ILogger>();
            var sut = new SampleModbusTcpBlock(harness.Client, loggerMock.Object);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.AreEqual(0x12345678u, sut.Power, "End-to-end UInt32 decode of holding-register bytes should match expected wire format.");
            Assert.IsNull(sut.LastReadError, "No error path should have fired for a successful read.");
        }
    }
}
