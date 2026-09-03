using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    // Fixtures for docs/specs/introspection.md. Each declaration below is one shape the spec page
    // names — a bound that is not finite, a member on both emission streams, an endpoint identifier
    // that cannot be wired — so a test can assert what the emitted document does with it.

    /// <summary>An enum whose members carry both a label and a severity.</summary>
    public enum IntrospectionSeverityEnum
    {
        [EnumLabel("Fine")]
        [Severity(StatusSeverity.Success)]
        Good,

        [Severity(StatusSeverity.Error)]
        Bad,
    }

    /// <summary>Bounds the compiler accepts and JSON cannot carry — <c>NaN</c> and each infinity on the wrong side.</summary>
    public class NonFiniteBoundBlock : LogicBlockBase
    {
        public NonFiniteBoundBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Not a number", Minimum = double.NaN, Maximum = double.NaN)]
        public double NanBounds { get; set; }

        [ServiceProperty(Title = "Infinities the wrong way round", Minimum = double.PositiveInfinity, Maximum = double.NegativeInfinity)]
        public double SwappedInfinities { get; set; }

        [ServiceProperty(Title = "Bounded", Minimum = 1, Maximum = 9)]
        public double Bounded { get; set; }

        [ServiceProperty(Title = "Bounded field carrier")]
        public NonFiniteBoundStruct Fields { get; set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>The same bounds one level down, on a struct field.</summary>
    public readonly record struct NonFiniteBoundStruct([StructField(Title = "Not a number", Minimum = double.NaN, Maximum = double.NaN)] double Nan,
                                                       [StructField(Title = "Infinities the wrong way round", Minimum = double.PositiveInfinity,
                                                                    Maximum = double.NegativeInfinity)]
                                                       double Swapped,
                                                       [StructField(Title = "Bounded", Minimum = 1, Maximum = 9)] double Bounded);

    /// <summary>One property on both publication streams, each stream declaring its own knobs.</summary>
    public class DualStreamKindBlock : LogicBlockBase
    {
        public DualStreamKindBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Grid power", Description = "Live state and a chart", Unit = "kW")]
        [ServiceMeasuringPoint(Kind = MeasuringPointKind.TotalIncreasing)]
        public double Power { get; private set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>An interface binding pinned to an identifier no topology can name.</summary>
    public class BlankInterfaceIdentifierBlock : LogicBlockBase
    {
        public BlankInterfaceIdentifierBlock() : base(NullLogger.Instance)
        {
        }

        // DALE045 warns that a non-service-bearing endpoint emits no relation half. That is deliberate
        // here: these fixtures are about the endpoint's identifier, not about its relation halves.
#pragma warning disable DALE045
        [LogicBlockInterfaceBinding(typeof(IToggleable), Identifier = "   ")]
        public TogglingEndpoint Blank { get; } = new();
#pragma warning restore DALE045

        protected override void Ready()
        {
        }
    }

    /// <summary>A contract binding pinned to an identifier no topology can name.</summary>
    public class BlankContractIdentifierBlock : LogicBlockBase
    {
        public BlankContractIdentifierBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProviderContractBinding(Identifier = "")]
        public IDigitalOutput Blank { get; private set; } = null!;

        protected override void Ready()
        {
        }
    }

    /// <summary>Two interface bindings — one class-level, one property-level — pinned to one identifier.</summary>
    [LogicBlockInterfaceBinding(typeof(IToggleable), Identifier = "Shared", DefaultName = "Class level")]
    public class CollidingInterfaceIdentifierBlock : LogicBlockBase, IToggleable
    {
        public CollidingInterfaceIdentifierBlock() : base(NullLogger.Instance)
        {
        }

#pragma warning disable DALE045 // The fixture is about the identifier, not the relation half.
        [LogicBlockInterfaceBinding(typeof(IToggleable), Identifier = "Shared", DefaultName = "Property level")]
        public TogglingEndpoint Peer { get; } = new();
#pragma warning restore DALE045

        public void HandleStateUpdate(InterfaceId functionId, Toggling.TogglePressed response)
        {
        }

        public void HandleStateUpdate(InterfaceId functionId, Toggling.ToggleReleased response)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>Two contract bindings pinned to one identifier.</summary>
    public class CollidingContractIdentifierBlock : LogicBlockBase
    {
        public CollidingContractIdentifierBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProviderContractBinding(Identifier = "Shared", DefaultName = "First")]
        public IDigitalOutput OutputA { get; private set; } = null!;

        [ServiceProviderContractBinding(Identifier = "Shared", DefaultName = "Second")]
        public IDigitalOutput OutputB { get; private set; } = null!;

        protected override void Ready()
        {
        }
    }

    /// <summary>Two property-based bindings whose derived identifiers differ — the control for a collision.</summary>
    public class DistinctInterfaceIdentifierBlock : LogicBlockBase
    {
        public DistinctInterfaceIdentifierBlock() : base(NullLogger.Instance)
        {
        }

#pragma warning disable DALE045 // The fixture is about the derived identifiers, not the relation halves.
        public TogglingEndpoint Left { get; } = new();

        public TogglingEndpoint Right { get; } = new();
#pragma warning restore DALE045

        protected override void Ready()
        {
        }
    }

    /// <summary>An endpoint with no service surface of its own — bindable, and owning no relation half.</summary>
    public class TogglingEndpoint : IToggleable
    {
        public void HandleStateUpdate(InterfaceId functionId, Toggling.TogglePressed response)
        {
        }

        public void HandleStateUpdate(InterfaceId functionId, Toggling.ToggleReleased response)
        {
        }
    }

    /// <summary>A block whose type name is nested, so its identity carries the CLR nesting separator.</summary>
    public static class IntrospectionOuter
    {
        [LogicBlock(Name = "Nested")]
        public class NestedBlock : LogicBlockBase
        {
            public NestedBlock() : base(NullLogger.Instance)
            {
            }

            [ServiceProperty(Title = "Value")]
            public int Value { get; set; }

            protected override void Ready()
            {
            }
        }
    }

    /// <summary>Severities and labels reached through a nullable and through an array.</summary>
    public class SeverityReachBlock : LogicBlockBase
    {
        public SeverityReachBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Through a nullable")]
        [Presentation(StatusIndicator = true)]
        public IntrospectionSeverityEnum? Nullable { get; set; }

        // DALE024 is the compile-time half of this fixture's own subject: it warns that an array of enum
        // gets no status mappings. The fixture exists to pin that the emitted document agrees with the
        // warning, so the warning is suppressed here and nowhere else.
#pragma warning disable DALE024
        [ServiceProperty(Title = "Through an array")]
        [Presentation(StatusIndicator = true)]
        public ImmutableArray<IntrospectionSeverityEnum> Array { get; set; } = ImmutableArray<IntrospectionSeverityEnum>.Empty;
#pragma warning restore DALE024

        protected override void Ready()
        {
        }
    }

    /// <summary>Display strings the compiler accepts that no consumer may see rewritten.</summary>
    [LogicBlock(Name = "Tür & <b>Wärme</b> — 20 °C", Icon = "probe-line", Groups = new[] { "acme.custom", PropertyGroup.Status })]
    public class VerbatimStringBlock : LogicBlockBase
    {
        public VerbatimStringBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Tür & <i>Wärme</i> — 20 °C", Description = "Ünïcödé — em-dash — and \"quotes\"", Unit = "€/kWh")]
        public string Marked { get; set; } = string.Empty;

        [ServiceProperty(Title = "", Description = "", Unit = "", StringFormat = "")]
        [Presentation(DisplayName = "", Group = PropertyGroup.None, UiHint = "", Format = "", Order = int.MinValue, Decimals = int.MinValue,
                      Importance = Importance.Normal)]
        public int Empties { get; set; }

        [ServiceProperty(Title = "Inverted bounds", Minimum = 10, Maximum = 1)]
        public double Inverted { get; set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>An empty block-level name, icon and group set — the three annotations that are filtered.</summary>
    [LogicBlock(Name = "", Icon = "", Groups = new string[0])]
    public class EmptyAnnotationBlock : LogicBlockBase
    {
        public EmptyAnnotationBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>A struct declaring a convenience constructor beside its positional one.</summary>
    public readonly record struct TwoConstructorStruct(double Left, double Right)
    {
        public TwoConstructorStruct(double left) : this(left, 0)
        {
        }
    }

    /// <summary>A struct with no positional constructor — outside the flat-struct whitelist.</summary>
    public readonly record struct FieldlessStruct();

    /// <summary>Carriers for the two struct shapes above.</summary>
    public class StructShapeBlock : LogicBlockBase
    {
        public StructShapeBlock() : base(NullLogger.Instance)
        {
        }

        [ServiceProperty(Title = "Two constructors")]
        public TwoConstructorStruct TwoConstructors { get; set; }

        protected override void Ready()
        {
        }
    }

    /// <summary>A carrier for the fieldless struct, which the schema builder refuses.</summary>
    public class FieldlessStructBlock : LogicBlockBase
    {
        public FieldlessStructBlock() : base(NullLogger.Instance)
        {
        }

        // DALE016 refuses a struct with no positional constructor at compile time. This fixture exists to
        // pin the pack-path backstop behind that diagnostic, so the diagnostic it deliberately violates is
        // suppressed here and nowhere else.
#pragma warning disable DALE003, DALE016
        [ServiceProperty(Title = "Nothing to describe")]
        public FieldlessStruct Nothing { get; set; }
#pragma warning restore DALE003, DALE016

        protected override void Ready()
        {
        }
    }

    /// <summary>Component services whose holding property names differ only in case.</summary>
    public class CaseDistinctServiceBlock : LogicBlockBase
    {
        public CaseDistinctServiceBlock() : base(NullLogger.Instance)
        {
        }

        public CaseDistinctComponent Sensor { get; } = new();

        public CaseDistinctComponent SENSOR { get; } = new();

        protected override void Ready()
        {
        }
    }

    /// <summary>The component behind <see cref="CaseDistinctServiceBlock" />.</summary>
    public class CaseDistinctComponent
    {
        [ServiceProperty(Title = "Reading")]
        public int Reading { get; set; }
    }
}
