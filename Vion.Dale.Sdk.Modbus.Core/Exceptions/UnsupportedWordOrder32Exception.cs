using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when an unsupported 32-bit word order value is specified.
    /// </summary>
    public class UnsupportedWordOrder32Exception : Exception
    {
        /// <summary>
        ///     Gets the unsupported 32-bit word order value.
        /// </summary>
        public WordOrder32 WordOrder { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnsupportedWordOrder32Exception" /> class.
        /// </summary>
        /// <param name="wordOrder">The unsupported 32-bit word order value.</param>
        public UnsupportedWordOrder32Exception(WordOrder32 wordOrder) : base($"Unsupported 32-bit word order specified: {wordOrder}.")
        {
            WordOrder = wordOrder;
        }
    }
}