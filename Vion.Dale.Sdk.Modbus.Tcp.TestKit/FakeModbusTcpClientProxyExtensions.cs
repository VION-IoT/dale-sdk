using System.Linq;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     Verification extension methods for asserting Modbus TCP operations in tests.
    /// </summary>
    [PublicApi]
    public static class FakeModbusTcpClientProxyExtensions
    {
        /// <summary>
        ///     Assert that a Modbus read was sent.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="unitId">The expected unit identifier, or null for any.</param>
        /// <param name="startingAddress">The expected starting address, or null for any.</param>
        /// <param name="quantity">The expected register/coil quantity, or null for any.</param>
        /// <param name="kind">The expected Modbus function, or null for any.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyReadSent(this FakeModbusTcpClientProxy proxy,
                                          int? unitId = null,
                                          ushort? startingAddress = null,
                                          ushort? quantity = null,
                                          ReadEventKind? kind = null,
                                          Times? times = null)
        {
            var matches = proxy.ReadHistory
                               .Where(r => (unitId == null || r.UnitId == unitId.Value) && (startingAddress == null || r.Address == startingAddress.Value) &&
                                           (quantity == null || r.Quantity == quantity.Value) && (kind == null || r.Kind == kind.Value))
                               .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count, $"ModbusRead verification failed (unitId={unitId}, address={startingAddress}, quantity={quantity}, kind={kind}).");
        }

        /// <summary>
        ///     Assert that a Modbus write was sent.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="unitId">The expected unit identifier, or null for any.</param>
        /// <param name="address">The expected target address, or null for any.</param>
        /// <param name="expectedBytes">The expected wire-format payload (raw bytes), or null to skip byte verification.</param>
        /// <param name="kind">The expected write function, or null for any.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyWriteSent(this FakeModbusTcpClientProxy proxy,
                                           int? unitId = null,
                                           ushort? address = null,
                                           byte[]? expectedBytes = null,
                                           WriteEventKind? kind = null,
                                           Times? times = null)
        {
            var matches = proxy.WriteHistory
                               .Where(w => (unitId == null || w.UnitId == unitId.Value) && (address == null || w.Address == address.Value) &&
                                           (kind == null || w.Kind == kind.Value) && (expectedBytes == null || w.Bytes.SequenceEqual(expectedBytes)))
                               .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count,
                                                $"ModbusWrite verification failed (unitId={unitId}, address={address}, kind={kind}, expectedBytes={(expectedBytes == null ? "(any)" : string.Join(" ", expectedBytes.Select(b => b.ToString("X2"))))}).");
        }

        /// <summary>
        ///     Assert that a <c>ConnectAsync</c> attempt was made.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="ipAddress">The expected IP address (as a string), or null for any.</param>
        /// <param name="port">The expected port, or null for any.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyConnectAttempted(this FakeModbusTcpClientProxy proxy, string? ipAddress = null, int? port = null, Times? times = null)
        {
            var matches = proxy.ConnectionHistory
                               .Where(e => e.Kind == ConnectionEventKind.Connect && (ipAddress == null || e.IpAddress?.ToString() == ipAddress) &&
                                           (port == null || e.Port == port.Value))
                               .ToList();

            (times ?? Times.Once()).AssertCount(matches.Count, $"ConnectAttempted verification failed (ipAddress={ipAddress ?? "(any)"}, port={port}).");
        }

        /// <summary>
        ///     Assert that <c>Disconnect</c> was called on the proxy.
        /// </summary>
        /// <param name="proxy">The fake proxy to inspect.</param>
        /// <param name="times">The expected number of times, or null for once.</param>
        public static void VerifyDisconnectCalled(this FakeModbusTcpClientProxy proxy, Times? times = null)
        {
            var count = proxy.ConnectionHistory.Count(e => e.Kind == ConnectionEventKind.Disconnect);
            (times ?? Times.Once()).AssertCount(count, "Disconnect verification failed.");
        }
    }
}