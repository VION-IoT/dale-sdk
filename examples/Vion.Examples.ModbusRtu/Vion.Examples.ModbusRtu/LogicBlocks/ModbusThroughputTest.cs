using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Rtu;
using Microsoft.Extensions.Logging;

namespace Vion.Examples.ModbusRtu.LogicBlocks
{
    /// <summary>
    ///     Benchmarks Modbus RTU throughput by firing burst reads and measuring completions per second.
    ///     Demonstrates how batch size (number of registers per read) affects throughput.
    /// </summary>
    [LogicBlockInfo("Modbus Durchsatztest", "speed-line")]
    public class ModbusThroughputTest : LogicBlockBase
    {
        private readonly ILogger _logger;

        private DateTime _testStartTime;

        // ── Contract ──

        [ServiceProviderContract("Modbus", "Durchsatztest Modbus RTU")]
        public IModbusRtu Modbus { get; set; } = null!;

        // ── Configuration ──

        [ServiceProperty("Modbus-Adresse")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public int UnitId { get; set; } = 1;

        [ServiceProperty("Anzahl Register pro Lesevorgang")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public int RegisterCount { get; set; } = 1;

        [ServiceProperty("Anzahl Anfragen pro Burst")]
        [Category(PropertyCategory.Configuration)]
        [Display(group: "Konfiguration")]
        public int BurstSize { get; set; } = 100;

        [ServiceProperty("Test starten")]
        [Category(PropertyCategory.Action)]
        [Display(group: "Konfiguration")]
        public bool StartTest
        {
            get => false;

            set
            {
                if (value && !TestRunning)
                {
                    RunBurstTest();
                }
            }
        }

        // ── Results ──

        [ServiceProperty("Lesevorgänge pro Sekunde")]
        [Importance(Importance.Primary)]
        [Display(group: "Ergebnis")]
        public double ReadsPerSecond { get; private set; }

        [ServiceProperty("Durchschnittliche Latenz", "ms")]
        [Display(group: "Ergebnis")]
        public double AverageLatencyMs { get; private set; }

        [ServiceProperty("Erfolgreiche Anfragen")]
        [Display(group: "Ergebnis")]
        public int CompletedReads { get; private set; }

        [ServiceProperty("Fehlgeschlagene Anfragen")]
        [Display(group: "Ergebnis")]
        public int FailedReads { get; private set; }

        [ServiceProperty("Test läuft")]
        [Display(group: "Ergebnis")]
        public bool TestRunning { get; private set; }

        [ServiceProperty("Letzte Testdauer", "ms")]
        [Display(group: "Ergebnis")]
        public double LastTestDurationMs { get; private set; }

        // ── Constructor ──

        public ModbusThroughputTest(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        // ── Lifecycle ──

        protected override void Ready()
        {
            Modbus.IsEnabled = true;
            _logger.LogInformation("Modbus Durchsatztest bereit, Modbus-Adresse {UnitId}", UnitId);
        }

        // ── Burst test ──

        private void RunBurstTest()
        {
            TestRunning = true;
            CompletedReads = 0;
            FailedReads = 0;
            _testStartTime = DateTime.UtcNow;

            var burstSize = BurstSize;
            var registerCount = (uint)Math.Max(1, RegisterCount);

            _logger.LogInformation("Starte Durchsatztest: {BurstSize} Anfragen, {RegisterCount} Register pro Anfrage", burstSize, registerCount);

            for (var i = 0; i < burstSize; i++)
            {
                // Read input registers starting at address 0 (phase voltages — always available)
                Modbus.ReadInputRegistersAsFloat(UnitId, 0, registerCount, _ => OnReadCompleted(burstSize), ex => OnReadFailed(burstSize, ex));
            }
        }

        private void OnReadCompleted(int burstSize)
        {
            CompletedReads++;
            CheckTestComplete(burstSize);
        }

        private void OnReadFailed(int burstSize, Exception ex)
        {
            FailedReads++;
            _logger.LogDebug(ex, "Lesefehler im Durchsatztest");
            CheckTestComplete(burstSize);
        }

        private void CheckTestComplete(int burstSize)
        {
            var total = CompletedReads + FailedReads;

            if (total < burstSize)
            {
                return;
            }

            var elapsed = DateTime.UtcNow - _testStartTime;
            LastTestDurationMs = elapsed.TotalMilliseconds;
            ReadsPerSecond = elapsed.TotalSeconds > 0 ? CompletedReads / elapsed.TotalSeconds : 0;
            AverageLatencyMs = CompletedReads > 0 ? elapsed.TotalMilliseconds / CompletedReads : 0;

            TestRunning = false;

            _logger.LogInformation("Durchsatztest abgeschlossen: {Completed}/{Total} erfolgreich in {Duration:F0}ms ({Rps:F1} Lesevorgänge/s, {Latency:F1}ms Latenz)",
                                   CompletedReads,
                                   total,
                                   elapsed.TotalMilliseconds,
                                   ReadsPerSecond,
                                   AverageLatencyMs);
        }
    }
}