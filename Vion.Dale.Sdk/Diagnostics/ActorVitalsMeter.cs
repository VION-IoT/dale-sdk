using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     RFC 0005 Sink 1: exposes the vitals core as OpenTelemetry metrics under the
    ///     <see cref="MeterName" /> meter. All instruments are <b>observable</b> — their callbacks read
    ///     <see cref="IRuntimeDiagnostics.Snapshot" /> at each export tick, so the core stays the single
    ///     readable source. Measurements are tagged per actor; the runtime (dale) shapes cardinality into
    ///     fleet/focus tiers via metric-view <c>TagKeys</c> — this type emits the full tag set.
    /// </summary>
    public sealed class ActorVitalsMeter : IDisposable
    {
        /// <summary>The meter name the runtime subscribes to via AddVionTelemetryExport's MeterNames.</summary>
        public const string MeterName = "Vion.Dale.Actors";

        private readonly Meter _meter;

        public ActorVitalsMeter(IRuntimeDiagnostics diagnostics)
        {
            _meter = new Meter(MeterName);

            _meter.CreateObservableCounter("vion.actor.messages_handled",
                                           () => Counts(diagnostics, v => v.MessagesHandled),
                                           unit: "{message}",
                                           description: "Messages handled per actor since start.");
            _meter.CreateObservableCounter("vion.actor.errors",
                                           () => Counts(diagnostics, v => v.Errors),
                                           unit: "{error}",
                                           description: "Handler exceptions per actor since start.");
            _meter.CreateObservableCounter("vion.actor.handler_duration_total",
                                           () => Seconds(diagnostics, v => v.HandlerDurationTotal),
                                           unit: "s",
                                           description: "Cumulative handler time per actor; rate() is the busy fraction, and total / messages_handled is the mean handler duration.");
            _meter.CreateObservableGauge("vion.actor.handler_duration_max",
                                         () => Milliseconds(diagnostics, v => v.HandlerDurationMax),
                                         unit: "ms",
                                         description: "Max handler duration per actor over the recent window.");
            _meter.CreateObservableGauge("vion.actor.mailbox_depth",
                                         () => Counts(diagnostics, v => (long)v.MailboxDepth),
                                         unit: "{message}",
                                         description: "Current mailbox depth (posted minus received) per actor.");
            _meter.CreateObservableGauge("vion.actor.mailbox_depth_max",
                                         () => Counts(diagnostics, v => (long)v.MailboxDepthMax),
                                         unit: "{message}",
                                         description: "Peak mailbox depth per actor over the recent window.");
            _meter.CreateObservableGauge("vion.actor.timer_callback_duration_max",
                                         () => Milliseconds(diagnostics, v => v.TimerCallbackDurationMax),
                                         unit: "ms",
                                         description: "Max [Timer] callback duration per actor over the recent window.");
            _meter.CreateObservableGauge("vion.actor.timer_jitter_max",
                                         () => Milliseconds(diagnostics, v => v.TimerJitterMax),
                                         unit: "ms",
                                         description: "Max [Timer] scheduler jitter per actor over the recent window.");
        }

        public void Dispose()
        {
            _meter.Dispose();
        }

        private static IEnumerable<Measurement<long>> Counts(IRuntimeDiagnostics diagnostics, Func<ActorVitals, long> selector)
        {
            return diagnostics.Snapshot().Select(v => new Measurement<long>(selector(v), TagsFor(v)));
        }

        private static IEnumerable<Measurement<double>> Milliseconds(IRuntimeDiagnostics diagnostics, Func<ActorVitals, TimeSpan> selector)
        {
            return diagnostics.Snapshot().Select(v => new Measurement<double>(selector(v).TotalMilliseconds, TagsFor(v)));
        }

        private static IEnumerable<Measurement<double>> Seconds(IRuntimeDiagnostics diagnostics, Func<ActorVitals, TimeSpan> selector)
        {
            return diagnostics.Snapshot().Select(v => new Measurement<double>(selector(v).TotalSeconds, TagsFor(v)));
        }

        private static KeyValuePair<string, object?>[] TagsFor(ActorVitals vitals)
        {
            var identity = vitals.Identity;
            if (identity == null)
            {
                return new[]
                {
                    new KeyValuePair<string, object?>("actor.kind", "unknown"),
                    new KeyValuePair<string, object?>("actor.id", vitals.ActorName),
                };
            }

            if (identity.Category == ActorCategory.LogicBlock)
            {
                return new[]
                {
                    new KeyValuePair<string, object?>("actor.kind", "logic-block"),
                    new KeyValuePair<string, object?>("logicblock.type", identity.Type),
                    new KeyValuePair<string, object?>("logicblock.id", vitals.ActorName),
                    new KeyValuePair<string, object?>("library", identity.Library ?? string.Empty),
                };
            }

            return new[]
            {
                new KeyValuePair<string, object?>("actor.kind", "runtime"),
                new KeyValuePair<string, object?>("role", identity.Type),
            };
        }
    }
}
