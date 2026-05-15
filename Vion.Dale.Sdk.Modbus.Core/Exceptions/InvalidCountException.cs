using System;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when a count value is invalid for a Modbus operation.
    /// </summary>
    /// <remarks>
    ///     This occurs when the requested count of values results in a register quantity that is 0 or exceeds the
    ///     maximum of 65535 registers. For example, requesting 17000 64-bit values requires 68000 registers.
    /// </remarks>
    public class InvalidCountException : Exception
    {
        /// <summary>
        ///     Gets the invalid count value.
        /// </summary>
        public uint Count { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="InvalidCountException" /> class.
        /// </summary>
        /// <param name="count">The invalid count value.</param>
        /// <param name="message">The error message.</param>
        public InvalidCountException(uint count, string message) : base(message)
        {
            Count = count;
        }
    }
}