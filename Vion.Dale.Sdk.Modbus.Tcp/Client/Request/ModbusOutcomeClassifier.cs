using System;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Turns the exception a request failed with into the outcome recorded on its receipt. This is the single place
    ///     the TCP client decides what a failure means for the link, so a new exception type is classified once.
    /// </summary>
    internal static class ModbusOutcomeClassifier
    {
        public static ModbusOutcome Classify(Exception exception)
        {
            return exception switch
            {
                RequestExpiredException => ModbusOutcome.Expired,
                RequestDroppedException { Reason: RequestDropReason.QueueFull } => ModbusOutcome.Dropped,
                RequestDroppedException => ModbusOutcome.Cancelled,

                // A device that answers with an exception code proves the link works; FluentModbus's code-less
                // ModbusException is a frame fault and must not be read as the device refusing the request.
                ModbusException { HasExceptionCode: true } => ModbusOutcome.DeviceError,
                ModbusException => ModbusOutcome.ProtocolError,
                ModbusResponseAlignmentException => ModbusOutcome.ProtocolError,
                InvalidBitQuantityException => ModbusOutcome.ProtocolError,

                OperationTimeoutException => ModbusOutcome.Timeout,
                ConnectionTimeoutException => ModbusOutcome.Timeout,

                InvalidUnitIdentifierException => ModbusOutcome.Invalid,
                InvalidCountException => ModbusOutcome.Invalid,
                UnsupportedByteOrderException => ModbusOutcome.Invalid,
                UnsupportedWordOrder32Exception => ModbusOutcome.Invalid,
                UnsupportedWordOrder64Exception => ModbusOutcome.Invalid,
                UnsupportedTextEncodingException => ModbusOutcome.Invalid,
                IpAddressNotSetException => ModbusOutcome.Invalid,

                OperationCanceledException => ModbusOutcome.Cancelled,

                _ => ModbusOutcome.TransportError,
            };
        }
    }
}