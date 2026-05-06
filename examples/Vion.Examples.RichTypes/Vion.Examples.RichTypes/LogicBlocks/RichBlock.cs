using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.RichTypes.LogicBlocks
{
    /// <summary>
    ///     Demonstrates every rich-types service-property / measuring-point shape introduced in PR 2.
    ///     Used by Vion.Examples.RichTypes.DevHost to inspect the emitted introspection JSON
    ///     and exercise the schema/presentation/runtime three-doc shape.
    /// </summary>
    [LogicBlockInfo("Rich Types Demo", "device-line")]
    public class RichBlock : LogicBlockBase
    {
        // ── Primitives — including the new unsigned set ────────────────────────────
        [ServiceProperty]
        public bool Enabled { get; set; }

        [ServiceProperty]
        public byte ByteCounter { get; set; }

        [ServiceProperty]
        public ushort UShortRegister { get; set; }

        [ServiceProperty]
        public uint UIntCounter { get; set; }

        [ServiceProperty]
        public long LongCounter { get; set; }

        [ServiceProperty(Title = "Spannungs-Sollwert", Unit = "V", Minimum = 0, Maximum = 400)]
        public double VoltageSetpoint { get; set; }

        // ── Nullable primitives ───────────────────────────────────────────────────
        [ServiceProperty(Title = "Optionaler Sollwert", Unit = "kW")]
        public double? OptionalTarget { get; set; }

        [ServiceProperty]
        public int? OptionalCount { get; set; }

        // ── Strings ────────────────────────────────────────────────────────────────
        [ServiceProperty(Title = "Gerätename")]
        public string DeviceName { get; set; } = "demo";

        [ServiceMeasuringPoint(Title = "Letzter Fehler")]
        public string? LastError { get; private set; }

        // ── ImmutableArray<T> ──────────────────────────────────────────────────────
        [ServiceMeasuringPoint(Unit = "A")]
        public ImmutableArray<double> HistogramBuckets { get; private set; } = ImmutableArray<double>.Empty;

        [ServiceProperty]
        public ImmutableArray<int> ScheduleHours { get; set; } = ImmutableArray<int>.Empty;

        [ServiceProperty(Title = "Messreihe mit Lücken", Unit = "kW")]
        public ImmutableArray<double?> SamplesWithGaps { get; set; } = ImmutableArray<double?>.Empty;

        [ServiceProperty]
        public ImmutableArray<int?> CountsWithGaps { get; set; } = ImmutableArray<int?>.Empty;

        // ── Structs (readonly record struct) ───────────────────────────────────────
        [ServiceMeasuringPoint(Title = "Position")]
        public Coordinates CurrentLocation { get; private set; }

        [ServiceMeasuringPoint(Title = "Letzte bekannte Position")]
        public Coordinates? LastKnownLocation { get; private set; }

        [ServiceProperty(Title = "Bevorzugte Position")]
        public Coordinates? PreferredLocation { get; set; }

        [ServiceMeasuringPoint(Title = "Route")]
        public ImmutableArray<Coordinates> Route { get; private set; } = ImmutableArray<Coordinates>.Empty;

        [ServiceProperty(Title = "Sollwert-Plan")]
        public ImmutableArray<ScheduledSetpoint> Schedule { get; set; } = ImmutableArray<ScheduledSetpoint>.Empty;

        // ── Enums ──────────────────────────────────────────────────────────────────
        [ServiceMeasuringPoint(Title = "Aktueller Alarm")]
        [StatusIndicator]
        public AlarmState CurrentAlarm { get; private set; }

        [ServiceProperty(Title = "Bevorzugter Modus")]
        public AlarmState? PreferredAlarm { get; set; }

        public RichBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public readonly record struct Coordinates(
        [StructField(Unit = "deg", Minimum = -90, Maximum = 90)]
        double Lat,
        [StructField(Unit = "deg", Minimum = -180, Maximum = 180)]
        double Lon);

    public readonly record struct ScheduledSetpoint(DateTime At, [StructField(Unit = "kW")] double PowerSetpoint, [StructField(Unit = "V")] double VoltageSetpoint);

    public enum AlarmState
    {
        [StatusSeverity(StatusSeverity.Success)]
        Ok,

        [StatusSeverity(StatusSeverity.Warning)]
        Warning,

        [StatusSeverity(StatusSeverity.Error)]
        Critical,
    }
}