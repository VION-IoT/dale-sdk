namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Specifies whether a dependency or contract binding is shared or exclusive.
    /// </summary>
    [PublicApi]
    public enum SharingType
    {
        /// <summary>
        ///     The binding can be shared with other consumers.
        /// </summary>
        Shared,

        /// <summary>
        ///     The binding is exclusive to a single consumer.
        /// </summary>
        Exclusive,
    }
}