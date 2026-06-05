using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Diagnostics;
using Xunit;

namespace Vion.Diagnostics.Test
{
    public class DiagnosticsProjectionShould
    {
        private static readonly DiagnosticsThresholds T = new(50, 500, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1));

        private static ActorVitals LogicBlock(string name,
                                              string type,
                                              long messages = 0,
                                              long errors = 0,
                                              double handlerMaxMs = 0,
                                              int mailbox = 0)
        {
            return new ActorVitals(name,
                                   new ActorIdentity(ActorCategory.LogicBlock, type, "Lib"),
                                   messages,
                                   errors,
                                   TimeSpan.FromMilliseconds(handlerMaxMs),
                                   TimeSpan.Zero,
                                   mailbox,
                                   mailbox,
                                   TimeSpan.Zero,
                                   TimeSpan.Zero,
                                   DateTimeOffset.UnixEpoch);
        }

        private static ActorVitals Runtime(string role, long errors = 0, int mailbox = 0)
        {
            return new ActorVitals(role,
                                   new ActorIdentity(ActorCategory.Runtime, role, null),
                                   0,
                                   errors,
                                   TimeSpan.Zero,
                                   TimeSpan.Zero,
                                   mailbox,
                                   mailbox,
                                   TimeSpan.Zero,
                                   TimeSpan.Zero,
                                   DateTimeOffset.UnixEpoch);
        }

        [Fact]
        public void ComputeMessageRatePerSecondFromTheDelta()
        {
            var prior = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater", 90) };
            var current = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater", 100) };

            var result = DiagnosticsProjection.Project(prior, current, TimeSpan.FromSeconds(2));

            Assert.Equal(5.0, Assert.Single(result.LogicBlocks).MessageRatePerSec);
        }

        [Fact]
        public void ComputePublishErrorsPerSecondFromThePublisherDeltas()
        {
            var prior = new List<ActorVitals>
                        {
                            Runtime("ServicePropertyHandler", 10),
                            Runtime("ServiceMeasuringPointHandler"),
                        };
            var current = new List<ActorVitals>
                          {
                              Runtime("ServicePropertyHandler", 14),
                              Runtime("ServiceMeasuringPointHandler", 2),
                          };

            var result = DiagnosticsProjection.Project(prior, current, TimeSpan.FromSeconds(2));

            Assert.Equal(3.0, result.RuntimeHealth.PublishErrorsPerSec);
        }

        [Fact]
        public void DeriveRuntimeBacklogsFromTheRuntimeActors()
        {
            var snapshot = new List<ActorVitals>
                           {
                               Runtime("MqttClient", mailbox: 7),
                               Runtime("ServicePropertyHandler", mailbox: 3),
                               Runtime("ServiceMeasuringPointHandler", mailbox: 2),
                               LogicBlock("logicblock_Heater_1", "Heater", mailbox: 99),
                           };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1));

            Assert.Equal(7, result.RuntimeHealth.MqttIngressBacklog);
            Assert.Equal(5, result.RuntimeHealth.PublisherBacklog);
        }

        [Fact]
        public void ExcludeRuntimeActorsFromTheLogicBlockTable()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater"), Runtime("MqttClient") };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1));

            Assert.Equal("logicblock_Heater_1", Assert.Single(result.LogicBlocks).LogicBlockName);
        }

        [Fact]
        public void IncludeOnlyLogicBlocksMatchingTheFilter()
        {
            var snapshot = new List<ActorVitals>
                           {
                               LogicBlock("logicblock_Heater_1", "Heater"),
                               LogicBlock("logicblock_Pump_2", "Pump"),
                           };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), "Heater");

            Assert.Equal("logicblock_Heater_1", Assert.Single(result.LogicBlocks).LogicBlockName);
        }

        [Fact]
        public void MarkCriticalWhenMailboxReachesTheCriticalThreshold()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", mailbox: 500) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(LogicBlockHealth.Critical, Assert.Single(result.LogicBlocks).Health);
        }

        [Fact]
        public void MarkOkAndHealthyWhenWithinAllThresholds()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", mailbox: 5, handlerMaxMs: 10) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(LogicBlockHealth.Ok, Assert.Single(result.LogicBlocks).Health);
            Assert.Equal(DiagnosticsStatus.Healthy, result.Status);
        }

        [Fact]
        public void MarkWarningWhenErrorsIncreasedSinceTheLastTick()
        {
            var prior = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", errors: 2) };
            var current = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", errors: 5) };

            var result = DiagnosticsProjection.Project(prior, current, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(LogicBlockHealth.Warning, Assert.Single(result.LogicBlocks).Health);
        }

        [Fact]
        public void MarkWarningWhenHandlerDurationReachesTheWarnThreshold()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", handlerMaxMs: 100) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(LogicBlockHealth.Warning, Assert.Single(result.LogicBlocks).Health);
        }

        [Fact]
        public void MarkWarningWhenMailboxReachesTheWarnThreshold()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", mailbox: 50) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(LogicBlockHealth.Warning, Assert.Single(result.LogicBlocks).Health);
        }

        [Fact]
        public void ProjectALogicBlockActorIntoARow()
        {
            var lastActivity = new DateTimeOffset(2026,
                                                  6,
                                                  5,
                                                  8,
                                                  0,
                                                  0,
                                                  TimeSpan.Zero);
            var snapshot = new List<ActorVitals>
                           {
                               new("logicblock_Heater_1",
                                   new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Vion.Examples.Energy"),
                                   100,
                                   3,
                                   TimeSpan.FromMilliseconds(12),
                                   TimeSpan.FromMilliseconds(500),
                                   4,
                                   9,
                                   TimeSpan.Zero,
                                   TimeSpan.Zero,
                                   lastActivity),
                           };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1));

            var row = Assert.Single(result.LogicBlocks);
            Assert.Equal("logicblock_Heater_1", row.LogicBlockName);
            Assert.Equal(TimeSpan.FromMilliseconds(12), row.HandlerDurationMax);
            Assert.Equal(4, row.MailboxDepth);
            Assert.Equal(3, row.Errors);
            Assert.Equal(lastActivity.UtcDateTime, row.LastActivityUtc);
        }

        [Fact]
        public void ReportNullLastActivityForANeverActiveBlock()
        {
            var idle = new ActorVitals("logicblock_Idle_1",
                                       new ActorIdentity(ActorCategory.LogicBlock, "Idle", "Lib"),
                                       0,
                                       0,
                                       TimeSpan.Zero,
                                       TimeSpan.Zero,
                                       0,
                                       0,
                                       TimeSpan.Zero,
                                       TimeSpan.Zero,
                                       default);
            var snapshot = new List<ActorVitals> { idle };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1));

            Assert.Null(Assert.Single(result.LogicBlocks).LastActivityUtc);
        }

        [Fact]
        public void ReportZeroRateAfterACounterReset()
        {
            var prior = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater", 100) };
            var current = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater", 10) };

            var result = DiagnosticsProjection.Project(prior, current, TimeSpan.FromSeconds(2));

            Assert.Equal(0.0, Assert.Single(result.LogicBlocks).MessageRatePerSec);
        }

        [Fact]
        public void ReportZeroRateWhenThereIsNoPriorSample()
        {
            var current = new List<ActorVitals> { LogicBlock("logicblock_Heater_1", "Heater", 100) };

            var result = DiagnosticsProjection.Project(new List<ActorVitals>(), current, TimeSpan.FromSeconds(2));

            Assert.Equal(0.0, Assert.Single(result.LogicBlocks).MessageRatePerSec);
        }

        [Fact]
        public void RollUpStatusToDegradedWhenAnyLogicBlockIsWarning()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", mailbox: 60), LogicBlock("logicblock_B_2", "B", mailbox: 1) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(DiagnosticsStatus.Degraded, result.Status);
        }

        [Fact]
        public void RollUpStatusToOverloadedWhenAnyLogicBlockIsCritical()
        {
            var snapshot = new List<ActorVitals> { LogicBlock("logicblock_A_1", "A", mailbox: 600), LogicBlock("logicblock_B_2", "B", mailbox: 1) };

            var result = DiagnosticsProjection.Project(snapshot, snapshot, TimeSpan.FromSeconds(1), thresholds: T);

            Assert.Equal(DiagnosticsStatus.Overloaded, result.Status);
        }
    }
}