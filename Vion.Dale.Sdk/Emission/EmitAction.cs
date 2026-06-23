namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The decision the <see cref="Throttler" /> reached for a single offered value.
    /// </summary>
    internal enum EmitAction
    {
        /// <summary>Publish the value now.</summary>
        Emit,

        /// <summary>Suppress the value (floor or deadband); nothing is published.</summary>
        Drop,

        /// <summary>Defer the value until the carried deadline (trailing-edge flush).</summary>
        Hold,
    }
}