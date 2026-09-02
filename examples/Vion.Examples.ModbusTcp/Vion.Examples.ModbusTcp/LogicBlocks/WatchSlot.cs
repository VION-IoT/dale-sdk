using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;

namespace Vion.Examples.ModbusTcp.LogicBlocks
{
    /// <summary>
    ///     One pinned register: a labelled address that <see cref="ModbusTcpDebugClient" /> re-reads on every
    ///     watch tick and publishes as both live state and a charted time series. Where the ad-hoc read pane
    ///     answers "what is at this address right now", a watch slot answers "how has this value moved".
    /// </summary>
    /// <remarks>
    ///     A service-bearing component: each slot forms its own service named after the property that holds it
    ///     (<c>Watch1</c>, <c>Watch2</c>, …), so the <c>VisibleWhen</c> predicates below resolve against the
    ///     slot's own properties. How many slots exist is decided at config time by the block's
    ///     <c>WatchSlotCount</c> instantiation parameter.
    /// </remarks>
    public class WatchSlot
    {
        [ServiceProperty(Title = "Label", Description = "What this register means on your device — shown instead of a bare address.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public string Label { get; set; } = string.Empty;

        [ServiceProperty(Title = "Enabled", Description = "Off by default so unconfigured slots stay silent.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 20)]
        public bool Enabled { get; set; }

        [ServiceProperty(Title = "Function")]
        [Presentation(DisplayName = "Function", Group = PropertyGroup.Configuration, Order = 30, VisibleWhen = "Enabled")]
        public WatchFunction Function { get; set; } = WatchFunction.InputRegisters;

        [ServiceProperty(Title = "Address", Minimum = 0, Maximum = 65535, Description = "Protocol (base-0) address of the first register.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40, VisibleWhen = "Enabled")]
        public int Address { get; set; }

        [ServiceProperty(Title = "Field type")]
        [Presentation(DisplayName = "Field type", Group = PropertyGroup.Configuration, Order = 50, VisibleWhen = "Enabled")]
        public WatchFieldType FieldType { get; set; } = WatchFieldType.Float32;

        [ServiceProperty(Title = "Byte order")]
        [Presentation(DisplayName = "Byte order", Group = PropertyGroup.Configuration, Order = 60, VisibleWhen = "Enabled")]
        public ByteOrder ByteOrder { get; set; } = ByteOrder.MsbToLsb;

        [ServiceProperty(Title = "Word order (32 bit)")]
        [Presentation(DisplayName = "Word order (32 bit)", Group = PropertyGroup.Configuration, Order = 70, VisibleWhen = "Enabled && FieldType in ['UInt32','Int32','Float32']")]
        public WordOrder32 WordOrder32 { get; set; } = WordOrder32.MswToLsw;

        [ServiceProperty(Title = "Word order (64 bit)")]
        [Presentation(DisplayName = "Word order (64 bit)", Group = PropertyGroup.Configuration, Order = 80, VisibleWhen = "Enabled && FieldType in ['UInt64','Int64','Float64']")]
        public WordOrder64 WordOrder64 { get; set; } = WordOrder64.ABCD;

        /// <summary>
        ///     The decoded value, widened to <c>double</c> so every field type charts through one member.
        ///     64-bit integers beyond 2^53 lose precision here — read them in the ad-hoc pane instead.
        /// </summary>
        [ServiceProperty(Title = "Value")]
        [ServiceMeasuringPoint(Title = "Value")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public double Value { get; internal set; }

        [ServiceProperty(Title = "Status")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string Status { get; internal set; } = string.Empty;

        /// <summary>
        ///     How many registers one value of <see cref="FieldType" /> occupies.
        /// </summary>
        internal ushort RegisterCount
        {
            get =>
                FieldType switch
                {
                    WatchFieldType.UInt16 or WatchFieldType.Int16 => 1,
                    WatchFieldType.UInt32 or WatchFieldType.Int32 or WatchFieldType.Float32 => 2,
                    _ => 4,
                };
        }
    }
}