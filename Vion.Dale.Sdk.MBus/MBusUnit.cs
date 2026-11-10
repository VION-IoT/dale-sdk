using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     A unit <see cref="MBusReadout.Value" /> converts a record's value into. Each unit belongs to one quantity's
    ///     family, and only values of that family convert into it.
    /// </summary>
    [PublicApi]
    public enum MBusUnit
    {
        /// <summary>Wh, for energy.</summary>
        WattHour,

        /// <summary>kWh, for energy.</summary>
        KilowattHour,

        /// <summary>MWh, for energy.</summary>
        MegawattHour,

        /// <summary>varh, for reactive energy.</summary>
        VarHour,

        /// <summary>kvarh, for reactive energy.</summary>
        KilovarHour,

        /// <summary>m³, for volume.</summary>
        CubicMeter,

        /// <summary>L, for volume.</summary>
        Liter,

        /// <summary>W, for power.</summary>
        Watt,

        /// <summary>kW, for power.</summary>
        Kilowatt,

        /// <summary>var, for reactive power.</summary>
        Var,

        /// <summary>kvar, for reactive power.</summary>
        Kilovar,

        /// <summary>VA, for apparent power.</summary>
        VoltAmpere,

        /// <summary>kVA, for apparent power.</summary>
        KilovoltAmpere,

        /// <summary>m³/h, for volume flow.</summary>
        CubicMeterPerHour,

        /// <summary>L/h, for volume flow.</summary>
        LiterPerHour,

        /// <summary>V, for voltage.</summary>
        Volt,

        /// <summary>A, for current.</summary>
        Ampere,

        /// <summary>Hz, for frequency.</summary>
        Hertz,

        /// <summary>h, for operating time.</summary>
        Hour,

        /// <summary>°C, for flow and return temperature, never for a temperature difference.</summary>
        DegreeCelsius,

        /// <summary>K, for a temperature difference, never for an absolute temperature.</summary>
        Kelvin,
    }
}