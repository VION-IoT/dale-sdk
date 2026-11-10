using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     One data record of a telegram: what it measures, which value of it, and the value, with its bytes as
    ///     received.
    /// </summary>
    /// <remarks>
    ///     A record carries at most one of <see cref="Number" />, <see cref="Timestamp" /> and <see cref="Text" />,
    ///     and none when the meter reported an error for it or its data cannot be read as a value.
    /// </remarks>
    [PublicApi]
    public sealed class MBusRecord
    {
        /// <summary>What the record measures, or <c>null</c> when its value information is outside the decoder's table.</summary>
        public MBusQuantity? Quantity { get; }

        /// <summary>Which value of the quantity the record carries.</summary>
        public MBusFunction Function { get; }

        /// <summary>The storage number: 0 for the current value, higher for stored ones such as a due-day value.</summary>
        public long Storage { get; }

        /// <summary>The tariff number, 0 when the record belongs to no tariff.</summary>
        public int Tariff { get; }

        /// <summary>The subunit (device unit) number, which a meter uses for phases, channels or registers.</summary>
        public int Subunit { get; }

        /// <summary>
        ///     The value as a number, scaled into the unit <see cref="Quantity" /> is reported in; the raw number when
        ///     the record has no quantity.
        /// </summary>
        public double? Number { get; }

        /// <summary>The value as a date, or a date and time, in the meter's own time.</summary>
        public DateTime? Timestamp { get; }

        /// <summary>The value as text.</summary>
        public string? Text { get; }

        /// <summary>The data information block as received: the DIF and its DIFEs.</summary>
        public IReadOnlyList<byte> Dib { get; }

        /// <summary>The value information block as received: the VIF and its VIFEs.</summary>
        public IReadOnlyList<byte> Vib { get; }

        /// <summary>The data bytes as received.</summary>
        public IReadOnlyList<byte> Data { get; }

        /// <summary>
        ///     Whether a value information extension changes what the record means, so that it is not the plain value
        ///     of its quantity and is reached only by its code.
        /// </summary>
        internal bool HasMeaningChangingExtension { get; }

        /// <summary>
        ///     The data and value information blocks together: what a selector by code names, and what makes one record
        ///     shadow another.
        /// </summary>
        internal byte[] Code { get; }

        internal MBusRecord(MBusQuantity? quantity,
                            MBusFunction function,
                            long storage,
                            int tariff,
                            int subunit,
                            double? number,
                            DateTime? timestamp,
                            string? text,
                            byte[] dib,
                            byte[] vib,
                            byte[] data,
                            bool hasMeaningChangingExtension)
        {
            Quantity = quantity;
            Function = function;
            Storage = storage;
            Tariff = tariff;
            Subunit = subunit;
            Number = number;
            Timestamp = timestamp;
            Text = text;
            Dib = Array.AsReadOnly(dib);
            Vib = Array.AsReadOnly(vib);
            Data = Array.AsReadOnly(data);
            HasMeaningChangingExtension = hasMeaningChangingExtension;
            Code = new byte[dib.Length + vib.Length];
            Buffer.BlockCopy(dib, 0, Code, 0, dib.Length);
            Buffer.BlockCopy(vib, 0, Code, dib.Length, vib.Length);
        }
    }
}