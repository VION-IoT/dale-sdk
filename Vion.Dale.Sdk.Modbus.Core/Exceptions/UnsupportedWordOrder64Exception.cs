using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when an unsupported 64-bit word order value is specified.
    /// </summary>
    public class UnsupportedWordOrder64Exception : Exception
    {
        /// <summary>
        ///     Gets the unsupported 64-bit word order value.
        /// </summary>
        public WordOrder64 WordOrder { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnsupportedWordOrder64Exception" /> class.
        /// </summary>
        /// <param name="wordOrder">The unsupported 64-bit word order value.</param>
        public UnsupportedWordOrder64Exception(WordOrder64 wordOrder) : base($"Unsupported 64-bit word order specified: {wordOrder}.")
        {
            WordOrder = wordOrder;
        }
    }
}