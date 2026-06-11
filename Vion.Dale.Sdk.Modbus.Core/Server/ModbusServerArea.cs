using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     Identifies one of the four Modbus register areas a server serves.
    /// </summary>
    [PublicApi]
    public enum ModbusServerArea
    {
        /// <summary>
        ///     Holding registers (read/write for clients; function codes 3, 6, 16, 23).
        /// </summary>
        HoldingRegisters = 0,

        /// <summary>
        ///     Input registers (read-only for clients; function code 4).
        /// </summary>
        InputRegisters = 1,

        /// <summary>
        ///     Coils (read/write for clients; function codes 1, 5, 15).
        /// </summary>
        Coils = 2,

        /// <summary>
        ///     Discrete inputs (read-only for clients; function code 2).
        /// </summary>
        DiscreteInputs = 3,
    }
}