using Vion.Dale.Sdk.Core;

namespace Vion.Examples.ModbusTcp.LogicBlocks
{
    /// <summary>
    ///     Which Modbus data area (and therefore which function code) a read targets.
    /// </summary>
    public enum ReadFunction
    {
        [EnumLabel("FC1 — Coils")]
        Coils,

        [EnumLabel("FC2 — Discrete inputs")]
        DiscreteInputs,

        [EnumLabel("FC3 — Holding registers")]
        HoldingRegisters,

        [EnumLabel("FC4 — Input registers")]
        InputRegisters,
    }

    /// <summary>
    ///     Which write function code to issue.
    /// </summary>
    public enum WriteFunction
    {
        [EnumLabel("FC5 — Write single coil")]
        SingleCoil,

        [EnumLabel("FC15 — Write multiple coils")]
        MultipleCoils,

        [EnumLabel("FC6 — Write single register")]
        SingleRegister,

        [EnumLabel("FC16 — Write multiple registers")]
        MultipleRegisters,
    }

    /// <summary>
    ///     How to interpret a run of registers. <see cref="None" /> shows the per-register columns only;
    ///     every other member spans <c>1</c>, <c>2</c>, or <c>4</c> registers per value.
    /// </summary>
    public enum RegisterFieldType
    {
        [EnumLabel("None (raw registers only)")]
        None,

        [EnumLabel("UInt16 (1 register)")]
        UInt16,

        [EnumLabel("Int16 (1 register)")]
        Int16,

        [EnumLabel("UInt32 (2 registers)")]
        UInt32,

        [EnumLabel("Int32 (2 registers)")]
        Int32,

        [EnumLabel("Float32 (2 registers)")]
        Float32,

        [EnumLabel("UInt64 (4 registers)")]
        UInt64,

        [EnumLabel("Int64 (4 registers)")]
        Int64,

        [EnumLabel("Float64 (4 registers)")]
        Float64,

        [EnumLabel("String (ASCII / UTF)")]
        String,
    }

    /// <summary>
    ///     Field types a watch slot can chart. Numeric only — a watched value has to be plottable.
    /// </summary>
    public enum WatchFieldType
    {
        [EnumLabel("UInt16 (1 register)")]
        UInt16,

        [EnumLabel("Int16 (1 register)")]
        Int16,

        [EnumLabel("UInt32 (2 registers)")]
        UInt32,

        [EnumLabel("Int32 (2 registers)")]
        Int32,

        [EnumLabel("Float32 (2 registers)")]
        Float32,

        [EnumLabel("UInt64 (4 registers)")]
        UInt64,

        [EnumLabel("Int64 (4 registers)")]
        Int64,

        [EnumLabel("Float64 (4 registers)")]
        Float64,
    }

    /// <summary>
    ///     Which register area a watch slot polls. Watches are read-only, so coils are out of scope.
    /// </summary>
    public enum WatchFunction
    {
        [EnumLabel("FC3 — Holding registers")]
        HoldingRegisters,

        [EnumLabel("FC4 — Input registers")]
        InputRegisters,
    }

    /// <summary>
    ///     The one-glance verdict on the polling connection, folded from the two the SDK keeps: the link's
    ///     view of the device and the socket's own state. Nothing here is counted or inferred locally — a
    ///     failed parse or a bad address shows up in the error strings, not in this pill, because neither
    ///     says anything about the device.
    /// </summary>
    /// <remarks>
    ///     <see cref="BackingOff" /> outranks <see cref="Faulted" />: both mean the device is not answering,
    ///     but backing off additionally means the client has stopped trying for now and requests are failing
    ///     fast. That is the state an operator most needs to recognise, because it is the one where the
    ///     symptom (instant failures) looks least like the cause (an unreachable endpoint).
    /// </remarks>
    public enum LinkStatus
    {
        [Severity(StatusSeverity.Neutral)]
        [EnumLabel("Unknown")]
        Unknown,

        [Severity(StatusSeverity.Success)]
        [EnumLabel("Online")]
        Online,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Faulted")]
        Faulted,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Backing off")]
        BackingOff,
    }
}