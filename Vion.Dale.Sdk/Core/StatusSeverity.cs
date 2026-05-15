namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Severity level for status indicator enum values.
    /// </summary>
    [PublicApi]
    public enum StatusSeverity
    {
        /// <summary>
        ///     Indicates a healthy or successful state.
        /// </summary>
        Success,

        /// <summary>
        ///     Informational status.
        /// </summary>
        Info,

        /// <summary>
        ///     Indicates a potential issue.
        /// </summary>
        Warning,

        /// <summary>
        ///     Indicates a failure or critical issue.
        /// </summary>
        Error,

        /// <summary>
        ///     No specific severity.
        /// </summary>
        Neutral,
    }
}