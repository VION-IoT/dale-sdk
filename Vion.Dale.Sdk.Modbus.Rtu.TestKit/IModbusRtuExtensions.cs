using System;
using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit
{
    /// <summary>
    ///     Extension methods on <see cref="IModbusRtu" /> for simulating Modbus responses in tests.
    /// </summary>
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
            var contractId = new LogicBlockContractId("", modbusRtuImpl.Identifier);
            var response = new ReadModbusRtuResponse(responseData, null, request.Callback, request.CorrelationId);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a read error by invoking the pending request's callback with the given exception.
        /// </summary>
        public static void SimulateReadError<T>(this IModbusRtu modbusRtu, LogicBlockTestContext<T> testContext, Exception exception, ushort? startingAddress = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastReadRequest(testContext, modbusRtuImpl, startingAddress);
            var contractId = new LogicBlockContractId("", modbusRtuImpl.Identifier);
            var response = new ReadModbusRtuResponse(null, exception, request.Callback, request.CorrelationId);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<ReadModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a successful write response by invoking the pending request's callback with no error.
        /// </summary>
        public static void SimulateWriteResponse<T>(this IModbusRtu modbusRtu, LogicBlockTestContext<T> testContext, ushort? address = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastWriteRequest(testContext, modbusRtuImpl, address);
            var contractId = new LogicBlockContractId("", modbusRtuImpl.Identifier);
            var response = new WriteModbusRtuResponse(null, request.Callback, request.CorrelationId);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(contractId, response));
        }

        /// <summary>
        ///     Simulates a write error by invoking the pending request's callback with the given exception.
        /// </summary>
        public static void SimulateWriteError<T>(this IModbusRtu modbusRtu, LogicBlockTestContext<T> testContext, Exception exception, ushort? address = null)
            where T : LogicBlockBase
        {
            var modbusRtuImpl = CastToImplementation(modbusRtu);
            var request = FindLastWriteRequest(testContext, modbusRtuImpl, address);
            var contractId = new LogicBlockContractId("", modbusRtuImpl.Identifier);
            var response = new WriteModbusRtuResponse(exception, request.Callback, request.CorrelationId);
            modbusRtuImpl.HandleContractMessage(new ContractMessage<WriteModbusRtuResponse>(contractId, response));
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

            return candidates[candidates.Count - 1].Data;
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

            return candidates[candidates.Count - 1].Data;
        }
    }
}