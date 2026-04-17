namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Specifies whether a dependency must already exist or can be created on demand.
    /// </summary>
    [PublicApi]
    public enum DependencyCreationType
    {
        /// <summary>
        ///     The dependency must already exist.
        /// </summary>
        MustExist,

        /// <summary>
        ///     The dependency is created on demand if it does not exist.
        /// </summary>
        AllowCreateNew,
    }
}