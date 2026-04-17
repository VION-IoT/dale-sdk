using System;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when an invalid unit identifier is provided.
    /// </summary>
    public class InvalidUnitIdentifierException : Exception
    {
        /// <summary>
        ///     Gets the invalid unit identifier that caused the exception.
        /// </summary>
        public int UnitIdentifier { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="InvalidUnitIdentifierException" /> class.
        /// </summary>
        /// <param name="unitIdentifier">The invalid unit identifier.</param>
        public InvalidUnitIdentifierException(int unitIdentifier) : base($"Unit identifier {unitIdentifier} is invalid. Must be between 0 and {byte.MaxValue}.")
        {
            UnitIdentifier = unitIdentifier;
        }
    }
}