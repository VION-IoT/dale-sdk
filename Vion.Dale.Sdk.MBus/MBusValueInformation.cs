using System.Collections.Generic;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     What a value information block says about its record: the quantity, how to scale the data into the unit the
    ///     quantity is reported in, and whether an extension changes the record's meaning or reports an error.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The table is EN 13757-3's primary VIF table, the FDh extension entries for voltage, current and
    ///         dimensionless values, and the four FBh extension entries the target meters send. A primary VIF whose
    ///         quantity is not in <see cref="MBusQuantity" /> decodes with no quantity, as everything outside the table
    ///         does.
    ///     </para>
    ///     <para>
    ///         Of the extensions that follow the true VIF, the multiplicative (E111 0nnn, E111 1101) and additive
    ///         (E111 10nn) corrections are folded into the scale, a record error code (E00x xxxx) leaves the record
    ///         without a value, and every other extension changes what the record means: a limit, a rate, a future
    ///         value, an accumulation of one sign only.
    ///     </para>
    /// </remarks>
    internal readonly struct MBusValueInformation
    {
        private MBusValueInformation(MBusQuantity? quantity,
                                     int vifExponent,
                                     int correctionExponent,
                                     double factor,
                                     int? additiveExponent,
                                     bool hasMeaningChangingExtension,
                                     bool hasErrorCode,
                                     TimePointKind timePoint)
        {
            Quantity = quantity;
            VifExponent = vifExponent;
            CorrectionExponent = correctionExponent;
            Factor = factor;
            AdditiveExponent = additiveExponent;
            HasMeaningChangingExtension = hasMeaningChangingExtension;
            HasErrorCode = hasErrorCode;
            TimePoint = timePoint;
        }

        public MBusQuantity? Quantity { get; }

        /// <summary>The power of ten the VIF scales the data by into the reported unit.</summary>
        public int VifExponent { get; }

        /// <summary>The power of ten the multiplicative corrections add.</summary>
        public int CorrectionExponent { get; }

        /// <summary>A further factor for codings that are not a power of ten from the reported unit, such as J to Wh.</summary>
        public double Factor { get; }

        /// <summary>The power of ten of an additive correction, in the VIF's own unit, or <c>null</c> when there is none.</summary>
        public int? AdditiveExponent { get; }

        public bool HasMeaningChangingExtension { get; }

        public bool HasErrorCode { get; }

        public TimePointKind TimePoint { get; }

        public static MBusValueInformation Of(IReadOnlyList<byte> vib, int extensionsStart)
        {
            var primary = vib[0] & 0x7F;
            var (quantity, exponent, factor, timePoint) = vib[0] switch
            {
                0xFD => Fd(vib[1] & 0x7F),
                0xFB => Fb(vib[1] & 0x7F),
                _ when primary is 0x7C or 0x7E or 0x7F => (null, 0, 1.0, TimePointKind.None),
                _ => Primary(primary),
            };

            if (vib[0] is 0x7F or 0xFF)
            {
                // A manufacturer-specific VIF: its extensions are the manufacturer's too, so none is read as an error code.
                return new MBusValueInformation(null, 0, 0, 1.0, null, false, false, TimePointKind.None);
            }

            var correction = 0;
            int? additive = null;
            var meaningChanging = false;
            var errorCode = false;
            for (var index = extensionsStart; index < vib.Count; index++)
            {
                var extension = vib[index] & 0x7F;
                switch (extension)
                {
                    case <= 0x1F:
                        errorCode = true;
                        break;
                    case >= 0x70 and <= 0x77:
                        correction += (extension & 0x07) - 6;
                        break;
                    case >= 0x78 and <= 0x7B:
                        additive = (extension & 0x03) - 3;
                        break;
                    case 0x7D:
                        correction += 3;
                        break;
                    case 0x7F:
                        // Every extension after this one, and the data, are the manufacturer's.
                        return new MBusValueInformation(quantity, exponent, correction, factor, additive, true, errorCode, timePoint);
                    default:
                        meaningChanging = true;
                        break;
                }
            }

            return new MBusValueInformation(quantity, exponent, correction, factor, additive, meaningChanging, errorCode, timePoint);
        }

        private static (MBusQuantity?, int, double, TimePointKind) Primary(int vif)
        {
            return vif switch
            {
                <= 0x07 => (MBusQuantity.Energy, (vif & 0x07) - 3, 1.0, TimePointKind.None),
                <= 0x0F => (MBusQuantity.Energy, vif & 0x07, 1.0 / 3600, TimePointKind.None),
                <= 0x17 => (MBusQuantity.Volume, (vif & 0x07) - 6, 1.0, TimePointKind.None),
                >= 0x24 and <= 0x27 => (MBusQuantity.OperatingTime, 0, HoursPer(vif & 0x03), TimePointKind.None),
                >= 0x28 and <= 0x2F => (MBusQuantity.Power, (vif & 0x07) - 3, 1.0, TimePointKind.None),
                >= 0x30 and <= 0x37 => (MBusQuantity.Power, vif & 0x07, 1.0 / 3600, TimePointKind.None),
                >= 0x38 and <= 0x3F => (MBusQuantity.VolumeFlow, (vif & 0x07) - 6, 1.0, TimePointKind.None),
                >= 0x40 and <= 0x47 => (MBusQuantity.VolumeFlow, (vif & 0x07) - 7, 60.0, TimePointKind.None),
                >= 0x48 and <= 0x4F => (MBusQuantity.VolumeFlow, (vif & 0x07) - 9, 3600.0, TimePointKind.None),
                >= 0x58 and <= 0x5B => (MBusQuantity.FlowTemperature, (vif & 0x03) - 3, 1.0, TimePointKind.None),
                >= 0x5C and <= 0x5F => (MBusQuantity.ReturnTemperature, (vif & 0x03) - 3, 1.0, TimePointKind.None),
                >= 0x60 and <= 0x63 => (MBusQuantity.TemperatureDifference, (vif & 0x03) - 3, 1.0, TimePointKind.None),
                0x6C => (MBusQuantity.DateTime, 0, 1.0, TimePointKind.Date),
                0x6D => (MBusQuantity.DateTime, 0, 1.0, TimePointKind.DateAndTime),
                _ => (null, 0, 1.0, TimePointKind.None),
            };
        }

        private static (MBusQuantity?, int, double, TimePointKind) Fd(int code)
        {
            return code switch
            {
                0x3A => (MBusQuantity.Dimensionless, 0, 1.0, TimePointKind.None),
                >= 0x40 and <= 0x4F => (MBusQuantity.Voltage, (code & 0x0F) - 9, 1.0, TimePointKind.None),
                >= 0x50 and <= 0x5F => (MBusQuantity.Current, (code & 0x0F) - 12, 1.0, TimePointKind.None),
                _ => (null, 0, 1.0, TimePointKind.None),
            };
        }

        /*
         * Only the four codes the target meters send, each pinned to a captured telegram: the FBh table's newer
         * entries are not in the open-source decoders this table was cross-checked against, so a neighbouring code
         * stays outside the table rather than being decoded by an unverified rule. The reported units are var, varh
         * and VA, three powers of ten above the k-units the codes name.
         */
        private static (MBusQuantity?, int, double, TimePointKind) Fb(int code)
        {
            return code switch
            {
                0x02 => (MBusQuantity.ReactiveEnergy, 3, 1.0, TimePointKind.None),
                0x17 => (MBusQuantity.ReactivePower, 3, 1.0, TimePointKind.None),
                0x2E => (MBusQuantity.Frequency, -1, 1.0, TimePointKind.None),
                0x37 => (MBusQuantity.ApparentPower, 3, 1.0, TimePointKind.None),
                _ => (null, 0, 1.0, TimePointKind.None),
            };
        }

        private static double HoursPer(int durationUnit)
        {
            return durationUnit switch
            {
                0 => 1.0 / 3600,
                1 => 1.0 / 60,
                2 => 1.0,
                _ => 24.0,
            };
        }
    }

    internal enum TimePointKind
    {
        None,

        Date,

        DateAndTime,
    }
}
