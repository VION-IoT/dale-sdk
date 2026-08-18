using System;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Classifies the failures the block side of a Modbus RTU transaction decides itself. What the handler decides
    ///     — the device's answer, the pending limit, expiry, a missing mapping — the handler stamps on the response.
    /// </summary>
    internal static class ModbusRtuOutcomeClassifier
    {
        /// <summary>Classifies a failure raised while turning the device's answer into the requested type.</summary>
        public static ModbusOutcome ClassifyResponseFailure(Exception exception)
        {
            // The device answered, so the link is fine: either the caller asked for a conversion that cannot be
            // done, or the answer did not fit the request.
            var isCallerError = exception is InvalidCountException or UnsupportedByteOrderException or UnsupportedWordOrder32Exception or UnsupportedWordOrder64Exception
                                    or UnsupportedTextEncodingException;

            return isCallerError ? ModbusOutcome.Invalid : ModbusOutcome.ProtocolError;
        }
    }
}