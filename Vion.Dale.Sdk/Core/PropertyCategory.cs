namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Semantic category for a service property or measuring point.
    /// </summary>
    [PublicApi]
    public enum PropertyCategory
    {
        /// <summary>
        ///     Reflects current operational state.
        /// </summary>
        Status,

        /// <summary>
        ///     A user-configurable setting.
        /// </summary>
        Configuration,

        /// <summary>
        ///     A triggerable action.
        /// </summary>
        Action,

        /// <summary>
        ///     A measured or calculated value.
        /// </summary>
        Metric,
    }
}