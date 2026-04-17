using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Represents errors that occur during Modbus communication.
    /// </summary>
    [PublicApi]
    public class ModbusException : Exception
    {
        // ReSharper disable once MemberCanBePrivate.Global
        /// <summary>
        ///     The Modbus exception code. A value of -1 indicates that there is no specific exception code.
        /// </summary>
        public ModbusExceptionCode ExceptionCode { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusException" /> class with a specified error message
        ///     and a default exception code of -1.
        /// </summary>
        /// <param name="message">The error message that describes the Modbus communication failure.</param>
        public ModbusException(string message) : base(message)
        {
            ExceptionCode -= 1;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusException" /> class with a specified
        ///     Modbus exception code and error message.
        /// </summary>
        /// <param name="exceptionCode">The Modbus exception code identifying the type of failure.</param>
        /// <param name="message">The error message that describes the Modbus communication failure.</param>
        public ModbusException(ModbusExceptionCode exceptionCode, string message) : base(message)
        {
            ExceptionCode = exceptionCode;
        }
    }
}