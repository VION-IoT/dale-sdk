namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Whether the value a block last wrote on a service-provider output contract could be read as a
    ///     comparable scalar — and if not, why.
    /// </summary>
    public enum ServiceProviderOutputState
    {
        /// <summary>The block has not written this contract yet — no command was captured this run.</summary>
        NeverWritten,

        /// <summary>A command was captured and the addressed value is a comparable scalar (or an explicit null).</summary>
        Readable,

        /// <summary>
        ///     A command was captured, but the addressed value has no scalar leaf: the whole command of a
        ///     multi-field contract, or a field path that lands on a struct, an array, or nothing at all.
        /// </summary>
        Unreadable,
    }

    /// <summary>
    ///     One read of a service-provider output contract: what state the read is in, the comparable value when
    ///     there is one, and the raw wire JSON that was captured — carried so a failing assertion can show the
    ///     author what the block actually wrote.
    /// </summary>
    public sealed record ServiceProviderOutputRead
    {
        /// <summary>Whether the read yielded a comparable value, and if not, why.</summary>
        public required ServiceProviderOutputState State { get; init; }

        /// <summary>
        ///     The comparable CLR scalar — bool, double, string (enums by name) — or null for an explicit JSON
        ///     null. Always null unless <see cref="State" /> is <see cref="ServiceProviderOutputState.Readable" />.
        /// </summary>
        public object? Value { get; init; }

        /// <summary>The raw wire JSON of the last captured command, or null when nothing was ever written.</summary>
        public string? Captured { get; init; }
    }
}