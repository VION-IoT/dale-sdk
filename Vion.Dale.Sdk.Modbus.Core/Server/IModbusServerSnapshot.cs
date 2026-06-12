using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     A consistent view of all four register areas of a hosted Modbus server, valid for the duration of one
    ///     synchronization callback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The server lock is held for the whole callback and released afterwards: reads and writes across all
    ///         areas within one callback are atomic with respect to client requests, so read-modify-publish patterns
    ///         (e.g. echoing a master-written heartbeat into a feedback register) need no further coordination.
    ///     </para>
    ///     <para>
    ///         Only valid inside the callback that provided it — do not capture the snapshot or its accessors.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface IModbusServerSnapshot
    {
        /// <summary>
        ///     The holding registers (client-writable setpoints; the server may seed defaults or echo feedback).
        /// </summary>
        IModbusRegisterAccessor HoldingRegisters { get; }

        /// <summary>
        ///     The input registers (server-published telemetry; read-only for clients).
        /// </summary>
        IModbusRegisterAccessor InputRegisters { get; }

        /// <summary>
        ///     The coils (client-writable commands; the server may reset consumed command bits).
        /// </summary>
        IModbusBitAccessor Coils { get; }

        /// <summary>
        ///     The discrete inputs (server-published flags; read-only for clients).
        /// </summary>
        IModbusBitAccessor DiscreteInputs { get; }
    }
}