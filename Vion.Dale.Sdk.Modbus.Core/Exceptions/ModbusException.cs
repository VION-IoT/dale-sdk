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
        // The value the previous no-code constructor produced (0 - 1 on a byte-backed enum). Kept so the
        // observable ExceptionCode of a code-less failure does not change; HasExceptionCode is the test to use.
        private const ModbusExceptionCode NoExceptionCode = (ModbusExceptionCode)0xFF;

        /// <summary>
        ///     The Modbus exception code the device reported. Meaningless unless <see cref="HasExceptionCode" /> is
        ///     <c>true</c>.
        /// </summary>
        public ModbusExceptionCode ExceptionCode { get; }

        /// <summary>
        ///     Whether the device reported an exception code. When <c>false</c> the failure is a frame or protocol
        ///     fault, not a refusal by the device.
        /// </summary>
        public bool HasExceptionCode { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusException" /> class for a failure that carries no
        ///     device exception code.
        /// </summary>
        /// <param name="message">The error message that describes the Modbus communication failure.</param>
        public ModbusException(string message) : base(message)
        {
            ExceptionCode = NoExceptionCode;
            HasExceptionCode = false;
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
            HasExceptionCode = true;
        }
    }
}