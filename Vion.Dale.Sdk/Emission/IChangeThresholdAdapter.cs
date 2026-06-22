namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    /// Non-generic façade over <see cref="IChangeThreshold{T}"/> so the emission gate can compare
    /// boxed <see cref="object"/> values without knowing the closed value type at the call site.
    /// </summary>
    internal interface IChangeThresholdAdapter
    {
        /// <summary>
        /// Unboxes <paramref name="last"/> and <paramref name="candidate"/> to the wrapped value
        /// type and delegates to the inner <see cref="IChangeThreshold{T}"/>.
        /// </summary>
        bool Exceeds(object? last, object? candidate, string threshold);
    }
}
