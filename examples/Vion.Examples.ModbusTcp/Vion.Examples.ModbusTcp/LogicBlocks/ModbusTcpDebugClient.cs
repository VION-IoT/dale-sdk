using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Examples.ModbusTcp.LogicBlocks
{
    /// <summary>
    ///     An interactive Modbus TCP client for working out what a device actually speaks: point it at a
    ///     server, read a range of registers or coils, and reinterpret the same bytes under different field
    ///     types and byte/word orders until the numbers make sense. Then write values back the same way.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Addresses are <b>protocol (base-0)</b> addresses throughout — the number that goes on the wire.
    ///         Documentation written in the PLC/Modicon convention (40001, 30001, …) is one-based and names the
    ///         area in its leading digit, so holding register 40001 is address 0 here and input register 30007
    ///         is address 6.
    ///     </para>
    ///     <para>
    ///         Range reads go over the wire as a raw read and are decoded locally, so the hex, unsigned,
    ///         signed and interpreted columns of one result always describe the very same bytes. Flipping the
    ///         byte or word order re-decodes on the next read rather than changing what was asked of the
    ///         device.
    ///     </para>
    ///     <para>
    ///         Two client connections are used: one carries the poll loop and the watch slots, the other
    ///         carries the one-shot reads and writes you trigger by hand. A client executes its requests
    ///         sequentially, so without the split a slow poll against an unresponsive device would delay every
    ///         manual command behind it.
    ///     </para>
    /// </remarks>
    [LogicBlock(Name = "Modbus TCP Debug Client",
                Icon = "search-line",
                Groups = new[]
                         {
                             PropertyGroup.Status,
                             ConnectionGroup,
                             ReadGroup,
                             WriteGroup,
                             WatchGroup,
                             PropertyGroup.Diagnostics,
                         })]
    public class ModbusTcpDebugClient : LogicBlockBase
    {
        private const string ConnectionGroup = "connection";

        private const string ReadGroup = "read";

        private const string WriteGroup = "write";

        private const string WatchGroup = "watch";

        /// <summary>Modbus caps a single register read at 125 registers and a bit read at 2000 bits.</summary>
        private const int MaxRegistersPerRead = 125;

        private const int MaxBitsPerRead = 2000;

        /// <summary>FC16 carries at most 123 registers per request.</summary>
        private const int MaxRegistersPerWrite = 123;

        private readonly ILogicBlockModbusTcpClient _commandClient;

        private readonly IModbusDataConverter _converter;

        private readonly ILogger _logger;

        private readonly ILogicBlockModbusTcpClient _pollClient;

        private bool _connectionEnabled = true;

        private ModbusTcpConnectionSettings _connectionSettings = new("127.0.0.1", 15020, 1, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

        private ModbusTcpLinkPolicy _linkPolicy = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

        private bool _pollInFlight;

        private bool _watchInFlight;

        /// <summary>The unit identifier every request carries — one field of <see cref="ConnectionSettings" />.</summary>
        private int UnitId
        {
            get => _connectionSettings.UnitId;
        }

        // ── Connection ────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Connection", Description = "Where the client talks and how long it waits — endpoint, unit id and the two timeouts, edited as one value.")]
        [Presentation(DisplayName = "Connection", Group = ConnectionGroup, Order = 10)]
        public ModbusTcpConnectionSettings ConnectionSettings
        {
            get => _connectionSettings;

            set
            {
                _connectionSettings = value;
                ApplyConnectionSettings();
            }
        }

        [ServiceProperty(Title = "Link policy",
                         Description = "What the client does when the device stops answering: how stale a queued request may get, and how long it waits between connect attempts.")]
        [Presentation(DisplayName = "Link policy", Group = ConnectionGroup, Order = 20)]
        public ModbusTcpLinkPolicy LinkPolicy
        {
            get => _linkPolicy;

            set
            {
                _linkPolicy = value;
                ApplyConnectionSettings();
            }
        }

        // A switch, not a setting — it belongs beside the two structs rather than inside them, because
        // turning traffic off is something you do to a configuration you want to keep.
        [ServiceProperty(Title = "Connection enabled", Description = "Turn off to stop all traffic without losing the settings.")]
        [Presentation(Group = ConnectionGroup, Order = 30)]
        public bool ConnectionEnabled
        {
            get => _connectionEnabled;

            set
            {
                if (_connectionEnabled == value)
                {
                    return;
                }

                _connectionEnabled = value;
                ApplyConnectionSettings();
            }
        }

        // ── Read ──────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Read function")]
        [Presentation(DisplayName = "Read function", Group = ReadGroup, Order = 10)]
        public ReadFunction ReadFunction { get; set; } = ReadFunction.HoldingRegisters;

        [ServiceProperty(Title = "Start address", Minimum = 0, Maximum = 65535, Description = "Protocol (base-0) address of the first register or bit.")]
        [Presentation(Group = ReadGroup, Order = 20)]
        public int Address { get; set; }

        [ServiceProperty(Title = "Quantity", Minimum = 1, Maximum = 2000, Description = "Registers or bits to read. Modbus allows at most 125 registers or 2000 bits per request.")]
        [Presentation(Group = ReadGroup, Order = 30)]
        public int Quantity { get; set; } = 10;

        [ServiceProperty(Title = "Field type", Description = "How to interpret the registers on top of the raw per-register columns.")]
        [Presentation(DisplayName = "Field type", Group = ReadGroup, Order = 40, VisibleWhen = "ReadFunction in ['HoldingRegisters','InputRegisters']")]
        public RegisterFieldType ReadFieldType { get; set; } = RegisterFieldType.Float32;

        [ServiceProperty(Title = "Byte order", Description = "Order of the two bytes inside each register. Modbus standard is MSB first.")]
        [Presentation(DisplayName = "Byte order",
                      Group = ReadGroup,
                      Order = 50,
                      VisibleWhen =
                          "ReadFunction in ['HoldingRegisters','InputRegisters'] && ReadFieldType in ['UInt16','Int16','UInt32','Int32','Float32','UInt64','Int64','Float64']")]
        public ByteOrder ReadByteOrder { get; set; } = ByteOrder.MsbToLsb;

        [ServiceProperty(Title = "Word order (32 bit)", Description = "Which register holds the most significant word of a 32-bit value.")]
        [Presentation(DisplayName = "Word order (32 bit)",
                      Group = ReadGroup,
                      Order = 60,
                      VisibleWhen = "ReadFunction in ['HoldingRegisters','InputRegisters'] && ReadFieldType in ['UInt32','Int32','Float32']")]
        public WordOrder32 ReadWordOrder32 { get; set; } = WordOrder32.MswToLsw;

        [ServiceProperty(Title = "Word order (64 bit)", Description = "Register order of a 64-bit value, including the two mid-endian permutations.")]
        [Presentation(DisplayName = "Word order (64 bit)",
                      Group = ReadGroup,
                      Order = 70,
                      VisibleWhen = "ReadFunction in ['HoldingRegisters','InputRegisters'] && ReadFieldType in ['UInt64','Int64','Float64']")]
        public WordOrder64 ReadWordOrder64 { get; set; } = WordOrder64.ABCD;

        [ServiceProperty(Title = "Text encoding")]
        [Presentation(DisplayName = "Text encoding",
                      Group = ReadGroup,
                      Order = 80,
                      VisibleWhen = "ReadFunction in ['HoldingRegisters','InputRegisters'] && ReadFieldType == 'String'")]
        public TextEncoding ReadTextEncoding { get; set; } = TextEncoding.Ascii;

        [ServiceProperty(Title = "Read now", Description = "Issues one read on the command connection, independent of the poll loop.")]
        [Presentation(Group = ReadGroup, Order = 90, UiHint = UiHints.Trigger)]
        public bool ReadOnce
        {
            get => false;

            set
            {
                if (value)
                {
                    ExecuteRead(_commandClient);
                }
            }
        }

        [ServiceProperty(Title = "Polling enabled", Description = "Repeats the configured read continuously.")]
        [Presentation(Group = ReadGroup, Order = 100)]
        public bool PollingEnabled { get; set; } = true;

        [ServiceProperty(Title = "Poll interval", Unit = "ms", Minimum = 100, Maximum = 60000)]
        [Presentation(Group = ReadGroup, Order = 110, VisibleWhen = "PollingEnabled")]
        public int PollIntervalMs { get; set; } = 1000;

        [ServiceProperty(Title = "Poll interval while faulted",
                         Unit = "ms",
                         Minimum = 100,
                         Maximum = 300000,
                         Description =
                             "The interval used while the link reads Faulted. Polling a device that is not answering at full rate buys nothing — the client reconnects on its own — so an unattended block slows down until the link recovers.")]
        [Presentation(Group = ReadGroup, Order = 115, VisibleWhen = "PollingEnabled")]
        public int FaultedPollIntervalMs { get; set; } = 5000;

        // A result table is state, not a time series — declaring it a measuring point would push a
        // 125-row array into the charting pipeline on every poll, and VisibleWhen is not evaluated for
        // measuring points, so the irrelevant table could not hide itself either.
        [ServiceProperty(Title = "Registers", Description = "One row per register: raw hex, both 16-bit readings, and the decoded value where a field starts.")]
        [Presentation(DisplayName = "Registers", Group = ReadGroup, Order = 120, VisibleWhen = "ReadFunction in ['HoldingRegisters','InputRegisters']")]
        public ImmutableArray<RegisterRow> Registers { get; private set; } = ImmutableArray<RegisterRow>.Empty;

        [ServiceProperty(Title = "Bits", Description = "One row per coil or discrete input.")]
        [Presentation(DisplayName = "Bits", Group = ReadGroup, Order = 130, VisibleWhen = "ReadFunction in ['Coils','DiscreteInputs']")]
        public ImmutableArray<BitRow> Bits { get; private set; } = ImmutableArray<BitRow>.Empty;

        [ServiceProperty(Title = "Last read")]
        [Presentation(Group = ReadGroup, Order = 140)]
        public string LastReadInfo { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Last read error")]
        [Presentation(Group = ReadGroup, Order = 150)]
        public string LastReadError { get; private set; } = string.Empty;

        // ── Write ─────────────────────────────────────────────────────────────────

        [ServiceProperty(Title = "Write function")]
        [Presentation(DisplayName = "Write function", Group = WriteGroup, Order = 10)]
        public WriteFunction WriteFunction { get; set; } = WriteFunction.SingleRegister;

        [ServiceProperty(Title = "Write address", Minimum = 0, Maximum = 65535, Description = "Protocol (base-0) address of the first register or coil to write.")]
        [Presentation(Group = WriteGroup, Order = 20)]
        public int WriteAddress { get; set; }

        [ServiceProperty(Title = "Field type", Description = "How to encode the value across the registers.")]
        [Presentation(DisplayName = "Field type", Group = WriteGroup, Order = 30, VisibleWhen = "WriteFunction == 'MultipleRegisters'")]
        public RegisterFieldType WriteFieldType { get; set; } = RegisterFieldType.Float32;

        [ServiceProperty(Title = "Byte order")]
        [Presentation(DisplayName = "Byte order",
                      Group = WriteGroup,
                      Order = 40,
                      VisibleWhen =
                          "WriteFunction == 'SingleRegister' || (WriteFunction == 'MultipleRegisters' && WriteFieldType in ['UInt16','Int16','UInt32','Int32','Float32','UInt64','Int64','Float64'])")]
        public ByteOrder WriteByteOrder { get; set; } = ByteOrder.MsbToLsb;

        [ServiceProperty(Title = "Word order (32 bit)")]
        [Presentation(DisplayName = "Word order (32 bit)",
                      Group = WriteGroup,
                      Order = 50,
                      VisibleWhen = "WriteFunction == 'MultipleRegisters' && WriteFieldType in ['UInt32','Int32','Float32']")]
        public WordOrder32 WriteWordOrder32 { get; set; } = WordOrder32.MswToLsw;

        [ServiceProperty(Title = "Word order (64 bit)")]
        [Presentation(DisplayName = "Word order (64 bit)",
                      Group = WriteGroup,
                      Order = 60,
                      VisibleWhen = "WriteFunction == 'MultipleRegisters' && WriteFieldType in ['UInt64','Int64','Float64']")]
        public WordOrder64 WriteWordOrder64 { get; set; } = WordOrder64.ABCD;

        [ServiceProperty(Title = "Text encoding")]
        [Presentation(DisplayName = "Text encoding", Group = WriteGroup, Order = 70, VisibleWhen = "WriteFunction == 'MultipleRegisters' && WriteFieldType == 'String'")]
        public TextEncoding WriteTextEncoding { get; set; } = TextEncoding.Ascii;

        [ServiceProperty(Title = "Value",
                         Description =
                             "Coils take true/false (comma-separated for FC15). Registers take a number, 0x-prefixed hex for FC6, or a comma-separated list for FC16. A String field takes the text itself.")]
        [Presentation(Group = WriteGroup, Order = 80)]
        public string WriteValue { get; set; } = string.Empty;

        [ServiceProperty(Title = "Write now")]
        [Presentation(Group = WriteGroup, Order = 90, UiHint = UiHints.Trigger)]
        public bool WriteOnce
        {
            get => false;

            set
            {
                if (value)
                {
                    ExecuteWrite();
                }
            }
        }

        [ServiceProperty(Title = "Last write")]
        [Presentation(Group = WriteGroup, Order = 100)]
        public string LastWriteInfo { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Last write error")]
        [Presentation(Group = WriteGroup, Order = 110)]
        public string LastWriteError { get; private set; } = string.Empty;

        // ── Watch slots ───────────────────────────────────────────────────────────

        /// <summary>
        ///     How many watch slots this instance exposes. Chosen when the block is configured (RFC 0016) —
        ///     slots above the count do not exist at all, rather than sitting empty in the UI.
        /// </summary>
        [ServiceProperty(Title = "Watch slots", Minimum = 0, Maximum = 8, Description = "Number of pinned registers this instance exposes. Set when the block is configured.")]
        [InstantiationParameter]
        [Presentation(Group = WatchGroup, Order = 10)]
        public int WatchSlotCount { get; init; } = 3;

        [ServiceProperty(Title = "Watch interval", Unit = "ms", Minimum = 100, Maximum = 60000)]
        [Presentation(Group = WatchGroup, Order = 20)]
        public int WatchIntervalMs { get; set; } = 1000;

        [IncludedWhen("WatchSlotCount >= 1")]
        public WatchSlot Watch1 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 2")]
        public WatchSlot Watch2 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 3")]
        public WatchSlot Watch3 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 4")]
        public WatchSlot Watch4 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 5")]
        public WatchSlot Watch5 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 6")]
        public WatchSlot Watch6 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 7")]
        public WatchSlot Watch7 { get; } = new();

        [IncludedWhen("WatchSlotCount >= 8")]
        public WatchSlot Watch8 { get; } = new();

        // ── Status & diagnostics ──────────────────────────────────────────────────

        [ServiceProperty(Title = "Link", Description = "The polling connection at a glance — the SDK's own verdict on the device and the socket, not a tally this block keeps.")]
        [Presentation(DisplayName = "Link", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public LinkStatus LinkHealth { get; private set; } = LinkStatus.Unknown;

        // The SDK accumulates every receipt into these two snapshots, so there is nothing to count here:
        // ModbusLinkSummary and ModbusTcpConnectionSummary are flat readonly record structs of
        // service-property-legal types and publish as they are. Link is the verdict on the *device* — local
        // outcomes (a backed-up queue, an expired request) are counted in it but never move Link.State, so a
        // congested client stays distinguishable from a broken one. Connection is the verdict on the *socket*.
        [ServiceProperty(Title = "Link", Description = "The polling connection's view of the device: state, last contact, per-outcome counters and latencies.")]
        [Presentation(DisplayName = "Link", Group = PropertyGroup.Diagnostics, Order = 10)]
        public ModbusLinkSummary Link { get; private set; }

        [ServiceProperty(Title = "Connection", Description = "The polling connection's socket: connect attempts, failures, and the backoff the client is waiting out.")]
        [Presentation(DisplayName = "Connection", Group = PropertyGroup.Diagnostics, Order = 20)]
        public ModbusTcpConnectionSummary Connection { get; private set; }

        [ServiceProperty(Title = "Command link", Description = "The same summary for the second connection — the one carrying the reads and writes you trigger by hand.")]
        [Presentation(DisplayName = "Command link", Group = PropertyGroup.Diagnostics, Order = 30)]
        public ModbusLinkSummary CommandLink { get; private set; }

        [ServiceProperty(Title = "Last read at",
                         Description = "When the last poll response was observed — taken from the transaction's receipt, not from when this block got round to it.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 40, Format = Formats.Relative)]
        public DateTime? LastReadAt { get; private set; }

        [ServiceProperty(Title = "Last round trip", Unit = "ms", Description = "The receipt's wire time for the last poll — the queue wait is reported separately, in Link.")]
        [ServiceMeasuringPoint(Title = "Last round trip", Unit = "ms")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 50, Decimals = 1)]
        public double LastRoundTripMs { get; private set; }

        [ServiceProperty(Title = "Last error")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 60)]
        public string LastError { get; private set; } = string.Empty;

        public ModbusTcpDebugClient(ILogicBlockModbusTcpClientFactory clientFactory, IModbusDataConverter converter, ILogger logger) : base(logger)
        {
            _converter = converter;
            _logger = logger;
            _pollClient = clientFactory.Create();
            _commandClient = clientFactory.Create();
        }

        protected override void Ready()
        {
        }

        /// <summary>
        ///     Persisted property values are in place by now, so this is where the clients get configured and
        ///     the two tick loops start. Both loops reschedule themselves, which is what lets the interval be
        ///     changed at runtime — a <c>[Timer]</c> interval is fixed at compile time.
        /// </summary>
        protected override void Starting()
        {
            ApplyConnectionSettings();
            InvokeSynchronizedAfter(PollTick, NextPollDelay());
            InvokeSynchronizedAfter(WatchTick, TimeSpan.FromMilliseconds(WatchIntervalMs));
        }

        protected override void Stopping()
        {
            // Factory-created clients come from the root container, not the per-block scope that disposes
            // an injected client on teardown (RFC 0018) — so closing these sockets is the block's job.
            _pollClient.Dispose();
            _commandClient.Dispose();
        }

        // ── Connection handling ───────────────────────────────────────────────────

        /// <summary>
        ///     The one reconfigure chokepoint: every field of both structs is pushed to both clients on any
        ///     edit. That is safe because the SDK's setters detect change — re-supplying the same IP or port
        ///     is a no-op, so editing the operation timeout no longer drops a healthy socket or cancels a
        ///     connect backoff. There is deliberately no disable / re-enable cycle around the assignments
        ///     either: re-enabling resets the backoff, and that would hide the behaviour this example exists
        ///     to show.
        /// </summary>
        private void ApplyConnectionSettings()
        {
            foreach (var client in new[] { _pollClient, _commandClient })
            {
                try
                {
                    client.IpAddress = _connectionSettings.IpAddress;
                    client.Port = _connectionSettings.Port;
                    client.ConnectionTimeout = _connectionSettings.ConnectionTimeout;
                    client.DefaultOperationTimeout = _connectionSettings.OperationTimeout;
                    client.MaxQueuedAge = _linkPolicy.MaxQueuedAge;
                    client.ConnectBackoff = _linkPolicy.ConnectBackoff;
                    client.ConnectBackoffMax = _linkPolicy.ConnectBackoffMax < _linkPolicy.ConnectBackoff ? _linkPolicy.ConnectBackoff : _linkPolicy.ConnectBackoffMax;
                    client.IsEnabled = _connectionEnabled;
                }
                catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
                {
                    // A half-typed address or a zero duration arrives here on every keystroke — report it,
                    // keep the block alive.
                    LastError = ex.Message;
                    _logger.LogDebug(ex, "Invalid Modbus TCP connection settings");
                }
            }

            RefreshDiagnostics();
        }

        // ── Polling ───────────────────────────────────────────────────────────────

        private void PollTick()
        {
            // The in-flight guard matters against an unresponsive device: without it every tick would add a
            // request to a queue that is not draining, and the backlog would outlive the failure.
            if (PollingEnabled && _connectionEnabled && !_pollInFlight)
            {
                _pollInFlight = true;
                ExecuteRead(_pollClient, () => _pollInFlight = false);
            }

            RefreshDiagnostics();
            InvokeSynchronizedAfter(PollTick, NextPollDelay());
        }

        /// <summary>
        ///     The recommended unattended pattern: while the link is faulted the client is already reconnecting
        ///     on its own schedule, so polling at full rate only adds failed requests to count. One line, and it
        ///     is the difference between a quiet log and a flood while a device is down overnight.
        /// </summary>
        private TimeSpan NextPollDelay()
        {
            return TimeSpan.FromMilliseconds(Link.State == ModbusLinkState.Faulted ? FaultedPollIntervalMs : PollIntervalMs);
        }

        /// <summary>
        ///     Republishes the SDK's accumulated snapshots and folds them into the headline pill. Nothing is
        ///     counted here — the counters, latencies and both verdicts come from the receipts the client
        ///     already recorded. The one thing the block contributes is knowing it has switched the client
        ///     off: a disabled client issues nothing, so its <c>Link</c> rightly keeps the last verdict, and a
        ///     pill that mirrored it would go on claiming Online for a link nobody is using.
        /// </summary>
        private void RefreshDiagnostics()
        {
            Link = _pollClient.Link;
            Connection = _pollClient.Connection;
            CommandLink = _commandClient.Link;

            LinkHealth = !_connectionEnabled ? LinkStatus.Disabled :
                         Connection.State == ModbusTcpConnectionState.BackingOff ? LinkStatus.BackingOff : Link.State switch
                         {
                             ModbusLinkState.Online => LinkStatus.Online,
                             ModbusLinkState.Faulted => LinkStatus.Faulted,
                             _ => LinkStatus.Unknown,
                         };
        }

        private void WatchTick()
        {
            if (_connectionEnabled && !_watchInFlight)
            {
                var slots = IncludedSlots().Where(slot => slot.Enabled).ToList();

                if (slots.Count > 0)
                {
                    _watchInFlight = true;
                    ReadWatchSlots(slots, 0);
                }
            }

            InvokeSynchronizedAfter(WatchTick, TimeSpan.FromMilliseconds(WatchIntervalMs));
        }

        /// <summary>
        ///     Reads the enabled slots one after another, each read starting when the previous one finishes.
        ///     Sequential rather than fired all at once so a tick can never enqueue more work than the device
        ///     managed to answer.
        /// </summary>
        private void ReadWatchSlots(IReadOnlyList<WatchSlot> slots, int index)
        {
            if (index >= slots.Count)
            {
                _watchInFlight = false;

                return;
            }

            var slot = slots[index];
            var address = (ushort)slot.Address;
            var registers = slot.RegisterCount;

            void ReadNext()
            {
                ReadWatchSlots(slots, index + 1);
            }

            void OnBytes(byte[] bytes, ModbusReceipt _)
            {
                try
                {
                    var decoded = DecodeScalar(bytes,
                                               0,
                                               ToRegisterFieldType(slot.FieldType),
                                               slot.ByteOrder,
                                               slot.WordOrder32,
                                               slot.WordOrder64);
                    slot.Value = Convert.ToDouble(decoded, CultureInfo.InvariantCulture);
                    slot.Status = "OK";
                    RefreshDiagnostics();
                }
                catch (Exception ex)
                {
                    slot.Status = Describe(ex);
                    RegisterFailure(ex);
                }

                ReadNext();
            }

            void OnError(Exception ex, ModbusReceipt receipt)
            {
                // One bad slot must not stop the others — record it and carry on down the list. The receipt
                // names how it ended, which separates "the device said no" from "we never got that far".
                slot.Status = $"{receipt.Outcome}: {Describe(ex)}";
                RegisterFailure(ex);
                ReadNext();
            }

            if (slot.Function == WatchFunction.HoldingRegisters)
            {
                _pollClient.ReadHoldingRegistersRaw(UnitId,
                                                    address,
                                                    registers,
                                                    this,
                                                    OnBytes,
                                                    OnError);
            }
            else
            {
                _pollClient.ReadInputRegistersRaw(UnitId,
                                                  address,
                                                  registers,
                                                  this,
                                                  OnBytes,
                                                  OnError);
            }
        }

        private IEnumerable<WatchSlot> IncludedSlots()
        {
            // Mirrors the [IncludedWhen] gates: slots above the configured count exist as CLR objects but
            // are not part of this instance, so they must not be polled.
            var all = new[] { Watch1, Watch2, Watch3, Watch4, Watch5, Watch6, Watch7, Watch8 };

            return all.Take(Math.Max(0, Math.Min(WatchSlotCount, all.Length)));
        }

        // ── Reading ───────────────────────────────────────────────────────────────

        private void ExecuteRead(ILogicBlockModbusTcpClient client, Action? completed = null)
        {
            var function = ReadFunction;
            var address = Address;
            var quantity = Quantity;
            var isBitRead = function is ReadFunction.Coils or ReadFunction.DiscreteInputs;
            var limit = isBitRead ? MaxBitsPerRead : MaxRegistersPerRead;

            if (quantity < 1 || quantity > limit)
            {
                FailRead($"Quantity must be between 1 and {limit} for this function.");
                completed?.Invoke();

                return;
            }

            if (address + quantity > ushort.MaxValue + 1)
            {
                FailRead("Start address plus quantity exceeds the Modbus address space.");
                completed?.Invoke();

                return;
            }

            void OnError(Exception ex, ModbusReceipt receipt)
            {
                LastReadError = $"{receipt.Outcome}: {Describe(ex)}";
                RegisterFailure(ex);
                completed?.Invoke();
            }

            void OnBits(bool[] values, ModbusReceipt receipt)
            {
                Bits = values.Select((value, index) => new BitRow(address + index, value)).ToImmutableArray();
                Registers = ImmutableArray<RegisterRow>.Empty;
                CompleteRead(function, address, quantity, receipt);
                completed?.Invoke();
            }

            void OnRegisters(byte[] bytes, ModbusReceipt receipt)
            {
                try
                {
                    Registers = BuildRegisterRows(bytes, address, quantity);
                    Bits = ImmutableArray<BitRow>.Empty;
                    CompleteRead(function, address, quantity, receipt);
                }
                catch (Exception ex)
                {
                    // Decoding is local, so a failure here is a bad field-type/quantity combination rather
                    // than a communication problem — report it as a read error all the same.
                    LastReadError = Describe(ex);
                    RegisterFailure(ex);
                }

                completed?.Invoke();
            }

            switch (function)
            {
                case ReadFunction.Coils:
                    client.ReadCoils(UnitId,
                                     (ushort)address,
                                     (ushort)quantity,
                                     this,
                                     OnBits,
                                     OnError);

                    break;
                case ReadFunction.DiscreteInputs:
                    client.ReadDiscreteInputs(UnitId,
                                              (ushort)address,
                                              (ushort)quantity,
                                              this,
                                              OnBits,
                                              OnError);

                    break;
                case ReadFunction.HoldingRegisters:
                    client.ReadHoldingRegistersRaw(UnitId,
                                                   (ushort)address,
                                                   (ushort)quantity,
                                                   this,
                                                   OnRegisters,
                                                   OnError);

                    break;
                default:
                    client.ReadInputRegistersRaw(UnitId,
                                                 (ushort)address,
                                                 (ushort)quantity,
                                                 this,
                                                 OnRegisters,
                                                 OnError);

                    break;
            }
        }

        /// <summary>
        ///     The receipt is what a poll handler wants: <see cref="ModbusReceipt.ReceivedAt" /> is when the
        ///     answer was observed — stamped before the callback was handed to this block's mailbox, so it dates
        ///     the transaction and not this block's backlog — and <see cref="ModbusReceipt.RoundTrip" /> is the
        ///     time on the wire alone. Neither can be measured correctly from inside the callback.
        /// </summary>
        private void CompleteRead(ReadFunction function, int address, int quantity, ModbusReceipt receipt)
        {
            LastReadAt = receipt.ReceivedAt;
            LastRoundTripMs = receipt.RoundTrip.TotalMilliseconds;
            LastReadError = string.Empty;
            LastReadInfo = $"{FunctionCode(function)} @ {address} x {quantity} — {receipt.Outcome}, {LastRoundTripMs:F1} ms";
            RefreshDiagnostics();
        }

        // A rejected quantity or address never reaches the device, so it says nothing about the link and
        // must not move the status pill — it belongs in the error strings only.
        private void FailRead(string message)
        {
            LastReadError = message;
            LastError = message;
        }

        /// <summary>
        ///     Turns one raw response into table rows. The per-register columns always show the wire bytes as
        ///     they arrived; the interpreted column is filled only on rows where a field of the configured
        ///     type starts and fully fits inside the response.
        /// </summary>
        private ImmutableArray<RegisterRow> BuildRegisterRows(byte[] bytes, int address, int quantity)
        {
            var rows = ImmutableArray.CreateBuilder<RegisterRow>(quantity);
            var registersPerValue = RegistersPerValue(ReadFieldType, quantity);

            for (var i = 0; i < quantity; i++)
            {
                var offset = i * 2;
                var high = bytes[offset];
                var low = bytes[offset + 1];
                var unsigned = (ushort)((high << 8) | low);

                var interpreted = string.Empty;

                if (ReadFieldType == RegisterFieldType.String)
                {
                    // A string spans the whole response, so it belongs on the first row only.
                    if (i == 0)
                    {
                        interpreted = _converter.ConvertBytesToString(new Memory<byte>(bytes), ReadTextEncoding);
                    }
                }
                else if (registersPerValue > 0 && i % registersPerValue == 0 && i + registersPerValue <= quantity)
                {
                    var value = DecodeScalar(bytes,
                                             offset,
                                             ReadFieldType,
                                             ReadByteOrder,
                                             ReadWordOrder32,
                                             ReadWordOrder64);
                    interpreted = Format(value);
                }

                rows.Add(new RegisterRow(address + i, $"{high:X2} {low:X2}", unsigned, (short)unsigned, interpreted));
            }

            return rows.ToImmutable();
        }

        /// <summary>
        ///     Decodes one value out of a raw response, applying the same conversion steps in the same order
        ///     as the SDK's typed reads: byte swap, then word swap, then reinterpret.
        /// </summary>
        private object DecodeScalar(byte[] source,
                                    int byteOffset,
                                    RegisterFieldType fieldType,
                                    ByteOrder byteOrder,
                                    WordOrder32 wordOrder32,
                                    WordOrder64 wordOrder64)
        {
            var length = RegistersPerValue(fieldType, 0) * 2;

            if (length == 0 || byteOffset + length > source.Length)
            {
                throw new InvalidOperationException($"The response is too short to decode a {fieldType} value at byte offset {byteOffset}.");
            }

            var slice = new byte[length];
            Array.Copy(source, byteOffset, slice, 0, length);
            var memory = new Memory<byte>(slice);

            _converter.SwapBytes(memory, byteOrder);

            switch (length)
            {
                case 4:
                    _converter.SwapWords(memory, wordOrder32);

                    break;
                case 8:
                    _converter.SwapWords(memory, wordOrder64);

                    break;
            }

            return fieldType switch
            {
                RegisterFieldType.UInt16 => _converter.CastFromBytes<ushort>(memory)[0],
                RegisterFieldType.Int16 => _converter.CastFromBytes<short>(memory)[0],
                RegisterFieldType.UInt32 => _converter.CastFromBytes<uint>(memory)[0],
                RegisterFieldType.Int32 => _converter.CastFromBytes<int>(memory)[0],
                RegisterFieldType.Float32 => _converter.CastFromBytes<float>(memory)[0],
                RegisterFieldType.UInt64 => _converter.CastFromBytes<ulong>(memory)[0],
                RegisterFieldType.Int64 => _converter.CastFromBytes<long>(memory)[0],
                RegisterFieldType.Float64 => _converter.CastFromBytes<double>(memory)[0],
                _ => throw new InvalidOperationException($"{fieldType} cannot be decoded as a scalar."),
            };
        }

        // ── Writing ───────────────────────────────────────────────────────────────

        private void ExecuteWrite()
        {
            var address = (ushort)WriteAddress;

            void OnSuccess(ModbusReceipt receipt)
            {
                LastWriteError = string.Empty;
                LastWriteInfo = $"{WriteFunctionCode(WriteFunction)} @ {address} — {receipt.Outcome}, {receipt.RoundTrip.TotalMilliseconds:F1} ms";
                RefreshDiagnostics();
            }

            void OnError(Exception ex, ModbusReceipt receipt)
            {
                LastWriteError = $"{receipt.Outcome}: {Describe(ex)}";
                RegisterFailure(ex);
            }

            try
            {
                switch (WriteFunction)
                {
                    case WriteFunction.SingleCoil:
                        _commandClient.WriteSingleCoil(UnitId,
                                                       address,
                                                       ParseBool(WriteValue),
                                                       this,
                                                       OnSuccess,
                                                       OnError);

                        break;
                    case WriteFunction.MultipleCoils:
                        _commandClient.WriteMultipleCoils(UnitId,
                                                          address,
                                                          SplitValues(WriteValue).Select(ParseBool).ToArray(),
                                                          this,
                                                          OnSuccess,
                                                          OnError);

                        break;
                    case WriteFunction.SingleRegister:
                        WriteSingleRegister(address, OnSuccess, OnError);

                        break;
                    default:
                        WriteMultipleRegisters(address, OnSuccess, OnError);

                        break;
                }
            }
            catch (FormatException ex)
            {
                // Parsing happens before anything is sent, so a malformed value never reaches the device.
                FailWrite(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                FailWrite(ex.Message);
            }
        }

        private void WriteSingleRegister(ushort address, Action<ModbusReceipt> onSuccess, Action<Exception, ModbusReceipt> onError)
        {
            var raw = ParseRegisterWord(WriteValue);

            if (raw < 0)
            {
                _commandClient.WriteSingleHoldingRegister(UnitId,
                                                          address,
                                                          (short)raw,
                                                          this,
                                                          onSuccess,
                                                          onError,
                                                          WriteByteOrder);
            }
            else
            {
                _commandClient.WriteSingleHoldingRegister(UnitId,
                                                          address,
                                                          (ushort)raw,
                                                          this,
                                                          onSuccess,
                                                          onError,
                                                          WriteByteOrder);
            }
        }

        private void WriteMultipleRegisters(ushort address, Action<ModbusReceipt> onSuccess, Action<Exception, ModbusReceipt> onError)
        {
            if (WriteFieldType == RegisterFieldType.String)
            {
                _commandClient.WriteMultipleHoldingRegistersAsString(UnitId,
                                                                     address,
                                                                     WriteValue,
                                                                     this,
                                                                     onSuccess,
                                                                     onError,
                                                                     WriteTextEncoding);

                return;
            }

            var parts = SplitValues(WriteValue);
            var registersPerValue = RegistersPerValue(WriteFieldType, 0);

            if (registersPerValue == 0)
            {
                throw new InvalidOperationException("Choose a field type before writing multiple registers.");
            }

            if (parts.Count * registersPerValue > MaxRegistersPerWrite)
            {
                throw new InvalidOperationException($"A single FC16 request carries at most {MaxRegistersPerWrite} registers.");
            }

            switch (WriteFieldType)
            {
                case RegisterFieldType.UInt16:
                    _commandClient.WriteMultipleHoldingRegistersAsUShort(UnitId,
                                                                         address,
                                                                         parts.Select(ParseUInt16).ToArray(),
                                                                         this,
                                                                         onSuccess,
                                                                         onError,
                                                                         WriteByteOrder);

                    break;
                case RegisterFieldType.Int16:
                    _commandClient.WriteMultipleHoldingRegistersAsShort(UnitId,
                                                                        address,
                                                                        parts.Select(ParseInt16).ToArray(),
                                                                        this,
                                                                        onSuccess,
                                                                        onError,
                                                                        WriteByteOrder);

                    break;
                case RegisterFieldType.UInt32:
                    _commandClient.WriteMultipleHoldingRegistersAsUInt(UnitId,
                                                                       address,
                                                                       parts.Select(ParseUInt32).ToArray(),
                                                                       this,
                                                                       onSuccess,
                                                                       onError,
                                                                       WriteByteOrder,
                                                                       WriteWordOrder32);

                    break;
                case RegisterFieldType.Int32:
                    _commandClient.WriteMultipleHoldingRegistersAsInt(UnitId,
                                                                      address,
                                                                      parts.Select(ParseInt32).ToArray(),
                                                                      this,
                                                                      onSuccess,
                                                                      onError,
                                                                      WriteByteOrder,
                                                                      WriteWordOrder32);

                    break;
                case RegisterFieldType.Float32:
                    _commandClient.WriteMultipleHoldingRegistersAsFloat(UnitId,
                                                                        address,
                                                                        parts.Select(ParseSingle).ToArray(),
                                                                        this,
                                                                        onSuccess,
                                                                        onError,
                                                                        WriteByteOrder,
                                                                        WriteWordOrder32);

                    break;
                case RegisterFieldType.UInt64:
                    _commandClient.WriteMultipleHoldingRegistersAsULong(UnitId,
                                                                        address,
                                                                        parts.Select(ParseUInt64).ToArray(),
                                                                        this,
                                                                        onSuccess,
                                                                        onError,
                                                                        WriteByteOrder,
                                                                        WriteWordOrder64);

                    break;
                case RegisterFieldType.Int64:
                    _commandClient.WriteMultipleHoldingRegistersAsLong(UnitId,
                                                                       address,
                                                                       parts.Select(ParseInt64).ToArray(),
                                                                       this,
                                                                       onSuccess,
                                                                       onError,
                                                                       WriteByteOrder,
                                                                       WriteWordOrder64);

                    break;
                default:
                    _commandClient.WriteMultipleHoldingRegistersAsDouble(UnitId,
                                                                         address,
                                                                         parts.Select(ParseDouble).ToArray(),
                                                                         this,
                                                                         onSuccess,
                                                                         onError,
                                                                         WriteByteOrder,
                                                                         WriteWordOrder64);

                    break;
            }
        }

        // As with FailRead: a value that could not be parsed never left the block.
        private void FailWrite(string message)
        {
            LastWriteError = message;
            LastError = message;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        private void RegisterFailure(Exception ex)
        {
            LastError = Describe(ex);
            RefreshDiagnostics();
            _logger.LogDebug(ex, "Modbus TCP debug client operation failed");
        }

        /// <summary>
        ///     Names what went wrong the way a Modbus tool should: a protocol exception shows its code, a
        ///     timeout says so, everything else falls back to the message.
        /// </summary>
        private static string Describe(Exception ex)
        {
            return ex switch
            {
                ModbusException modbus => $"{modbus.ExceptionCode} (0x{(byte)modbus.ExceptionCode:X2}): {modbus.Message}",
                OperationTimeoutException => "Timeout — no response within the operation timeout.",
                OperationCanceledException => "Canceled before the response arrived.",
                _ => ex.Message,
            };
        }

        private static string FunctionCode(ReadFunction function)
        {
            return function switch
            {
                ReadFunction.Coils => "FC1",
                ReadFunction.DiscreteInputs => "FC2",
                ReadFunction.HoldingRegisters => "FC3",
                _ => "FC4",
            };
        }

        private static string WriteFunctionCode(WriteFunction function)
        {
            return function switch
            {
                WriteFunction.SingleCoil => "FC5",
                WriteFunction.MultipleCoils => "FC15",
                WriteFunction.SingleRegister => "FC6",
                _ => "FC16",
            };
        }

        /// <summary>
        ///     Registers occupied by one value. <paramref name="quantity" /> is only used for
        ///     <see cref="RegisterFieldType.String" />, which spans the whole response.
        /// </summary>
        private static int RegistersPerValue(RegisterFieldType fieldType, int quantity)
        {
            return fieldType switch
            {
                RegisterFieldType.UInt16 or RegisterFieldType.Int16 => 1,
                RegisterFieldType.UInt32 or RegisterFieldType.Int32 or RegisterFieldType.Float32 => 2,
                RegisterFieldType.UInt64 or RegisterFieldType.Int64 or RegisterFieldType.Float64 => 4,
                RegisterFieldType.String => quantity,
                _ => 0,
            };
        }

        private static RegisterFieldType ToRegisterFieldType(WatchFieldType fieldType)
        {
            return fieldType switch
            {
                WatchFieldType.UInt16 => RegisterFieldType.UInt16,
                WatchFieldType.Int16 => RegisterFieldType.Int16,
                WatchFieldType.UInt32 => RegisterFieldType.UInt32,
                WatchFieldType.Int32 => RegisterFieldType.Int32,
                WatchFieldType.Float32 => RegisterFieldType.Float32,
                WatchFieldType.UInt64 => RegisterFieldType.UInt64,
                WatchFieldType.Int64 => RegisterFieldType.Int64,
                _ => RegisterFieldType.Float64,
            };
        }

        private static string Format(object value)
        {
            return value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
        }

        private static IReadOnlyList<string> SplitValues(string value)
        {
            var parts = value.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

            if (parts.Count == 0)
            {
                throw new FormatException("Enter at least one value.");
            }

            return parts;
        }

        private static bool ParseBool(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "on" or "yes" => true,
                "0" or "false" or "off" or "no" => false,
                _ => throw new FormatException($"'{value}' is not a coil value — use true/false, 1/0, on/off."),
            };
        }

        /// <summary>
        ///     Parses an FC6 register value, accepting either a signed or an unsigned reading of the same
        ///     16 bits, plus <c>0x</c> hex. Returns a negative number when the value was written as signed.
        /// </summary>
        private static int ParseRegisterWord(string value)
        {
            var trimmed = value.Trim();

            if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(trimmed.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            var parsed = int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);

            if (parsed is < short.MinValue or > ushort.MaxValue)
            {
                throw new FormatException($"{parsed} does not fit in a 16-bit register ({short.MinValue}..{ushort.MaxValue}).");
            }

            return parsed;
        }

        private static ushort ParseUInt16(string value)
        {
            return ushort.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static short ParseInt16(string value)
        {
            return short.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static uint ParseUInt32(string value)
        {
            return uint.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static int ParseInt32(string value)
        {
            return int.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static ulong ParseUInt64(string value)
        {
            return ulong.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static long ParseInt64(string value)
        {
            return long.Parse(Normalize(value, out var hex), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static float ParseSingle(string value)
        {
            return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string value)
        {
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Normalize(string value, out bool hex)
        {
            var trimmed = value.Trim();
            hex = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase);

            return hex ? trimmed.Substring(2) : trimmed;
        }
    }

    /// <summary>
    ///     One register of a range read. The first three columns are the bytes exactly as they arrived; the
    ///     last is the decoded value where a field of the configured type begins.
    /// </summary>
    public readonly record struct RegisterRow(
        [StructField(Title = "Address", Description = "Protocol (base-0) register address.")]
        int Address,
        [StructField(Title = "Hex", Description = "The two register bytes in wire order.")]
        string Hex,
        [StructField(Title = "UInt16", Description = "The register read as an unsigned 16-bit integer.")]
        int Unsigned,
        [StructField(Title = "Int16", Description = "The register read as a signed 16-bit integer.")]
        int Signed,
        [StructField(Title = "Interpreted", Description = "The configured field type decoded from this register onwards; blank where no field starts.")]
        string Interpreted);

    /// <summary>
    ///     One coil or discrete input of a range read.
    /// </summary>
    public readonly record struct BitRow(
        [StructField(Title = "Address", Description = "Protocol (base-0) bit address.")]
        int Address,
        [StructField(Title = "Value")] bool Value);
}