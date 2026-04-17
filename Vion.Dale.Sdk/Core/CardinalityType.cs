namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Specifies the cardinality of a dependency or contract binding.
    /// </summary>
    [PublicApi]
    public enum CardinalityType
    {
        /// <summary>
        ///     The binding is required and must be fulfilled.
        /// </summary>
        Mandatory,

        /// <summary>
        ///     The binding is optional and may be left unbound.
        /// </summary>
        Optional,

        /// <summary>
        ///     Multiple bindings are allowed.
        /// </summary>
        Multiple,
    }
}