using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.MBus;

namespace Vion.Examples.MBus.LogicBlocks
{
    /// <summary>
    ///     A commissioning tool for working out what an M-Bus meter really sends: point it at a primary address,
    ///     read, and see every record with a selector that finds it — ready to paste into a logic block — and every
    ///     telegram as hex, ready to replay in a test.
    /// </summary>
    /// <remarks>
    ///     The line is shared with the production meters, and every debug read takes line time from them. Polling is
    ///     therefore off by default, and its interval is at least the contract's default operation timeout, which
    ///     bounds one pass of a full line: a polling debug client adds at most one read per pass.
    /// </remarks>
    [LogicBlock(Name = "M-Bus Debug Client",
                Icon = "search-line",
                Groups = new[]
                         {
                             ReadGroup,
                             ResultGroup,
                             PropertyGroup.Diagnostics,
                         })]
    public class MBusDebugClient : LogicBlockBase
    {
        private const string ReadGroup = "read";

        private const string ResultGroup = "result";

        private const int MinPollIntervalSeconds = 300;

        private int _pollIntervalSeconds = MinPollIntervalSeconds;

        private bool _readInFlight;

        [ServiceProviderContractBinding(Identifier = "MBus", DefaultName = "M-Bus")]
        public IMBus MBus { get; set; } = null!;

        // ── Read ──────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Primary address", Minimum = 0, Maximum = 250)]
        [Presentation(Group = ReadGroup, Order = 10)]
        public int PrimaryAddress { get; set; }

        [ServiceProperty(Title = "Telegrams", Minimum = 1, Maximum = 10, Description = "How many telegrams the meter sends; fewer leaves the readout incomplete.")]
        [Presentation(Group = ReadGroup, Order = 20)]
        public int MaxTelegrams { get; set; } = 5;

        [ServiceProperty(Title = "Read now", Description = "Reads the meter once.")]
        [Presentation(Group = ReadGroup, Order = 30, UiHint = UiHints.Trigger)]
        public bool ReadNow
        {
            get => false;

            set
            {
                if (value)
                {
                    ExecuteRead();
                }
            }
        }

        [ServiceProperty(Title = "Polling enabled", Description = "Repeats the read. Every read takes line time from the production meters on the same line.")]
        [Presentation(Group = ReadGroup, Order = 40)]
        public bool PollingEnabled { get; set; }

        [ServiceProperty(Title = "Poll interval", Unit = "s", Minimum = MinPollIntervalSeconds, Maximum = 86400)]
        [Presentation(Group = ReadGroup, Order = 50, VisibleWhen = "PollingEnabled")]
        public int PollIntervalSeconds
        {
            get => _pollIntervalSeconds;
            set => _pollIntervalSeconds = Math.Max(MinPollIntervalSeconds, value);
        }

        // ── Result ────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Header", Description = "Who answered: identification, manufacturer, version, medium, access number and status.")]
        [Presentation(Group = ResultGroup, Order = 10)]
        public MBusHeaderRow Header { get; private set; }

        // A result table is state, not a time series, so it is a service property rather than a measuring point.
        [ServiceProperty(Title = "Records", Description = "One row per record, with a selector that finds it or the record that shadows it.")]
        [Presentation(Group = ResultGroup, Order = 20)]
        public ImmutableArray<MBusRecordRow> Records { get; private set; } = ImmutableArray<MBusRecordRow>.Empty;

        [ServiceProperty(Title = "Telegrams", Description = "Each telegram as hex, as a test replays it.")]
        [Presentation(Group = ResultGroup, Order = 30)]
        public ImmutableArray<string> Telegrams { get; private set; } = ImmutableArray<string>.Empty;

        [ServiceProperty(Title = "Complete", Description = "Whether the last telegram announced no further records.")]
        [Presentation(Group = ResultGroup, Order = 40)]
        public bool Complete { get; private set; }

        [ServiceProperty(Title = "Last read")]
        [Presentation(Group = ResultGroup, Order = 50)]
        public string LastRead { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Last read error")]
        [Presentation(Group = ResultGroup, Order = 60)]
        public string LastReadError { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Link")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public MBusLinkSummary Link { get; private set; }

        public MBusDebugClient(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            MBus.IsEnabled = true;
        }

        /// <summary>The poll loop reschedules itself, which lets its interval change at runtime.</summary>
        protected override void Starting()
        {
            InvokeSynchronizedAfter(PollTick, TimeSpan.FromSeconds(PollIntervalSeconds));
        }

        private void PollTick()
        {
            if (PollingEnabled)
            {
                ExecuteRead();
            }

            InvokeSynchronizedAfter(PollTick, TimeSpan.FromSeconds(PollIntervalSeconds));
        }

        /// <summary>
        ///     A binding has one read in flight, and the contract drops a second one without a callback. A press of
        ///     <i>Read now</i> while a poll waits in a busy line's queue would then change nothing on screen, so the
        ///     client says why instead.
        /// </summary>
        private void ExecuteRead()
        {
            if (_readInFlight)
            {
                LastReadError = "A read is already in flight; its answer will replace these results.";
                return;
            }

            _readInFlight = true;
            MBus.Read(PrimaryAddress, MaxTelegrams, this, OnReadout, OnError);
        }

        private void OnReadout(MBusReadout readout, MBusReceipt receipt)
        {
            _readInFlight = false;
            var header = readout.Header;
            Header = new MBusHeaderRow(header.Identification,
                                       header.Manufacturer,
                                       header.Version,
                                       header.Medium,
                                       header.AccessNumber,
                                       header.Status.Value,
                                       header.Status.HasError);
            Records = BuildRows(readout);
            Telegrams = readout.Telegrams.Select(telegram => telegram.ToString()).ToImmutableArray();
            Complete = readout.IsComplete;
            LastRead = string.Format(CultureInfo.InvariantCulture,
                                     "{0} telegrams, round trip {1:0.##} s, queued {2:0.###} s",
                                     readout.Telegrams.Count,
                                     receipt.RoundTrip.TotalSeconds,
                                     receipt.QueuedWait.TotalSeconds);
            LastReadError = string.Empty;
            Link = MBus.Link;
        }

        private void OnError(Exception error, MBusReceipt receipt)
        {
            _readInFlight = false;

            // The telegrams of a protocol error are in the message, so a capture survives a failed check.
            LastReadError = $"{receipt.Outcome}: {error.Message}";
            Link = MBus.Link;
        }

        private static ImmutableArray<MBusRecordRow> BuildRows(MBusReadout readout)
        {
            var rows = ImmutableArray.CreateBuilder<MBusRecordRow>(readout.Records.Count);
            var number = 0;
            for (var telegram = 0; telegram < readout.Telegrams.Count; telegram++)
            {
                foreach (var record in readout.Telegrams[telegram].Records)
                {
                    number++;
                    rows.Add(new MBusRecordRow(number,
                                               telegram + 1,
                                               record.Quantity?.ToString() ?? "—",
                                               ValueText(record),
                                               record.Storage,
                                               record.Tariff,
                                               record.Subunit,
                                               record.Function.ToString(),
                                               Hex(record.Dib),
                                               Hex(record.Vib),
                                               readout.SelectorFor(record)?.ToString() ?? $"shadowed by #{ShadowOf(readout, record)}"));
                }
            }

            return rows.MoveToImmutable();
        }

        /// <summary>The number of the earlier record whose bytes this one repeats.</summary>
        private static int ShadowOf(MBusReadout readout, MBusRecord record)
        {
            var code = record.Dib.Concat(record.Vib).ToArray();
            for (var index = 0; index < readout.Records.Count; index++)
            {
                if (readout.Records[index].Dib.Concat(readout.Records[index].Vib).SequenceEqual(code))
                {
                    return index + 1;
                }
            }

            return 0;
        }

        private static string ValueText(MBusRecord record)
        {
            if (record.Number is { } number)
            {
                var unit = record.Quantity is { } quantity && Units.TryGetValue(quantity, out var symbol) ? " " + symbol : string.Empty;
                return number.ToString("R", CultureInfo.InvariantCulture) + unit;
            }

            if (record.Timestamp is { } timestamp)
            {
                return timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return record.Text ?? "no value";
        }

        private static string Hex(IReadOnlyList<byte> bytes)
        {
            return string.Join(" ", bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
        }

        // The unit each quantity's Number is reported in, as MBusQuantity's members document it.
        private static readonly Dictionary<MBusQuantity, string> Units = new()
                                                                          {
                                                                              [MBusQuantity.Energy] = "Wh",
                                                                              [MBusQuantity.ReactiveEnergy] = "varh",
                                                                              [MBusQuantity.Volume] = "m³",
                                                                              [MBusQuantity.Power] = "W",
                                                                              [MBusQuantity.ReactivePower] = "var",
                                                                              [MBusQuantity.ApparentPower] = "VA",
                                                                              [MBusQuantity.VolumeFlow] = "m³/h",
                                                                              [MBusQuantity.FlowTemperature] = "°C",
                                                                              [MBusQuantity.ReturnTemperature] = "°C",
                                                                              [MBusQuantity.TemperatureDifference] = "K",
                                                                              [MBusQuantity.Voltage] = "V",
                                                                              [MBusQuantity.Current] = "A",
                                                                              [MBusQuantity.Frequency] = "Hz",
                                                                              [MBusQuantity.OperatingTime] = "h",
                                                                          };
    }

    /// <summary>
    ///     The header of the first telegram of the last readout.
    /// </summary>
    public readonly record struct MBusHeaderRow(
        [StructField(Title = "Identification", Description = "The meter's identification number, eight digits.")]
        string Identification,
        [StructField(Title = "Manufacturer", Description = "The three-letter manufacturer code.")]
        string Manufacturer,
        [StructField(Title = "Version")] int Version,
        [StructField(Title = "Medium", Description = "The medium (device type) code.")]
        int Medium,
        [StructField(Title = "Access number")] int AccessNumber,
        [StructField(Title = "Status", Description = "The status byte as received.")]
        int Status,
        [StructField(Title = "Error", Description = "The status byte reports an error.")]
        bool HasError);

    /// <summary>
    ///     One record of the last readout.
    /// </summary>
    public readonly record struct MBusRecordRow(
        [StructField(Title = "#", Description = "The record's number in the readout, as a shadowed record names its winner.")]
        int Number,
        [StructField(Title = "Telegram")] int Telegram,
        [StructField(Title = "Quantity")] string Quantity,
        [StructField(Title = "Value", Description = "The value in the unit its quantity is reported in.")]
        string Value,
        [StructField(Title = "Storage")] long Storage,
        [StructField(Title = "Tariff")] int Tariff,
        [StructField(Title = "Subunit")] int Subunit,
        [StructField(Title = "Function")] string Function,
        [StructField(Title = "DIB")] string Dib,
        [StructField(Title = "VIB")] string Vib,
        [StructField(Title = "Selector", Description = "C# that finds this record, or the record that shadows it.")]
        string Selector);
}
