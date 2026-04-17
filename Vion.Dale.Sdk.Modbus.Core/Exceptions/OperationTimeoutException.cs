using System;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when a Modbus read or write operation does not complete within the specified timeout period.
    /// </summary>
    public class OperationTimeoutException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="OperationTimeoutException" /> class.
        /// </summary>
        public OperationTimeoutException() : base("The operation did not complete within time.")
        {
        }
    }
}