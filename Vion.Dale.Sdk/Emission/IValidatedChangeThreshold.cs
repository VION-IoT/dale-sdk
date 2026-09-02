namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     A deadband whose <c>MinChange</c> format is known, so the raw token can be read once when the
    ///     member's policy is built instead of on every offered value. The six built-ins implement it; a
    ///     custom <see cref="IChangeThreshold{T}" /> defines its own format and is left alone, which is the
    ///     same line <c>DALE035</c> draws at compile time.
    /// </summary>
    internal interface IValidatedChangeThreshold
    {
        /// <summary>
        ///     Reads <paramref name="threshold" />, throwing a <see cref="System.FormatException" /> naming
        ///     the token when this deadband cannot use it.
        /// </summary>
        void ValidateThreshold(string threshold);
    }
}