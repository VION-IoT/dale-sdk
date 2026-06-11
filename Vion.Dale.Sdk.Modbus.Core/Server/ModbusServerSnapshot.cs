namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     <see cref="IModbusServerSnapshot" /> composing one accessor per register area.
    /// </summary>
    public sealed class ModbusServerSnapshot : IModbusServerSnapshot
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusServerSnapshot" /> class.
        /// </summary>
        /// <param name="holdingRegisters">The holding register accessor.</param>
        /// <param name="inputRegisters">The input register accessor.</param>
        /// <param name="coils">The coil accessor.</param>
        /// <param name="discreteInputs">The discrete input accessor.</param>
        public ModbusServerSnapshot(IModbusRegisterAccessor holdingRegisters, IModbusRegisterAccessor inputRegisters, IModbusBitAccessor coils, IModbusBitAccessor discreteInputs)
        {
            HoldingRegisters = holdingRegisters;
            InputRegisters = inputRegisters;
            Coils = coils;
            DiscreteInputs = discreteInputs;
        }

        /// <inheritdoc />
        public IModbusRegisterAccessor HoldingRegisters { get; }

        /// <inheritdoc />
        public IModbusRegisterAccessor InputRegisters { get; }

        /// <inheritdoc />
        public IModbusBitAccessor Coils { get; }

        /// <inheritdoc />
        public IModbusBitAccessor DiscreteInputs { get; }
    }
}
