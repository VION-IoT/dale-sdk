using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Diagnostics
{
    /// <summary>
    ///     Pure projection of the SDK vitals snapshot (<see cref="IRuntimeDiagnostics" />) into the
    ///     diagnostics block's dashboard surface (RFC 0005 Sink 2). Stateless by design: the block holds
    ///     the prior snapshot and passes it in, so per-second rates can be diffed without the projection
    ///     carrying state — which keeps it trivially testable.
    /// </summary>
    public static class DiagnosticsProjection
    {
        // Well-known dale runtime-actor roles (= their receiver class names; see ActorIdentity.For).
        // RuntimeHealth couples to these names: if dale renames the classes this silently reads 0.
        // A future hardening is an SDK-exposed well-known-role marker instead of name matching.
        private const string MqttClientRole = "MqttClient";

        private const string ServicePropertyHandlerRole = "ServicePropertyHandler";

        private const string ServiceMeasuringPointHandlerRole = "ServiceMeasuringPointHandler";

        public static DiagnosticsResult Project(IReadOnlyList<ActorVitals> prior,
                                                IReadOnlyList<ActorVitals> current,
                                                TimeSpan elapsed,
                                                string? filter = null,
                                                DiagnosticsThresholds? thresholds = null)
        {
            var priorByName = BuildLookup(prior);
            var t = thresholds ?? DiagnosticsThresholds.Default;

            var logicBlocks = ImmutableArray.CreateBuilder<LogicBlockVitals>();
            foreach (var vitals in current)
            {
                if (vitals.Identity?.Category != ActorCategory.LogicBlock)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(filter) && !Regex.IsMatch(vitals.ActorName, filter))
                {
                    continue;
                }

                logicBlocks.Add(new LogicBlockVitals(vitals.ActorName,
                                                     RatePerSecond(priorByName, vitals, v => v.MessagesHandled, elapsed),
                                                     vitals.HandlerDurationMax,
                                                     vitals.MailboxDepth,
                                                     (int)vitals.Errors,
                                                     vitals.LastActivityUtc == default ? null : vitals.LastActivityUtc.UtcDateTime,
                                                     HealthFor(vitals, priorByName, t)));
            }

            var rows = logicBlocks.ToImmutable();
            var runtime = ProjectRuntime(priorByName, current, elapsed);

            return new DiagnosticsResult(rows, runtime, RollUp(rows, runtime, t));
        }

        private static LogicBlockHealth HealthFor(ActorVitals current, Dictionary<string, ActorVitals> priorByName, DiagnosticsThresholds t)
        {
            if (current.MailboxDepth >= t.CriticalMailboxDepth || current.HandlerDurationMax >= t.CriticalHandlerDuration)
            {
                return LogicBlockHealth.Critical;
            }

            var errorsIncreased = priorByName.TryGetValue(current.ActorName, out var prior) && current.Errors > prior.Errors;
            if (current.MailboxDepth >= t.WarnMailboxDepth || current.HandlerDurationMax >= t.WarnHandlerDuration || errorsIncreased)
            {
                return LogicBlockHealth.Warning;
            }

            return LogicBlockHealth.Ok;
        }

        private static DiagnosticsStatus RollUp(ImmutableArray<LogicBlockVitals> rows, RuntimeHealth runtime, DiagnosticsThresholds t)
        {
            var critical = runtime.MqttIngressBacklog >= t.CriticalMailboxDepth || runtime.PublisherBacklog >= t.CriticalMailboxDepth;
            var warning = runtime.MqttIngressBacklog >= t.WarnMailboxDepth || runtime.PublisherBacklog >= t.WarnMailboxDepth;

            foreach (var row in rows)
            {
                if (row.Health == LogicBlockHealth.Critical)
                {
                    critical = true;
                }
                else if (row.Health == LogicBlockHealth.Warning)
                {
                    warning = true;
                }
            }

            return critical ? DiagnosticsStatus.Overloaded : warning ? DiagnosticsStatus.Degraded : DiagnosticsStatus.Healthy;
        }

        private static RuntimeHealth ProjectRuntime(Dictionary<string, ActorVitals> priorByName, IReadOnlyList<ActorVitals> current, TimeSpan elapsed)
        {
            var mqttIngressBacklog = 0;
            var publisherBacklog = 0;
            var publishErrorsPerSec = 0.0;

            foreach (var vitals in current)
            {
                if (vitals.Identity?.Category != ActorCategory.Runtime)
                {
                    continue;
                }

                switch (vitals.Identity.Type)
                {
                    case MqttClientRole:
                        mqttIngressBacklog = vitals.MailboxDepth;
                        break;
                    case ServicePropertyHandlerRole:
                    case ServiceMeasuringPointHandlerRole:
                        publisherBacklog += vitals.MailboxDepth;
                        publishErrorsPerSec += RatePerSecond(priorByName, vitals, v => v.Errors, elapsed);
                        break;
                }
            }

            return new RuntimeHealth(mqttIngressBacklog, publisherBacklog, publishErrorsPerSec);
        }

        private static Dictionary<string, ActorVitals> BuildLookup(IReadOnlyList<ActorVitals> snapshot)
        {
            var byName = new Dictionary<string, ActorVitals>(snapshot.Count);
            foreach (var vitals in snapshot)
            {
                byName[vitals.ActorName] = vitals;
            }

            return byName;
        }

        private static double RatePerSecond(Dictionary<string, ActorVitals> priorByName, ActorVitals current, Func<ActorVitals, long> counter, TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds <= 0 || !priorByName.TryGetValue(current.ActorName, out var prior))
            {
                return 0;
            }

            var delta = counter(current) - counter(prior);
            return delta <= 0 ? 0 : delta / elapsed.TotalSeconds;
        }
    }
}