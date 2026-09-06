using System;
using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit
{
    /// <summary>
    ///     Extension methods on <see cref="IModbusRtu" /> for simulating Modbus responses in tests.
    /// </summary>
    /// <remarks>
    ///     Every simulated response is stamped from the test context's virtual clock, so the receipt the logic block
    ///     receives reflects whatever time the test advanced between issuing the request and simulating the answer. The
    ///     callbacks are dispatched the way production dispatches them — call
    ///     <c>LogicBlockTestContext.FlushPendingActions()</c> to run them.
    /// </remarks>
    [PublicApi]
    public static class IModbusRtuExtensions
    {
        /// <summary>
        ///     Simulates a successful read response by invoking the pending request's callback with the given data.
        ///     The data bytes are processed through the same callback chain as in production (SwapBytes, CastFromBytes, etc.).
        /// </summary>
        /// <param name="modbusRtu">The Modbus RTU contract instance.</param>
        /// <param name="testContext">The test context containing recorded messages.</param>
        /// <param name="responseData">The raw response bytes (big-endian by default, matching Modbus wire format).</param>
        /// <param name="startingAddress">Optional filter to match a specific request by starting address.</param>
        public static void SimulateReadResponse<T>(this IModbusRtu modbusRtu, LogicBlockTestContext<T> testContext, byte[] responseData, ushort? startingAddress = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastReadRequest(testContext, modbusRtuImpl, startingAddress);
            var contractId = new LogicBlockContractId(Constants.LogicBlockId, modbusRtuImpl.Identifier);
            var response = new ReadModbusRtuResponse(responseData,
                                                     null,
                                                     request.Callback,
                                                     request.CorrelationId,
                                                     request.CreatedAt,
                                                     request.CreatedAt,
                                                     testContext.TimeProvider.GetUtcNow().UtcDateTime,
                                                     testContext.TimeProvider.GetTimestamp(),
                                                     ModbusOutcome.Success);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a read error by invoking the pending request's callback with the given exception.
        /// </summary>
        /// <param name="modbusRtu">The Modbus RTU contract instance.</param>
        /// <param name="testContext">The test context containing recorded messages.</param>
        /// <param name="exception">The failure to deliver to the block.</param>
        /// <param name="startingAddress">Optional filter to match a specific request by starting address.</param>
        /// <param name="outcome">The outcome to stamp on the receipt, or <c>null</c> to derive it from the exception.</param>
        public static void SimulateReadError<T>(this IModbusRtu modbusRtu,
                                                LogicBlockTestContext<T> testContext,
                                                Exception exception,
                                                ushort? startingAddress = null,
                                                ModbusOutcome? outcome = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastReadRequest(testContext, modbusRtuImpl, startingAddress);
            var contractId = new LogicBlockContractId(Constants.LogicBlockId, modbusRtuImpl.Identifier);
            var effectiveOutcome = outcome ?? Classify(exception);
            var response = new ReadModbusRtuResponse(null,
                                                     exception,
                                                     request.Callback,
                                                     request.CorrelationId,
                                                     request.CreatedAt,
                                                     PublishedAtFor(effectiveOutcome, request.CreatedAt),
                                                     testContext.TimeProvider.GetUtcNow().UtcDateTime,
                                                     testContext.TimeProvider.GetTimestamp(),
                                                     effectiveOutcome);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a successful write response by invoking the pending request's callback with no error.
        /// </summary>
        /// <param name="modbusRtu">The Modbus RTU contract instance.</param>
        /// <param name="testContext">The test context containing recorded messages.</param>
        /// <param name="address">Optional filter to match a specific request by address.</param>
        public static void SimulateWriteResponse<T>(this IModbusRtu modbusRtu, LogicBlockTestContext<T> testContext, ushort? address = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastWriteRequest(testContext, modbusRtuImpl, address);
            var contractId = new LogicBlockContractId(Constants.LogicBlockId, modbusRtuImpl.Identifier);
            var response = new WriteModbusRtuResponse(null,
                                                      request.Callback,
                                                      request.CorrelationId,
                                                      request.CreatedAt,
                                                      request.CreatedAt,
                                                      testContext.TimeProvider.GetUtcNow().UtcDateTime,
                                                      testContext.TimeProvider.GetTimestamp(),
                                                      ModbusOutcome.Success);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a write error by invoking the pending request's callback with the given exception.
        /// </summary>
        /// <param name="modbusRtu">The Modbus RTU contract instance.</param>
        /// <param name="testContext">The test context containing recorded messages.</param>
        /// <param name="exception">The failure to deliver to the block.</param>
        /// <param name="address">Optional filter to match a specific request by address.</param>
        /// <param name="outcome">The outcome to stamp on the receipt, or <c>null</c> to derive it from the exception.</param>
        public static void SimulateWriteError<T>(this IModbusRtu modbusRtu,
                                                 LogicBlockTestContext<T> testContext,
                                                 Exception exception,
                                                 ushort? address = null,
                                                 ModbusOutcome? outcome = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastWriteRequest(testContext, modbusRtuImpl, address);
            var contractId = new LogicBlockContractId(Constants.LogicBlockId, modbusRtuImpl.Identifier);
            var effectiveOutcome = outcome ?? Classify(exception);
            var response = new WriteModbusRtuResponse(exception,
                                                      request.Callback,
                                                      request.CorrelationId,
                                                      request.CreatedAt,
                                                      PublishedAtFor(effectiveOutcome, request.CreatedAt),
                                                      testContext.TimeProvider.GetUtcNow().UtcDateTime,
                                                      testContext.TimeProvider.GetTimestamp(),
                                                      effectiveOutcome);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(contractId, response));
        }

        // Mirrors what ModbusRtuHandler stamps, so a simulated failure produces the receipt the real one would.
        private static ModbusOutcome Classify(Exception exception)
        {
            return exception switch
            {
                ModbusException { HasExceptionCode: true } => ModbusOutcome.DeviceError,
                ModbusException => ModbusOutcome.ProtocolError,
                OperationTimeoutException => ModbusOutcome.Timeout,
                RequestExpiredException => ModbusOutcome.Expired,
                PendingRequestsLimitReachedException => ModbusOutcome.Dropped,
                ServiceProviderContractMappingNotFoundException => ModbusOutcome.Invalid,
                _ => ModbusOutcome.TransportError,
            };
        }

        // Outcomes the handler decides before publishing never reach the wire, so they carry no publish instant and
        // their whole elapsed time is queued wait.
        private static DateTime? PublishedAtFor(ModbusOutcome outcome, DateTime createdAt)
        {
            return outcome is ModbusOutcome.Expired or ModbusOutcome.Dropped or ModbusOutcome.Invalid ? null : createdAt;
        }

        private static ModbusRtu CastToImplementation(IModbusRtu modbusRtu)
        {
            if (modbusRtu is not ModbusRtu impl)
            {
                throw new InvalidOperationException("Unable to simulate response on provided IModbusRtu instance");
            }

            return impl;
        }

        private static ReadModbusRtuRequest FindLastReadRequest<T>(LogicBlockTestContext<T> testContext, ModbusRtu modbusRtuImpl, ushort? startingAddress)
            where T : LogicBlockBase
        {
            var messages = testContext.GetContractMessages<ReadModbusRtuRequest>(modbusRtuImpl.Identifier);
            var candidates = startingAddress.HasValue ? messages.Where(m => m.Data.StartingAddress == startingAddress.Value).ToList() : messages.ToList();

            if (candidates.Count == 0)
            {
                var filter = startingAddress.HasValue ? $" with starting address {startingAddress.Value}" : "";
                throw new InvalidOperationException($"No pending ReadModbusRtuRequest found{filter}. " + "Ensure the logic block has issued a read before simulating a response.");
            }

            return candidates[^1].Data;
        }

        private static WriteModbusRtuRequest FindLastWriteRequest<T>(LogicBlockTestContext<T> testContext, ModbusRtu modbusRtuImpl, ushort? address)
            where T : LogicBlockBase
        {
            var messages = testContext.GetContractMessages<WriteModbusRtuRequest>(modbusRtuImpl.Identifier);
            var candidates = address.HasValue ? messages.Where(m => m.Data.Address == address.Value).ToList() : messages.ToList();

            if (candidates.Count == 0)
            {
                var filter = address.HasValue ? $" with address {address.Value}" : "";
                throw new InvalidOperationException($"No pending WriteModbusRtuRequest found{filter}. " +
                                                    "Ensure the logic block has issued a write before simulating a response.");
            }

            return candidates[^1].Data;
        }
    }
}