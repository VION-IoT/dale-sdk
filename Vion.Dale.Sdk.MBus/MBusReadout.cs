using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Everything one read returned: the telegrams in the order the meter sent them, and their records joined in
    ///     that order.
    /// </summary>
    /// <remarks>
    ///     A readout is incomplete when its last telegram announces more records than were read: the provider stopped
    ///     before the meter did. Values from the telegrams that were not read are then missing, never zero. Fewer
    ///     telegrams than the read asked for means the provider stopped; as many as asked for means the meter sends
    ///     more than the read asked for.
    /// </remarks>
    [PublicApi]
    public sealed class MBusReadout
    {
        /// <summary>The header of the first telegram.</summary>
        public MBusHeader Header
        {
            get => Telegrams[0].Header;
        }

        /// <summary>The telegrams, in the order the meter sent them.</summary>
        public IReadOnlyList<MBusTelegram> Telegrams { get; }

        /// <summary>The records of every telegram, in telegram order.</summary>
        public IReadOnlyList<MBusRecord> Records { get; }

        /// <summary>Whether the last telegram announces no further records, so every record the meter sends was read.</summary>
        public bool IsComplete
        {
            get => !Telegrams[Telegrams.Count - 1].AnnouncesMoreRecords;
        }

        /// <summary>Called with each selector that finds nothing on a complete readout; the contract logs it.</summary>
        internal Action<MBusSelector>? OnMiss { get; set; }

        internal MBusReadout(IReadOnlyList<MBusTelegram> telegrams)
        {
            Telegrams = telegrams;
            Records = telegrams.SelectMany(telegram => telegram.Records).ToList().AsReadOnly();
        }

        /// <summary>
        ///     The first record, in telegram order, that <paramref name="selector" /> names, or <c>null</c> when none does.
        /// </summary>
        /// <param name="selector">The record to find.</param>
        /// <returns>The record, or <c>null</c>.</returns>
        public MBusRecord? Find(MBusSelector selector)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            var record = FirstMatch(selector);
            if (record == null && IsComplete)
            {
                OnMiss?.Invoke(selector);
            }

            return record;
        }

        /// <summary>
        ///     The value of the record <paramref name="selector" /> names, in <paramref name="unit" />; <c>null</c> when no
        ///     record matches or the record has no value. On a complete readout a miss is logged once per selector.
        /// </summary>
        /// <param name="selector">The record to read.</param>
        /// <param name="unit">The unit to convert the value into, one of its quantity's units.</param>
        /// <returns>The value, or <c>null</c>.</returns>
        /// <exception cref="ArgumentException">The record's value cannot be expressed in <paramref name="unit" />.</exception>
        public double? Value(MBusSelector selector, MBusUnit unit)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            // A selector by meaning fixes the quantity, so a wrong unit is refused whether or not this readout has the
            // record: the first replay of any capture finds the mistake.
            if (selector.Quantity is { } quantity && !MBusUnits.TryGetExponent(quantity, unit, out _))
            {
                throw UnitMismatch(selector, null, unit);
            }

            var record = Find(selector);
            if (record == null)
            {
                return null;
            }

            if (record.Quantity is not { } recordQuantity || !MBusUnits.TryGetExponent(recordQuantity, unit, out var exponent))
            {
                throw UnitMismatch(selector, record, unit);
            }

            return record.Number is { } number ? MBusScale.Power(number, -exponent) : null;
        }

        /// <summary>
        ///     A selector that finds <paramref name="record" /> in this readout: by meaning where that names it
        ///     unambiguously, by code otherwise. <c>null</c> for a record whose data and value information repeat an
        ///     earlier record's, which every selector reaching it would find first.
        /// </summary>
        /// <param name="record">A record of this readout.</param>
        /// <returns>The selector, or <c>null</c> for a shadowed record.</returns>
        /// <exception cref="ArgumentException"><paramref name="record" /> is not a record of this readout.</exception>
        public MBusSelector? SelectorFor(MBusRecord record)
        {
            var index = IndexOf(record);
            if (ShadowOf(index) >= 0)
            {
                return null;
            }

            if (record.Quantity is { } quantity && !record.HasMeaningChangingExtension)
            {
                var byMeaning = MBusSelector.Of(quantity, record.Storage, record.Tariff, record.Subunit, record.Function);
                if (ReferenceEquals(FirstMatch(byMeaning), record))
                {
                    return byMeaning;
                }
            }

            return MBusSelector.CodeOf(record);
        }

        /// <summary>
        ///     One line per record: its number and telegram, quantity, value and unit, storage, tariff, subunit and
        ///     function, its bytes, and a selector that finds it — or the record that shadows it.
        /// </summary>
        /// <returns>The description, in the invariant culture.</returns>
        public string Describe()
        {
            var lines = new List<string>(Records.Count);
            var number = 0;
            for (var telegram = 0; telegram < Telegrams.Count; telegram++)
            {
                foreach (var record in Telegrams[telegram].Records)
                {
                    var shadow = ShadowOf(number);
                    var selector = shadow >= 0 ? $"shadowed by #{(shadow + 1).ToString(CultureInfo.InvariantCulture)}" : SelectorFor(record)!.ToString();
                    lines.Add(string.Join(" | ",
                                          $"#{(++number).ToString(CultureInfo.InvariantCulture)}",
                                          $"t{(telegram + 1).ToString(CultureInfo.InvariantCulture)}",
                                          record.Quantity?.ToString() ?? "—",
                                          ValueOf(record),
                                          string.Format(CultureInfo.InvariantCulture,
                                                        "storage {0}, tariff {1}, subunit {2}, {3}",
                                                        record.Storage,
                                                        record.Tariff,
                                                        record.Subunit,
                                                        record.Function),
                                          $"DIB {MBusHex.Format(record.Dib)}",
                                          $"VIB {MBusHex.Format(record.Vib)}",
                                          selector));
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        ///     Checks and decodes the frames one read returned from the meter at <paramref name="primaryAddress" />.
        /// </summary>
        /// <exception cref="MBusDecodeException">
        ///     There is no frame or more than <paramref name="maxTelegrams" />, a frame fails its checks or cannot be
        ///     decoded, a frame comes from another address, or the identification changes between frames.
        /// </exception>
        internal static MBusReadout Create(IReadOnlyList<byte[]> frames, int maxTelegrams, int primaryAddress)
        {
            if (frames.Count == 0)
            {
                throw new MBusDecodeException("The provider answered Ok with no telegram.");
            }

            if (frames.Count > maxTelegrams)
            {
                throw new MBusDecodeException($"The provider answered with {frames.Count} telegrams to a read of at most {maxTelegrams}.");
            }

            var telegrams = new List<MBusTelegram>(frames.Count);
            foreach (var frame in frames)
            {
                var telegram = MBusTelegram.Decode(frame);
                if (telegram.Address != primaryAddress)
                {
                    throw new MBusDecodeException($"Telegram {telegrams.Count + 1} comes from primary address {telegram.Address}, not from {primaryAddress}.");
                }

                // A second meter answering on the same primary address must not read as this meter's data.
                if (telegrams.Count > 0 && telegram.Header.Identification != telegrams[0].Header.Identification)
                {
                    throw new MBusDecodeException($"Telegram {telegrams.Count + 1} carries identification {telegram.Header.Identification}, " +
                                                  $"but telegram 1 carries {telegrams[0].Header.Identification}.");
                }

                telegrams.Add(telegram);
            }

            return new MBusReadout(telegrams.AsReadOnly());
        }

        private static string ValueOf(MBusRecord record)
        {
            if (record.Number is { } number)
            {
                var symbol = record.Quantity is { } quantity ? MBusUnits.ReportedSymbol(quantity) : string.Empty;
                var text = number.ToString("R", CultureInfo.InvariantCulture);
                return symbol.Length == 0 ? text : $"{text} {symbol}";
            }

            if (record.Timestamp is { } timestamp)
            {
                return timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return record.Text is { } text1 ? $"\"{text1}\"" : "no value";
        }

        private static ArgumentException UnitMismatch(MBusSelector selector, MBusRecord? record, MBusUnit unit)
        {
            var found = record == null ? string.Empty :
                            $" found the record DIB {MBusHex.Format(record.Dib)} VIB {MBusHex.Format(record.Vib)} ({record.Quantity?.ToString() ?? "no quantity"}), which";
            var subject = record == null ? $" names {selector.Quantity}, which" : string.Empty;
            return new ArgumentException($"{selector}{found}{subject} cannot be expressed in {unit}.", nameof(unit));
        }

        private MBusRecord? FirstMatch(MBusSelector selector)
        {
            foreach (var record in Records)
            {
                if (selector.Matches(record))
                {
                    return record;
                }
            }

            return null;
        }

        private int IndexOf(MBusRecord record)
        {
            for (var index = 0; index < Records.Count; index++)
            {
                if (ReferenceEquals(Records[index], record))
                {
                    return index;
                }
            }

            throw new ArgumentException("The record is not one of this readout's.", nameof(record));
        }

        /// <summary>The index of the earliest record with the same data and value information bytes, or -1 when there is none.</summary>
        private int ShadowOf(int index)
        {
            var code = MBusSelector.Concatenated(Records[index]).ToArray();
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (MBusSelector.Concatenated(Records[earlier]).SequenceEqual(code))
                {
                    return earlier;
                }
            }

            return -1;
        }
    }
}