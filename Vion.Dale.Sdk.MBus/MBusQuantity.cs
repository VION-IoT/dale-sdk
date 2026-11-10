using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     What a data record measures, as its value information names it. A record's
    ///     <see cref="MBusRecord.Number" /> is reported in the unit each member names, whichever coding of the
    ///     quantity the meter sent.
    /// </summary>
    /// <remarks>
    ///     A record whose value information is outside the decoder's table carries no quantity and is reached by
    ///     its code.
    /// </remarks>
    [PublicApi]
    public enum MBusQuantity
    {
        /// <summary>Active energy, reported in Wh; codings in J are converted.</summary>
        Energy,

        /// <summary>Reactive energy, reported in varh.</summary>
        ReactiveEnergy,

        /// <summary>Volume, reported in m³.</summary>
        Volume,

        /// <summary>Active power, reported in W; codings in J/h are converted.</summary>
        Power,

        /// <summary>Reactive power, reported in var.</summary>
        ReactivePower,

        /// <summary>Apparent power, reported in VA.</summary>
        ApparentPower,

        /// <summary>Volume flow, reported in m³/h; codings per minute and per second are converted.</summary>
        VolumeFlow,

        /// <summary>Flow (inlet) temperature, reported in °C.</summary>
        FlowTemperature,

        /// <summary>Return (outlet) temperature, reported in °C.</summary>
        ReturnTemperature,

        /// <summary>Temperature difference, reported in K.</summary>
        TemperatureDifference,

        /// <summary>Voltage, reported in V.</summary>
        Voltage,

        /// <summary>Current, reported in A.</summary>
        Current,

        /// <summary>Frequency, reported in Hz.</summary>
        Frequency,

        /// <summary>Operating time, reported in h; codings in seconds, minutes and days are converted.</summary>
        OperatingTime,

        /// <summary>A number without a unit, such as a power factor.</summary>
        Dimensionless,

        /// <summary>A date, or a date and time, read from <see cref="MBusRecord.Timestamp" />.</summary>
        DateTime,
    }
}