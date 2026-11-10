using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Names the record a logic block wants from a readout: by meaning with <see cref="Of" />, or by its exact bytes
    ///     with <see cref="ByCode" />.
    /// </summary>
    /// <remarks>
    ///     Declare selectors once, as <c>static readonly</c> fields, and the block reads like the meter's datasheet. A
    ///     selector prints as the C# that constructs it, which is what <see cref="MBusReadout.Describe" /> hands an
    ///     author to paste.
    /// </remarks>
    /// <example>
    ///     <code>
    /// private static readonly MBusSelector ImportEnergy = MBusSelector.Of(MBusQuantity.Energy);
    /// private static readonly MBusSelector CurrentL1 = MBusSelector.Of(MBusQuantity.Current, subunit: 1);
    /// private static readonly MBusSelector ExportEnergy = MBusSelector.ByCode("84 C0 80 40 05");
    /// </code>
    /// </example>
    [PublicApi]
    public sealed class MBusSelector : IEquatable<MBusSelector>
    {
        private readonly byte[]? _code;

        private readonly MBusFunction _function;

        private readonly MBusQuantity _quantity;

        private readonly long _storage;

        private readonly int _subunit;

        private readonly int _tariff;

        /// <summary>The quantity a selector by meaning names, or <c>null</c> for a selector by code.</summary>
        internal MBusQuantity? Quantity
        {
            get => _code == null ? _quantity : null;
        }

        private MBusSelector(MBusQuantity quantity, long storage, int tariff, int subunit, MBusFunction function)
        {
            _quantity = quantity;
            _storage = storage;
            _tariff = tariff;
            _subunit = subunit;
            _function = function;
        }

        private MBusSelector(byte[] code)
        {
            _code = code;
        }

        /// <inheritdoc />
        public bool Equals(MBusSelector? other)
        {
            if (other is null)
            {
                return false;
            }

            if (_code != null || other._code != null)
            {
                return _code != null && other._code != null && _code.SequenceEqual(other._code);
            }

            return _quantity == other._quantity && _storage == other._storage && _tariff == other._tariff && _subunit == other._subunit && _function == other._function;
        }

        /// <summary>
        ///     Names a record by meaning; the defaults name the current value. A record whose value information carries
        ///     an extension that changes its meaning — a limit, a rate, a future value — is never matched here and is
        ///     reached with <see cref="ByCode" />.
        /// </summary>
        /// <param name="quantity">What the record measures.</param>
        /// <param name="storage">The storage number: 0 for the current value, higher for stored ones.</param>
        /// <param name="tariff">The tariff number, 0 for none.</param>
        /// <param name="subunit">The subunit number, which a meter uses for phases, channels or registers.</param>
        /// <param name="function">
        ///     Which value of the quantity: the current one, a maximum, a minimum, or the value during an
        ///     error.
        /// </param>
        /// <returns>The selector.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     <paramref name="storage" />, <paramref name="tariff" /> or
        ///     <paramref name="subunit" /> is negative.
        /// </exception>
        public static MBusSelector Of(MBusQuantity quantity, long storage = 0, int tariff = 0, int subunit = 0, MBusFunction function = MBusFunction.Instantaneous)
        {
            if (storage < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(storage), storage, "A storage number is not negative.");
            }

            if (tariff < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tariff), tariff, "A tariff number is not negative.");
            }

            if (subunit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(subunit), subunit, "A subunit number is not negative.");
            }

            return new MBusSelector(quantity, storage, tariff, subunit, function);
        }

        /// <summary>
        ///     Names a record by its exact data and value information bytes, as a datasheet lists them: the DIF and its
        ///     DIFEs, then the VIF and its VIFEs.
        /// </summary>
        /// <param name="code">The bytes as hexadecimal text, such as <c>"84 80 C0 40 05"</c>.</param>
        /// <returns>The selector.</returns>
        /// <exception cref="FormatException"><paramref name="code" /> is not hexadecimal.</exception>
        /// <exception cref="ArgumentException"><paramref name="code" /> holds no byte.</exception>
        public static MBusSelector ByCode(string code)
        {
            var bytes = MBusHex.Parse(code);
            if (bytes.Length == 0)
            {
                throw new ArgumentException("A record code holds at least one byte.", nameof(code));
            }

            return new MBusSelector(bytes);
        }

        /// <summary>The C# that constructs this selector.</summary>
        public override string ToString()
        {
            if (_code != null)
            {
                return $"MBusSelector.ByCode(\"{MBusHex.Format(_code)}\")";
            }

            var builder = new StringBuilder("MBusSelector.Of(MBusQuantity.").Append(_quantity);
            if (_storage != 0)
            {
                builder.Append(", storage: ").Append(_storage.ToString(CultureInfo.InvariantCulture));
            }

            if (_tariff != 0)
            {
                builder.Append(", tariff: ").Append(_tariff.ToString(CultureInfo.InvariantCulture));
            }

            if (_subunit != 0)
            {
                builder.Append(", subunit: ").Append(_subunit.ToString(CultureInfo.InvariantCulture));
            }

            if (_function != MBusFunction.Instantaneous)
            {
                builder.Append(", function: MBusFunction.").Append(_function);
            }

            return builder.Append(')').ToString();
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return Equals(obj as MBusSelector);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        internal bool Matches(MBusRecord record)
        {
            if (_code != null)
            {
                return ((ReadOnlySpan<byte>)record.Code).SequenceEqual(_code);
            }

            return record.Quantity == _quantity && !record.HasMeaningChangingExtension && record.Storage == _storage && record.Tariff == _tariff && record.Subunit == _subunit &&
                   record.Function == _function;
        }

        internal static MBusSelector CodeOf(MBusRecord record)
        {
            return new MBusSelector(record.Code);
        }
    }
}