using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Diagnostics
{
    /// <summary>
    ///     The client's verdict on the link to its device, derived from the last transaction that reached the wire.
    /// </summary>
    [PublicApi]
    public enum ModbusLinkState
    {
        /// <summary>No transaction has reached the wire yet.</summary>
        Unknown,

        /// <summary>The device last answered — with data or with a Modbus exception code.</summary>
        Online,

        /// <summary>The last attempt to reach the device timed out, failed on the transport, or returned an invalid response.</summary>
        Faulted,
    }
}