using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using System;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Provides Modbus RTU read and write operations.
    /// </summary>
    public partial class ModbusRtu : LogicBlockContractBase, IModbusRtu
    {
        private const int BytesPer32Bit = 4;

        private const int BytesPer64Bit = 8;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ILogger<ModbusRtu> _logger;

        private readonly IModbusRtuRequestFactory _requestFactory;

        private readonly IModbusValidator _validator;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(ModbusRtuHandler);

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusRtu" /> class.
        /// </summary>
        /// <param name="identifier">The unique identifier for this ModbusRtu IO.</param>
        /// <param name="actorContext">The actor context used for communication with the HAL handler.</param>
        /// <param name="requestFactory">The factory used to create Modbus RTU read and write requests.</param>
        /// <param name="dataConverter">The converter used for Modbus data type transformations.</param>
        /// <param name="validator">The validator used to validate Modbus request parameters and responses.</param>
        /// <param name="logger">The logger for logging.</param>
        public ModbusRtu(string identifier,
                         IActorContext actorContext,
                         IModbusRtuRequestFactory requestFactory,
                         IModbusDataConverter dataConverter,
                         IModbusValidator validator,
                         ILogger<ModbusRtu> logger) : base(identifier, actorContext)
        {
            _requestFactory = requestFactory;
            _dataConverter = dataConverter;
            _validator = validator;
            _logger = logger;
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<ReadModbusRtuResponse> m:
                    LogReadResponseReceived(LogicBlockContractId, m.Data.CorrelationId);
                    m.Data.Callback(m.Data.Data, m.Data.Exception);
                    break;
                case ContractMessage<WriteModbusRtuResponse> m:
                    LogWriteResponseReceived(LogicBlockContractId, m.Data.CorrelationId);
                    m.Data.Callback(m.Data.Exception);
                    break;
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Read response received (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        partial void LogReadResponseReceived(LogicBlockContractId logicBlockContractId, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Write response received (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        partial void LogWriteResponseReceived(LogicBlockContractId logicBlockContractId, Guid correlationId);

        #region Client

        /// <inheritdoc />
        public bool IsEnabled
        {
            get;

            set
            {
                field = value;
                if (value)
                {
                    LogClientEnabled(LogicBlockContractId);
                }
                else
                {
                    LogClientDisabled(LogicBlockContractId);
                }
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Client enabled (LogicBlockContractId={LogicBlockContractId})")]
        partial void LogClientEnabled(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Client disabled (LogicBlockContractId={LogicBlockContractId})")]
        partial void LogClientDisabled(LogicBlockContractId logicBlockContractId);

        #endregion

        #region ModbusDataAccess

        /// <inheritdoc />
        public TimeSpan DefaultOperationTimeout { get; set; } = TimeSpan.FromSeconds(5);

        #region DiscreteInputs

        /// <inheritdoc />
        public void ReadDiscreteInputs(int unitIdentifier,
                                       ushort startingAddress,
                                       ushort quantity,
                                       Action<bool[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadDiscreteInputs,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBitsToBools(responseData, quantity),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        #endregion

        #region Coils

        /// <inheritdoc />
        public void ReadCoils(int unitIdentifier,
                              ushort startingAddress,
                              ushort quantity,
                              Action<bool[]> successCallback,
                              Action<Exception>? errorCallback = null,
                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadCoils,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBitsToBools(responseData, quantity),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleCoil(int unitIdentifier,
                                    ushort registerAddress,
                                    bool value,
                                    Action? successCallback = null,
                                    Action<Exception>? errorCallback = null,
                                    TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteSingleCoil,
                                unitIdentifier,
                                registerAddress,
                                () => [_dataConverter.ToByte(value)],
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleCoils(int unitIdentifier,
                                       ushort startingAddress,
                                       bool[] values,
                                       Action? successCallback = null,
                                       Action<Exception>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleCoils,
                                unitIdentifier,
                                startingAddress,
                                () => _dataConverter.CastToBytes(values),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        #endregion

        #region InputRegisters

        /// <inheritdoc />
        public void ReadInputRegistersRaw(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          Action<byte[]> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => responseData.ToArray(),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsShort(int unitIdentifier,
                                              ushort startingAddress,
                                              ushort quantity,
                                              Action<short[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<short>(responseData, unitIdentifier, startingAddress, byteOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUShort(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               Action<ushort[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<ushort>(responseData, unitIdentifier, startingAddress, byteOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsInt(int unitIdentifier,
                                            ushort startingAddress,
                                            uint count,
                                            Action<int[]> successCallback,
                                            Action<Exception>? errorCallback = null,
                                            ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                            WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                            TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<int>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUInt(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             Action<uint[]> successCallback,
                                             Action<Exception>? errorCallback = null,
                                             ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                             WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                             TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<uint>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsFloat(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              Action<float[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<float>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsLong(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             Action<long[]> successCallback,
                                             Action<Exception>? errorCallback = null,
                                             ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                             WordOrder64 wordOrder = WordOrder64.ABCD,
                                             TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<long>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsULong(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              Action<ulong[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder64 wordOrder = WordOrder64.ABCD,
                                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<ulong>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsDouble(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               Action<double[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder64 wordOrder = WordOrder64.ABCD,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<double>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsString(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               Action<string> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               TextEncoding textEncoding = TextEncoding.Ascii,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBytesToString(responseData, textEncoding),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        #endregion

        #region HoldingRegisters

        /// <inheritdoc />
        public void ReadHoldingRegistersRaw(int unitIdentifier,
                                            ushort startingAddress,
                                            ushort quantity,
                                            Action<byte[]> successCallback,
                                            Action<Exception>? errorCallback = null,
                                            TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => responseData.ToArray(),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsShort(int unitIdentifier,
                                                ushort startingAddress,
                                                ushort quantity,
                                                Action<short[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<short>(responseData, unitIdentifier, startingAddress, byteOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUShort(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 Action<ushort[]> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<ushort>(responseData, unitIdentifier, startingAddress, byteOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsInt(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              Action<int[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<int>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUInt(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               Action<uint[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<uint>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsFloat(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                Action<float[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer32Bit,
                               responseData => Process32BitResponse<float>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsLong(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               Action<long[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder64 wordOrder = WordOrder64.ABCD,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<long>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsULong(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                Action<ulong[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder64 wordOrder = WordOrder64.ABCD,
                                                TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<ulong>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsDouble(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint count,
                                                 Action<double[]> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder64 wordOrder = WordOrder64.ABCD,
                                                 TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               count,
                               BytesPer64Bit,
                               responseData => Process64BitResponse<double>(responseData, unitIdentifier, startingAddress, byteOrder, wordOrder),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsString(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 Action<string> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 TextEncoding textEncoding = TextEncoding.Ascii,
                                                 TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBytesToString(responseData, textEncoding),
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               short value,
                                               Action? successCallback = null,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteSingleRegister,
                                unitIdentifier,
                                registerAddress,
                                () =>
                                {
                                    var data = _dataConverter.GetBytes(value);
                                    _dataConverter.SwapBytes(data, byteOrder);

                                    return data;
                                },
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               ushort value,
                                               Action? successCallback = null,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteSingleRegister,
                                unitIdentifier,
                                registerAddress,
                                () =>
                                {
                                    var data = _dataConverter.GetBytes(value);
                                    _dataConverter.SwapBytes(data, byteOrder);

                                    return data;
                                },
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                                     ushort startingAddress,
                                                     byte[] values,
                                                     Action? successCallback = null,
                                                     Action<Exception>? errorCallback = null,
                                                     TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => values,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsShort(int unitIdentifier,
                                                         ushort startingAddress,
                                                         short[] values,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format16BitData(values, byteOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUShort(int unitIdentifier,
                                                          ushort startingAddress,
                                                          ushort[] values,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format16BitData(values, byteOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsInt(int unitIdentifier,
                                                       ushort startingAddress,
                                                       int[] values,
                                                       Action? successCallback = null,
                                                       Action<Exception>? errorCallback = null,
                                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                       TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUInt(int unitIdentifier,
                                                        ushort startingAddress,
                                                        uint[] values,
                                                        Action? successCallback = null,
                                                        Action<Exception>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                        TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsFloat(int unitIdentifier,
                                                         ushort startingAddress,
                                                         float[] values,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsLong(int unitIdentifier,
                                                        ushort startingAddress,
                                                        long[] values,
                                                        Action? successCallback = null,
                                                        Action<Exception>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                                        TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsULong(int unitIdentifier,
                                                         ushort startingAddress,
                                                         ulong[] values,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder64 wordOrder = WordOrder64.ABCD,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsDouble(int unitIdentifier,
                                                          ushort startingAddress,
                                                          double[] values,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          WordOrder64 wordOrder = WordOrder64.ABCD,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsString(int unitIdentifier,
                                                          ushort startingAddress,
                                                          string value,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          TextEncoding textEncoding = TextEncoding.Ascii,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => _dataConverter.ConvertStringToBytes(value, textEncoding),
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        private byte[] Format16BitData<T>(T[] values, ByteOrder byteOrder)
            where T : unmanaged
        {
            var data = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(data, byteOrder);

            return data;
        }

        private byte[] Format32BitData<T>(T[] values, ByteOrder byteOrder, WordOrder32 wordOrder)
            where T : unmanaged
        {
            var data = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(data, byteOrder);
            _dataConverter.SwapWords(data, wordOrder);

            return data;
        }

        private byte[] Format64BitData<T>(T[] values, ByteOrder byteOrder, WordOrder64 wordOrder)
            where T : unmanaged
        {
            var data = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(data, byteOrder);
            _dataConverter.SwapWords(data, wordOrder);

            return data;
        }

        #endregion

        private T[] Process16BitResponse<T>(Memory<byte> responseData, int unitIdentifier, ushort startingAddress, ByteOrder byteOrder)
            where T : unmanaged
        {
            _validator.ValidateResponseAlignment(responseData.Length, 2, unitIdentifier, startingAddress);
            _dataConverter.SwapBytes(responseData, byteOrder);

            return _dataConverter.CastFromBytes<T>(responseData);
        }

        private T[] Process32BitResponse<T>(Memory<byte> responseData, int unitIdentifier, ushort startingAddress, ByteOrder byteOrder, WordOrder32 wordOrder)
            where T : unmanaged
        {
            _validator.ValidateResponseAlignment(responseData.Length, BytesPer32Bit, unitIdentifier, startingAddress);
            _dataConverter.SwapBytes(responseData, byteOrder);
            _dataConverter.SwapWords(responseData, wordOrder);

            return _dataConverter.CastFromBytes<T>(responseData);
        }

        private T[] Process64BitResponse<T>(Memory<byte> responseData, int unitIdentifier, ushort startingAddress, ByteOrder byteOrder, WordOrder64 wordOrder)
            where T : unmanaged
        {
            _validator.ValidateResponseAlignment(responseData.Length, BytesPer64Bit, unitIdentifier, startingAddress);
            _dataConverter.SwapBytes(responseData, byteOrder);
            _dataConverter.SwapWords(responseData, wordOrder);

            return _dataConverter.CastFromBytes<T>(responseData);
        }

        private void ExecuteReadRequest<T>(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           ushort quantity,
                                           Func<Memory<byte>, T[]> processResponse,
                                           Action<T[]> successCallback,
                                           Action<Exception>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => _requestFactory.CreateReadRequest(functionCode,
                                                                       unitIdentifier,
                                                                       startingAddress,
                                                                       quantity,
                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                       processResponse,
                                                                       successCallback,
                                                                       errorCallback),
                               errorCallback);
        }

        private void ExecuteReadRequest<T>(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           ushort quantity,
                                           Func<Memory<byte>, T> processResponse,
                                           Action<T> successCallback,
                                           Action<Exception>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => _requestFactory.CreateReadRequest(functionCode,
                                                                       unitIdentifier,
                                                                       startingAddress,
                                                                       quantity,
                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                       processResponse,
                                                                       successCallback,
                                                                       errorCallback),
                               errorCallback);
        }

        private void ExecuteReadRequest<T>(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           uint count,
                                           int bytesPerCount,
                                           Func<Memory<byte>, T[]> processResponse,
                                           Action<T[]> successCallback,
                                           Action<Exception>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => _requestFactory.CreateReadRequest(functionCode,
                                                                       unitIdentifier,
                                                                       startingAddress,
                                                                       _dataConverter.ConvertCountToQuantity(count, bytesPerCount),
                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                       processResponse,
                                                                       successCallback,
                                                                       errorCallback),
                               errorCallback);
        }

        private void ExecuteReadRequest(ModbusFunctionCode functionCode,
                                        int unitIdentifier,
                                        ushort startingAddress,
                                        Func<ReadModbusRtuRequest> createReadRequest,
                                        Action<Exception>? errorCallback)
        {
            if (!IsEnabled)
            {
                LogRequestSkipped(LogicBlockContractId, functionCode);
                return;
            }

            LogExecutingReadRequest(LogicBlockContractId, functionCode, unitIdentifier, startingAddress);
            try
            {
                _validator.ValidateUnitIdentifier(unitIdentifier);
                var readRequest = createReadRequest();
                LogSendingReadRequest(LogicBlockContractId, functionCode, unitIdentifier, startingAddress, readRequest.CorrelationId);
                SendToContractHandler(new ContractMessage<ReadModbusRtuRequest>(LogicBlockContractId, readRequest));
            }
            catch (Exception exception)
            {
                LogRequestFailed(LogicBlockContractId, functionCode, unitIdentifier, startingAddress, exception);
                errorCallback?.Invoke(exception);
            }
        }

        private void ExecuteWriteRequest(ModbusFunctionCode functionCode,
                                         int unitIdentifier,
                                         ushort address,
                                         Func<byte[]> formatData,
                                         Action? successCallback,
                                         Action<Exception>? errorCallback,
                                         TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogRequestSkipped(LogicBlockContractId, functionCode);
                return;
            }

            LogExecutingWriteRequest(LogicBlockContractId, functionCode, unitIdentifier, address);
            try
            {
                _validator.ValidateUnitIdentifier(unitIdentifier);
                var data = formatData();
                var writeRequest = _requestFactory.CreateWriteRequest(functionCode,
                                                                      unitIdentifier,
                                                                      address,
                                                                      data,
                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                      successCallback,
                                                                      errorCallback);
                LogSendingWriteRequest(LogicBlockContractId, functionCode, unitIdentifier, address, writeRequest.CorrelationId);
                SendToContractHandler(new ContractMessage<WriteModbusRtuRequest>(LogicBlockContractId, writeRequest));
            }
            catch (Exception exception)
            {
                LogRequestFailed(LogicBlockContractId, functionCode, unitIdentifier, address, exception);
                errorCallback?.Invoke(exception);
            }
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Request skipped because client is disabled (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode})")]
        partial void LogRequestSkipped(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode);

        [LoggerMessage(Level = LogLevel.Error,
                       Message = "Request failed (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address})")]
        partial void LogRequestFailed(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Executing read request (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address})")]
        partial void LogExecutingReadRequest(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode, int unitIdentifier, ushort address);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Sending read request (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, " +
                           "CorrelationId={CorrelationId})")]
        partial void LogSendingReadRequest(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Executing write request (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address})")]
        partial void LogExecutingWriteRequest(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode, int unitIdentifier, ushort address);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Sending write request (LogicBlockContractId={LogicBlockContractId}, FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, " +
                           "CorrelationId={CorrelationId})")]
        partial void LogSendingWriteRequest(LogicBlockContractId logicBlockContractId, ModbusFunctionCode functionCode, int unitIdentifier, ushort address, Guid correlationId);

        #endregion
    }
}