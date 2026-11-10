using System;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Which quantities each <see cref="MBusUnit" /> applies to, how far it is from the unit a quantity is reported
    ///     in, and the symbols a description prints.
    /// </summary>
    internal static class MBusUnits
    {
        /// <summary>
        ///     Whether <paramref name="unit" /> belongs to <paramref name="quantity" />'s family, and if so the power of ten
        ///     from the reported unit to it: a kWh is 10³ of the Wh energy is reported in.
        /// </summary>
        public static bool TryGetExponent(MBusQuantity quantity, MBusUnit unit, out int exponent)
        {
            (MBusQuantity[] Quantities, int Exponent) entry = unit switch
            {
                MBusUnit.WattHour => ([MBusQuantity.Energy], 0),
                MBusUnit.KilowattHour => ([MBusQuantity.Energy], 3),
                MBusUnit.MegawattHour => ([MBusQuantity.Energy], 6),
                MBusUnit.VarHour => ([MBusQuantity.ReactiveEnergy], 0),
                MBusUnit.KilovarHour => ([MBusQuantity.ReactiveEnergy], 3),
                MBusUnit.CubicMeter => ([MBusQuantity.Volume], 0),
                MBusUnit.Liter => ([MBusQuantity.Volume], -3),
                MBusUnit.Watt => ([MBusQuantity.Power], 0),
                MBusUnit.Kilowatt => ([MBusQuantity.Power], 3),
                MBusUnit.Var => ([MBusQuantity.ReactivePower], 0),
                MBusUnit.Kilovar => ([MBusQuantity.ReactivePower], 3),
                MBusUnit.VoltAmpere => ([MBusQuantity.ApparentPower], 0),
                MBusUnit.KilovoltAmpere => ([MBusQuantity.ApparentPower], 3),
                MBusUnit.CubicMeterPerHour => ([MBusQuantity.VolumeFlow], 0),
                MBusUnit.LiterPerHour => ([MBusQuantity.VolumeFlow], -3),
                MBusUnit.Volt => ([MBusQuantity.Voltage], 0),
                MBusUnit.Ampere => ([MBusQuantity.Current], 0),
                MBusUnit.Hertz => ([MBusQuantity.Frequency], 0),
                MBusUnit.Hour => ([MBusQuantity.OperatingTime], 0),
                MBusUnit.DegreeCelsius => ([MBusQuantity.FlowTemperature, MBusQuantity.ReturnTemperature], 0),
                MBusUnit.Kelvin => ([MBusQuantity.TemperatureDifference], 0),
                _ => ([], 0),
            };

            exponent = entry.Exponent;
            return Array.IndexOf(entry.Quantities, quantity) >= 0;
        }

        /// <summary>The symbol of the unit <paramref name="quantity" /> is reported in, empty for a dimensionless one.</summary>
        public static string ReportedSymbol(MBusQuantity quantity)
        {
            return quantity switch
            {
                MBusQuantity.Energy => "Wh",
                MBusQuantity.ReactiveEnergy => "varh",
                MBusQuantity.Volume => "m³",
                MBusQuantity.Power => "W",
                MBusQuantity.ReactivePower => "var",
                MBusQuantity.ApparentPower => "VA",
                MBusQuantity.VolumeFlow => "m³/h",
                MBusQuantity.FlowTemperature or MBusQuantity.ReturnTemperature => "°C",
                MBusQuantity.TemperatureDifference => "K",
                MBusQuantity.Voltage => "V",
                MBusQuantity.Current => "A",
                MBusQuantity.Frequency => "Hz",
                MBusQuantity.OperatingTime => "h",
                _ => string.Empty,
            };
        }
    }
}