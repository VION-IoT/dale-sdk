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
    [LogicBlock(Name = "Modbus Durchsatztest", Icon = "speed-line")]
    public class ModbusThroughputTest : LogicBlockBase
    {
        private readonly ILogger _logger;

        private DateTime _testStartTime;

        // ── Contract ──

        [ServiceProviderContractBinding(Identifier = "Modbus", DefaultName = "Durchsatztest Modbus RTU")]
        public IModbusRtu Modbus { get; set; } = null!;

        // ── Configuration ──

        [ServiceProperty(Title = "Modbus-Adresse")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int UnitId { get; set; } = 1;

        [ServiceProperty(Title = "Anzahl Register pro Lesevorgang")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int RegisterCount { get; set; } = 1;

        [ServiceProperty(Title = "Anzahl Anfragen pro Burst")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int BurstSize { get; set; } = 100;

        // Trigger workaround: getter returns false; setter starts the test.
        [ServiceProperty(Title = "Test starten")]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Trigger)]
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

        // ── Results (Metric group — measurements + counters) ──

        [ServiceProperty(Title = "Lesevorgänge pro Sekunde")]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Primary)]
        public double ReadsPerSecond { get; private set; }

        [ServiceProperty(Title = "Durchschnittliche Latenz", Unit = "ms")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double AverageLatencyMs { get; private set; }

        [ServiceProperty(Title = "Erfolgreiche Anfragen")]
        [Presentation(Group = PropertyGroup.Metric)]
        public int CompletedReads { get; private set; }

        [ServiceProperty(Title = "Fehlgeschlagene Anfragen")]
        [Presentation(Group = PropertyGroup.Metric)]
        public int FailedReads { get; private set; }

        [ServiceProperty(Title = "Test läuft")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool TestRunning { get; private set; }

        [ServiceProperty(Title = "Letzte Testdauer", Unit = "ms")]
        [Presentation(Group = PropertyGroup.Metric)]
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