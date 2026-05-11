using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Rtu;
using Microsoft.Extensions.Logging;

namespace Vion.Examples.ModbusRtu.LogicBlocks
{
    /// <summary>
    ///     Demonstrates IModbusRtu usage against a Weidmüller EM122-RTU-2P 3-phase electricity meter.
    ///     Shows batch reads (contiguous register arrays), individual reads, holding register writes,
    ///     raw writes, and error handling patterns.
    /// </summary>
    [LogicBlockInfo("EM122 Stromzähler", "flashlight-line")]
    public class Em122ElectricityMeter : LogicBlockBase
    {
        private readonly ILogger _logger;

        private float _demandPeriodMinutes = 60f;

        // ── Contract ──

        [ServiceProviderContract("Modbus", "EM122 Modbus RTU")]
        public IModbusRtu Modbus { get; set; } = null!;

        // ── Configuration (Service Properties) ──

        [ServiceProperty(Title = "Modbus-Adresse")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public int UnitId { get; set; } = 1;

        [ServiceProperty(Title = "Abfrage aktiv")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public bool PollingEnabled { get; set; } = true;

        [ServiceProperty(Title = "Bedarfsperiode", Unit = "min")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
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

        [ServiceProperty(Title = "Energiezähler zurücksetzen")]
        [Category(PropertyCategory.Action)]
        [Display(group: "Konfiguration")]
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
        [ServiceMeasuringPoint(Title = "Spannung L1", Unit = "V")]
        [Importance(Importance.Primary)]
        [Display(group: "Spannung")]
        public float VoltageL1 { get; private set; }

        [ServiceProperty(Title = "Spannung L2", Unit = "V")]
        [ServiceMeasuringPoint(Title = "Spannung L2", Unit = "V")]
        [Display(group: "Spannung")]
        public float VoltageL2 { get; private set; }

        [ServiceProperty(Title = "Spannung L3", Unit = "V")]
        [ServiceMeasuringPoint(Title = "Spannung L3", Unit = "V")]
        [Display(group: "Spannung")]
        public float VoltageL3 { get; private set; }

        // ── Current (batch read: addr 6, count 3) ──

        [ServiceProperty(Title = "Strom L1", Unit = "A")]
        [ServiceMeasuringPoint(Title = "Strom L1", Unit = "A")]
        [Importance(Importance.Primary)]
        [Display(group: "Strom")]
        public float CurrentL1 { get; private set; }

        [ServiceProperty(Title = "Strom L2", Unit = "A")]
        [ServiceMeasuringPoint(Title = "Strom L2", Unit = "A")]
        [Display(group: "Strom")]
        public float CurrentL2 { get; private set; }

        [ServiceProperty(Title = "Strom L3", Unit = "A")]
        [ServiceMeasuringPoint(Title = "Strom L3", Unit = "A")]
        [Display(group: "Strom")]
        public float CurrentL3 { get; private set; }

        // ── Active Power (batch read: addr 12, count 3) ──

        [ServiceProperty(Title = "Wirkleistung L1", Unit = "W")]
        [ServiceMeasuringPoint(Title = "Wirkleistung L1", Unit = "W")]
        [Display(group: "Leistung")]
        public float ActivePowerL1 { get; private set; }

        [ServiceProperty(Title = "Wirkleistung L2", Unit = "W")]
        [ServiceMeasuringPoint(Title = "Wirkleistung L2", Unit = "W")]
        [Display(group: "Leistung")]
        public float ActivePowerL2 { get; private set; }

        [ServiceProperty(Title = "Wirkleistung L3", Unit = "W")]
        [ServiceMeasuringPoint(Title = "Wirkleistung L3", Unit = "W")]
        [Display(group: "Leistung")]
        public float ActivePowerL3 { get; private set; }

        // ── System totals (individual reads) ──

        [ServiceProperty(Title = "Wirkleistung Gesamt", Unit = "W")]
        [ServiceMeasuringPoint(Title = "Wirkleistung Gesamt", Unit = "W")]
        [Importance(Importance.Primary)]
        [Display(group: "Leistung")]
        public float TotalActivePower { get; private set; }

        [ServiceProperty(Title = "Scheinleistung Gesamt", Unit = "VA")]
        [ServiceMeasuringPoint(Title = "Scheinleistung Gesamt", Unit = "VA")]
        [Display(group: "Leistung")]
        public float TotalApparentPower { get; private set; }

        [ServiceProperty(Title = "Blindleistung Gesamt", Unit = "VAr")]
        [ServiceMeasuringPoint(Title = "Blindleistung Gesamt", Unit = "VAr")]
        [Display(group: "Leistung")]
        public float TotalReactivePower { get; private set; }

        [ServiceProperty(Title = "Leistungsfaktor Gesamt")]
        [ServiceMeasuringPoint(Title = "Leistungsfaktor Gesamt")]
        [Display(group: "Netzqualität")]
        public float TotalPowerFactor { get; private set; }

        [ServiceProperty(Title = "Frequenz", Unit = "Hz")]
        [ServiceMeasuringPoint(Title = "Frequenz", Unit = "Hz")]
        [Importance(Importance.Secondary)]
        [Display(group: "Netzqualität")]
        public float Frequency { get; private set; }

        // ── Energy (individual reads) ──

        [ServiceProperty(Title = "Bezug Energie", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Bezug Energie", Unit = "kWh")]
        [Importance(Importance.Primary)]
        [Display(group: "Energie")]
        public float ImportEnergy { get; private set; }

        [ServiceProperty(Title = "Lieferung Energie", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Lieferung Energie", Unit = "kWh")]
        [Display(group: "Energie")]
        public float ExportEnergy { get; private set; }

        [ServiceProperty(Title = "Gesamtenergie", Unit = "kWh")]
        [ServiceMeasuringPoint(Title = "Gesamtenergie", Unit = "kWh")]
        [Importance(Importance.Secondary)]
        [Display(group: "Energie")]
        public float TotalEnergy { get; private set; }

        // ── Diagnostics ──

        [ServiceProperty(Title = "Erfolgreiche Abfragen")]
        [Display(group: "Diagnose")]
        public int ReadCount { get; private set; }

        [ServiceProperty(Title = "Fehlgeschlagene Abfragen")]
        [Display(group: "Diagnose")]
        public int ErrorCount { get; private set; }

        [ServiceProperty(Title = "Letzter Fehler")]
        [Display(group: "Diagnose")]
        public string LastError { get; private set; } = "";

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
                                             values =>
                                             {
                                                 VoltageL1 = values[0];
                                                 VoltageL2 = values[1];
                                                 VoltageL3 = values[2];
                                                 ReadCount++;
                                                 _logger.LogDebug("Spannungen: L1={V1:F1}V L2={V2:F1}V L3={V3:F1}V", values[0], values[1], values[2]);
                                             },
                                             OnError);

            // ── Batch read: Phase currents (input registers addr 6, 3 floats) ──
            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             6,
                                             3,
                                             values =>
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
                                             values =>
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
                                             values =>
                                             {
                                                 TotalActivePower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             56,
                                             1,
                                             values =>
                                             {
                                                 TotalApparentPower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             60,
                                             1,
                                             values =>
                                             {
                                                 TotalReactivePower = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             62,
                                             1,
                                             values =>
                                             {
                                                 TotalPowerFactor = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             70,
                                             1,
                                             values =>
                                             {
                                                 Frequency = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            // ── Energy counters ──

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             72,
                                             1,
                                             values =>
                                             {
                                                 ImportEnergy = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             74,
                                             1,
                                             values =>
                                             {
                                                 ExportEnergy = values[0];
                                                 ReadCount++;
                                             },
                                             OnError);

            Modbus.ReadInputRegistersAsFloat(UnitId,
                                             0x0156,
                                             1,
                                             values =>
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
            Modbus.WriteMultipleHoldingRegistersAsFloat(UnitId, 2, new[] { minutes }, () => _logger.LogInformation("Bedarfsperiode auf {Minutes} min gesetzt", minutes), OnError);
        }

        private void WriteResetEnergyCounters()
        {
            // EM122 reset register at 0xF010: write 0x0003 to reset energy counters
            Modbus.WriteMultipleHoldingRegistersRaw(UnitId, 0xF010, new byte[] { 0x00, 0x03 }, () => _logger.LogInformation("Energiezähler zurückgesetzt"), OnError);
        }

        // ── Error handling ──

        private void OnError(Exception ex)
        {
            ErrorCount++;
            LastError = ex.Message;
            _logger.LogWarning(ex, "Modbus-Fehler bei Adresse {UnitId}", UnitId);
        }
    }
}