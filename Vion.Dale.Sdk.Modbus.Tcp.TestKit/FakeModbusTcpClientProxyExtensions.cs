using System.Linq;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     Verification helpers over the fake proxy's recorded histories. Sugar for the most common
    ///     assertion patterns; tests that need finer-grained matching can inspect <c>ReadHistory</c>,
    ///     <c>WriteHistory</c>, and <c>ConnectionHistory</c> directly. All parameters except the
    ///     receiver are optional — null = "any value matches".
    /// </summary>
    [PublicApi]
    public static class FakeModbusTcpClientProxyExtensions
    {
        /// <summary>
        ///     Assert that a read operation matching the given filters was sent through the fake.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="unitId">Unit identifier filter, or null to match any unit.</param>
        /// <param name="startingAddress">Starting-address filter, or null to match any address.</param>
        /// <param name="quantity">Register/coil quantity filter, or null to match any quantity.</param>
        /// <param name="kind">Modbus function filter (HoldingRegisters / InputRegisters / Coils / DiscreteInputs), or null to match any.</param>
        /// <param name="times">Expected match count via Moq's <see cref="Times" />, or null for <see cref="Times.Once" />.</param>
        public static void VerifyReadSent(this FakeModbusTcpClientProxy proxy,
                                          int? unitId = null,
                                          ushort? startingAddress = null,
                                          ushort? quantity = null,
                                          ReadEventKind? kind = null,
                                          Times? times = null)
        {
            var matches = proxy.ReadHistory.Where(r => (unitId == null || r.UnitId == unitId.Value) &&
                                                       (startingAddress == null || r.Address == startingAddress.Value) &&
                                                       (quantity == null || r.Quantity == quantity.Value) &&
                                                       (kind == null || r.Kind == kind.Value))
                              .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count, $"Read verification failed (unitId={unitId}, address={startingAddress}, quantity={quantity}, kind={kind}).");
        }

        /// <summary>
        ///     Assert that a write operation matching the given filters was sent through the fake.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="unitId">Unit identifier filter, or null to match any unit.</param>
        /// <param name="address">Target address filter, or null to match any address.</param>
        /// <param name="expectedBytes">Expected wire-format payload (raw bytes), or null to skip byte verification. Useful for wire-format regression tests.</param>
        /// <param name="kind">Write-function filter (SingleRegister / MultipleRegisters / SingleCoil / MultipleCoils), or null to match any.</param>
        /// <param name="times">Expected match count via Moq's <see cref="Times" />, or null for <see cref="Times.Once" />.</param>
        public static void VerifyWriteSent(this FakeModbusTcpClientProxy proxy,
                                           int? unitId = null,
                                           ushort? address = null,
                                           byte[]? expectedBytes = null,
                                           WriteEventKind? kind = null,
                                           Times? times = null)
        {
            var matches = proxy.WriteHistory.Where(w => (unitId == null || w.UnitId == unitId.Value) &&
                                                        (address == null || w.Address == address.Value) &&
                                                        (kind == null || w.Kind == kind.Value) &&
                                                        (expectedBytes == null || w.Bytes.SequenceEqual(expectedBytes)))
                               .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count, $"Write verification failed (unitId={unitId}, address={address}, kind={kind}, expectedBytes={(expectedBytes == null ? "(any)" : string.Join(" ", expectedBytes.Select(b => b.ToString("X2"))))}).");
        }

        /// <summary>
        ///     Assert that a <c>ConnectAsync</c> attempt matching the given filters was made.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="ipAddress">IP-address filter (as a string), or null to match any IP.</param>
        /// <param name="port">Port filter, or null to match any port.</param>
        /// <param name="times">Expected match count via Moq's <see cref="Times" />, or null for <see cref="Times.Once" />.</param>
        public static void VerifyConnectAttempted(this FakeModbusTcpClientProxy proxy,
                                                  string? ipAddress = null,
                                                  int? port = null,
                                                  Times? times = null)
        {
            var matches = proxy.ConnectionHistory.Where(e => e.Kind == ConnectionEventKind.Connect &&
                                                              (ipAddress == null || e.IpAddress?.ToString() == ipAddress) &&
                                                              (port == null || e.Port == port.Value))
                               .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count, $"Connect verification failed (ipAddress={ipAddress ?? "(any)"}, port={port}).");
        }

        /// <summary>
        ///     Assert that <c>Disconnect</c> was called on the proxy the expected number of times.
        /// </summary>
        public static void VerifyDisconnectCalled(this FakeModbusTcpClientProxy proxy, Times? times = null)
        {
            var count = proxy.ConnectionHistory.Count(e => e.Kind == ConnectionEventKind.Disconnect);
            (times ?? Times.Once()).AssertCount(count, "Disconnect verification failed.");
        }
    }
}
