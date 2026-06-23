namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Decides whether a candidate value differs from the last-emitted value by at least a
    ///     configured threshold. Implementations are pure and stateless; the threshold string is
    ///     the raw <c>MinChange</c> token from the attribute (e.g. <c>"2"</c>, <c>"0.5"</c>, <c>"250ms"</c>).
    /// </summary>
    /// <typeparam name="T">The service-element value type this threshold compares.</typeparam>
    public interface IChangeThreshold<T>
    {
        /// <summary>
        ///     Returns <see langword="true" /> when <paramref name="candidate" /> differs from
        ///     <paramref name="lastEmitted" /> by at least the parsed <paramref name="threshold" />.
        /// </summary>
        bool Exceeds(in T lastEmitted, in T candidate, string threshold);
    }
}