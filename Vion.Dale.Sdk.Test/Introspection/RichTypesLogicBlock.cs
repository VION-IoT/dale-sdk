using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Introspection
{
    public readonly record struct Coordinates(double Lat, double Lon);

    public readonly record struct ScheduledSetpoint(DateTime At, [StructField(Unit = "kW")] double PowerSetpoint, [StructField(Unit = "V")] double VoltageSetpoint);

    public enum AlarmState
    {
        Ok,

        Warning,

        Critical,
    }

    [Service("RichDevice")]
    public class RichTypesLogicBlock : LogicBlockBase
    {
        // Primitives — including the new unsigned set
        [ServiceProperty]
        public bool BoolValue { get; set; }

        [ServiceProperty]
        public byte ByteValue { get; set; }

        [ServiceProperty]
        public ushort UShortValue { get; set; }

        [ServiceProperty]
        public uint UIntValue { get; set; }

        [ServiceProperty]
        public long LongValue { get; set; }

        [ServiceProperty(Unit = "V", Minimum = 0, Maximum = 400)]
        public double VoltageSetpoint { get; set; }

        // Nullable primitives
        [ServiceProperty]
        public double? Target { get; set; }

        [ServiceProperty]
        public int? OptionalCount { get; set; }

        // String — both annotated forms
        [ServiceProperty]
        public string NonNullName { get; set; } = string.Empty;

        [ServiceProperty]
        public string? OptionalErrorMessage { get; set; }

        // ImmutableArray<T> for various T
        [ServiceMeasuringPoint(Unit = "A")]
        public ImmutableArray<double> HistogramBuckets { get; private set; } = ImmutableArray<double>.Empty;

        [ServiceMeasuringPoint]
        public ImmutableArray<int?> SamplesWithGaps { get; private set; } = ImmutableArray<int?>.Empty;

        // Struct — direct, nullable, array, array-of-nullable
        [ServiceMeasuringPoint]
        public Coordinates Location { get; private set; }

        [ServiceMeasuringPoint]
        public Coordinates? LastKnownLocation { get; private set; }

        [ServiceMeasuringPoint]
        public ImmutableArray<Coordinates> Route { get; private set; } = ImmutableArray<Coordinates>.Empty;

        [ServiceProperty]
        public ImmutableArray<ScheduledSetpoint> Schedule { get; set; } = ImmutableArray<ScheduledSetpoint>.Empty;

        // Enum — direct + nullable
        [ServiceMeasuringPoint]
        public AlarmState CurrentAlarm { get; private set; }

        [ServiceMeasuringPoint]
        public AlarmState? LastAlarm { get; private set; }

        // Nullable enum — writable service property (needed for ServiceBinder round-trip tests)
        [ServiceProperty]
        public OperatingMode? PreferredMode { get; set; }

        // Nullable struct — writable service property (needed for ServiceBinder round-trip tests)
        [ServiceProperty]
        public Coordinates? PreferredLocation { get; set; }

        // ImmutableArray — writable service property (needed for ServiceBinder round-trip tests)
        [ServiceProperty]
        public ImmutableArray<double> Setpoints { get; set; } = ImmutableArray<double>.Empty;

        public RichTypesLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }
}