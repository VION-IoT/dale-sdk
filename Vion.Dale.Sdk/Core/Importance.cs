namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares the UI importance level of a service property or measuring point.
    /// </summary>
    [PublicApi]
    public enum Importance
    {
        /// <summary>
        ///     Shown only in detail views.
        /// </summary>
        Normal,

        /// <summary>
        ///     Shown prominently on dashboard tiles (large display).
        /// </summary>
        Primary,

        /// <summary>
        ///     Shown on dashboard tiles (small display).
        /// </summary>
        Secondary,

        /// <summary>
        ///     Not shown in the UI.
        /// </summary>
        Hidden,
    }
}