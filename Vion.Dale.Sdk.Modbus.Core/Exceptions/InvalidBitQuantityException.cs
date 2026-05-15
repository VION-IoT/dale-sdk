using System;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when the requested quantity of bits exceeds the available bits in the byte array.
    /// </summary>
    /// <remarks>
    ///     This occurs when fewer coils or discrete inputs are returned by the Modbus device than were requested.
    /// </remarks>
    public class InvalidBitQuantityException : Exception
    {
        /// <summary>
        ///     Gets the requested quantity of bits.
        /// </summary>
        public int RequestedQuantity { get; }

        /// <summary>
        ///     Gets the available number of bits.
        /// </summary>
        public int AvailableBits { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="InvalidBitQuantityException" /> class.
        /// </summary>
        /// <param name="requestedQuantity">The requested quantity of bits.</param>
        /// <param name="availableBits">The available number of bits in the byte array.</param>
        public InvalidBitQuantityException(int requestedQuantity, int availableBits) :
            base($"Requested quantity ({requestedQuantity}) exceeds available bits ({availableBits}) in the byte array.")
        {
            RequestedQuantity = requestedQuantity;
            AvailableBits = availableBits;
        }
    }
}