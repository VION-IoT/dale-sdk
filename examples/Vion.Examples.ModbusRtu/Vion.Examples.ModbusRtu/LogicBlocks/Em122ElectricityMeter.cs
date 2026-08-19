using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Rtu;

namespace Vion.Examples.ModbusRtu.LogicBlocks
{
    /// <summary>
    ///     Demonstrates IModbusRtu usage against a Weidmüller EM122-RTU-2P 3-phase electricity meter.
    ///     Shows batch reads (contiguous register arrays), individual reads, holding register writes,
    ///     raw writes, and error handling patterns.
    /// </summary>
    [LogicBlock(Name = "EM122 Stromzähler", Icon = "flashlight-line")]
    public class Em122ElectricityMeter : LogicBlockBase
    {
        private readonly ILogger _logger;

        private float _demandPeriodMinutes = 60f;

        // ── Contract ──

        [ServiceProviderContractBinding(Identifier = "Modbus", DefaultName = "EM122 Modbus RTU")]
        public IModbusRtu Modbus { get; set; } = null!;

        // ── Configuration (Service Properties) ──

        [ServiceProperty(Title = "Modbus-Adresse")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int UnitId { get; set; } = 1;

        [ServiceProperty(Title = "Abfrage aktiv")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public bool PollingEnabled { get; set; } = true;

        [ServiceProperty(Title = "Bedarfsperiode", Unit = "min")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public float DemandPeriodMinutes
        {
            get => _demandPeriodMinutes;

            set
            {
                if (Math.Abs(_demandPeriodMinutes - value) > 0.01f)
                {
                    _demandPeriodMinutes = value;
                    WriteDemandPeriod(value);
                }
            }
        }

        // Action property: getter returns false; setter triggers the action. The bool-as-trigger
        // workaround pattern — UiHint = Trigger renders a button.
        [ServiceProperty(Title = "Energiezähler zurücksetzen")]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Trigger)]
        public bool ResetEnergyCounters
        {
            get => false;

            set
            {
                if (value)
                {
                    WriteResetEnergyCounters();
                }
            }
        }

        // ── Voltage (batch read: addr 0, count 3) ──

        [ServiceProperty(Title = "Spannung L1", Unit = "V")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public float VoltageL1 { get; private set; }

        [ServiceProperty(Title = "Spannung L2", Unit = "V")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float VoltageL2 { get; private set; }

        [ServiceProperty(Title = "Spannung L3", Unit = "V")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float VoltageL3 { get; private set; }

        // ── Current (batch read: addr 6, count 3) ──

        [ServiceProperty(Title = "Strom L1", Unit = "A")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public float CurrentL1 { get; private set; }

        [ServiceProperty(Title = "Strom L2", Unit = "A")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float CurrentL2 { get; private set; }

        [ServiceProperty(Title = "Strom L3", Unit = "A")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float CurrentL3 { get; private set; }

        // ── Active Power (batch read: addr 12, count 3) ──

        [ServiceProperty(Title = "Wirkleistung L1", Unit = "W")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float ActivePowerL1 { get; private set; }

        [ServiceProperty(Title = "Wirkleistung L2", Unit = "W")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float ActivePowerL2 { get; private set; }

        [ServiceProperty(Title = "Wirkleistung L3", Unit = "W")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float ActivePowerL3 { get; private set; }

        // ── System totals (individual reads) ──

        [ServiceProperty(Title = "Wirkleistung Gesamt", Unit = "W")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public float TotalActivePower { get; private set; }

        [ServiceProperty(Title = "Scheinleistung Gesamt", Unit = "VA")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float TotalApparentPower { get; private set; }

        [ServiceProperty(Title = "Blindleistung Gesamt", Unit = "VAr")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float TotalReactivePower { get; private set; }

        [ServiceProperty(Title = "Leistungsfaktor Gesamt")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status)]
        public float TotalPowerFactor { get; private set; }

        [ServiceProperty(Title = "Frequenz", Unit = "Hz")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public float Frequency { get; private set; }

        // ── Energy (individual reads) — cumulative counters; can reset via ResetEnergyCounters.
        // Mark as Total (not TotalIncreasing) because the reset path means they can decrease.

        [ServiceProperty(Title = "Bezug Energie", Unit = "kWh")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.Total)]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Primary)]
        public float ImportEnergy { get; private set; }

        [ServiceProperty(Title = "Lieferung Energie", Unit = "kWh")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.Total)]
        [Presentation(Group = PropertyGroup.Metric)]
        public float ExportEnergy { get; private set; }

        [ServiceProperty(Title = "Gesamtenergie", Unit = "kWh")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.Total)]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Secondary)]
        public float TotalEnergy { get; private set; }

        // ── Diagnostics ──

        [ServiceProperty(Title = "Erfolgreiche Abfragen")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public int ReadCount { get; private set; }

        [ServiceProperty(Title = "Letzter Kontakt",
                         Description = "Wann die letzte Antwort beobachtet wurde — aus der Quittung der Anfrage, nicht aus der Bearbeitungszeit dieses Blocks.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Format = Formats.Relative)]
        public DateTime? LastContactAt { get; private set; }

        [ServiceProperty(Title = "Fehlgeschlagene Abfragen")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public int ErrorCount { get; private set; }

        [ServiceProperty(Title = "Letzter Fehler")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string LastError { get; private set; } = "";

        // One [ServiceProperty] for the whole link: state, last contact, per-outcome counters and wire
        // latencies, all accumulated by the SDK from the receipts. ReadCount / ErrorCount above stay because
        // they count *this block's poll cycles* — Link counts transactions, and one cycle is eleven of them.
        [ServiceProperty(Title = "Verbindung", Description = "Der Zustand der Modbus-Verbindung zum Gerät, wie ihn der Client aus den Quittungen der Anfragen ermittelt.")]
        [Presentation(DisplayName = "Verbindung", Group = PropertyGroup.Diagnostics)]
        public ModbusLinkSummary Link { get; private set; }

        // ── Constructor ──

        public Em122ElectricityMeter(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        // ── Timer: poll all registers ──

        [Timer(2)]
        public void Poll()
        {
            if (!PollingEnabled)
            {
                return;
            }

            // ── Batch read: Phase voltages (input registers addr 0, 3 floats = 6 regs) ──
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             0,
                                             3,
                                             this,
                                             (values, receipt) =>
                                             {
                                                 VoltageL1 = values[0];
                                                 VoltageL2 = values[1];
                                                 VoltageL3 = values[2];
                                                 ReadCount++;
                                                 LastContactAt = receipt.ReceivedAt;
                                                 Link = Modbus.Link;
                                                 _logger.LogDebug("Spannungen: L1={V1:F1}V L2={V2:F1}V L3={V3:F1}V in {RoundTrip:F0} ms",
                                                                  values[0],
                                                                  values[1],
                                                                  values[2],
                                                                  receipt.RoundTrip.TotalMilliseconds);
                                             },
                                             OnError);

            // ── Batch read: Phase currents (input registers addr 6, 3 floats) ──
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             6,
                                             3,
                                             this,
                                             (values, _) =>
                                             {
                                                 CurrentL1 = values[0];
                                                 CurrentL2 = values[1];
                                                 CurrentL3 = values[2];
                                                 ReadCount++;
                                             },
                                             OnError);

            // ── Batch read: Phase active power (input registers addr 12, 3 floats) ──
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             12,
                                             3,
                                             this,
                                             (values, _) =>
                                             {
                                                 ActivePowerL1 = values[0];
                                                 ActivePowerL2 = values[1];
                                                 ActivePowerL3 = values[2];
                                                 ReadCount++;
                                             },
                                             OnError);

            // ── Individual reads: system totals (non-contiguous addresses) ──

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             52,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 TotalActivePower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             56,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 TotalApparentPower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             60,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 TotalReactivePower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             62,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 TotalPowerFactor = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             70,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 Frequency = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            // ── Energy counters ──

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             72,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 ImportEnergy = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             74,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 ExportEnergy = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             0x0156,
                                             1,
                                             this,
                                             (values, _) =>
                                             {
                                                 TotalEnergy = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);
        }

        // ── Lifecycle ──

        protected override void Ready()
        {
            Modbus.IsEnabled = true;
            _logger.LogInformation("EM122 Stromzähler bereit, Modbus-Adresse {UnitId}", UnitId);
        }

        // ── Holding register writes ──

        private void WriteDemandPeriod(float minutes)
        {
            // EM122 demand period is a Float (2 registers) at holding register addr 2
            Modbus.WriteMultipleHoldingRegistersAsFloat(UnitId,
                                                        2,
                                                        new[] { minutes },
                                                        this,
                                                        _ => _logger.LogInformation("Bedarfsperiode auf {Minutes} min gesetzt", minutes),
                                                        OnError);
        }

        private void WriteResetEnergyCounters()
        {
            // EM122 reset register at 0xF010: write 0x0003 to reset energy counters
            Modbus.WriteMultipleHoldingRegistersRaw(UnitId,
                                                    0xF010,
                                                    new byte[] { 0x00, 0x03 },
                                                    this,
                                                    _ => _logger.LogInformation("Energiezähler zurückgesetzt"),
                                                    OnError);
        }

        // ── Error handling ──

        private void OnError(Exception ex, ModbusReceipt receipt)
        {
            ErrorCount++;

            // The receipt classifies the failure — a device error, a timeout, or a request that never left
            // the queue — which the exception message alone does not always make obvious.
            LastError = $"{receipt.Outcome}: {ex.Message}";
            Link = Modbus.Link;
            _logger.LogWarning(ex, "Modbus-Fehler bei Adresse {UnitId}", UnitId);
        }
    }
}