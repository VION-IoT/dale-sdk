using System;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Modbus;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Modbus.Core.Client;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Utils;

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

        private readonly ModbusLinkAccumulator _linkAccumulator = new();

        private readonly ILogger<ModbusRtu> _logger;

        private readonly IModbusRtuRequestFactory _requestFactory;

        private readonly TimeProvider _timeProvider;

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
        /// <param name="timeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger for logging.</param>
        public ModbusRtu(string identifier,
                         IActorContext actorContext,
                         IModbusRtuRequestFactory requestFactory,
                         IModbusDataConverter dataConverter,
                         IModbusValidator validator,
                         TimeProvider timeProvider,
                         ILogger<ModbusRtu> logger) : base(identifier, actorContext)
        {
            _requestFactory = requestFactory;
            _dataConverter = dataConverter;
            _validator = validator;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            switch (contractMessage)
            {
                case ContractMessage<ReadModbusRtuResponse> m:
                    LogReadResponseReceived(LogicBlockContractId, m.Data.CorrelationId);
                    m.Data.Callback(m.Data.Data, m.Data.Exception, m.Data.ToReceipt());
                    break;
                case ContractMessage<WriteModbusRtuResponse> m:
                    LogWriteResponseReceived(LogicBlockContractId, m.Data.CorrelationId);
                    m.Data.Callback(m.Data.Exception, m.Data.ToReceipt());
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

        /// <inheritdoc />
        public TimeSpan? MaxQueuedAge
        {
            get;

            set
            {
                if (value is { } age && age <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), age, $"{nameof(MaxQueuedAge)} must be greater than zero, or null to disable the check.");
                }

                field = value;
            }
        } = TimeSpan.FromSeconds(30);

        /// <inheritdoc />
        /// <remarks>
        ///     <c>QueueDepth</c> is always zero: requests wait in the runtime-wide handler shared with every other
        ///     Modbus RTU binding, so there is no depth this client can honestly claim as its own.
        /// </remarks>
        public ModbusLinkSummary Link
        {
            get => _linkAccumulator.Snapshot(0);
        }

        #endregion

        #region ModbusDataAccess

        /// <inheritdoc />
        public TimeSpan DefaultOperationTimeout
        {
            get;

            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(DefaultOperationTimeout)} must be greater than zero.");
                }

                field = value;
            }
        } = TimeSpan.FromSeconds(5);

        #region DiscreteInputs

        /// <inheritdoc />
        public void ReadDiscreteInputs(int unitIdentifier,
                                       ushort startingAddress,
                                       ushort quantity,
                                       IActorDispatcher dispatcher,
                                       Action<bool[], ModbusReceipt> successCallback,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadDiscreteInputs,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBitsToBools(responseData, quantity),
                               dispatcher,
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
                              IActorDispatcher dispatcher,
                              Action<bool[], ModbusReceipt> successCallback,
                              Action<Exception, ModbusReceipt>? errorCallback = null,
                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadCoils,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBitsToBools(responseData, quantity),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleCoil(int unitIdentifier,
                                    ushort registerAddress,
                                    bool value,
                                    IActorDispatcher dispatcher,
                                    Action<ModbusReceipt>? successCallback = null,
                                    Action<Exception, ModbusReceipt>? errorCallback = null,
                                    TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteSingleCoil,
                                unitIdentifier,
                                registerAddress,
                                () => [_dataConverter.ToByte(value)],
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleCoils(int unitIdentifier,
                                       ushort startingAddress,
                                       bool[] values,
                                       IActorDispatcher dispatcher,
                                       Action<ModbusReceipt>? successCallback = null,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleCoils,
                                unitIdentifier,
                                startingAddress,
                                () => _dataConverter.CastToBytes(values),
                                dispatcher,
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
                                          IActorDispatcher dispatcher,
                                          Action<byte[], ModbusReceipt> successCallback,
                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                          TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => responseData.ToArray(),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsShort(int unitIdentifier,
                                              ushort startingAddress,
                                              ushort quantity,
                                              IActorDispatcher dispatcher,
                                              Action<short[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<short>(responseData, unitIdentifier, startingAddress, byteOrder),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUShort(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               IActorDispatcher dispatcher,
                                               Action<ushort[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<ushort>(responseData, unitIdentifier, startingAddress, byteOrder),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsInt(int unitIdentifier,
                                            ushort startingAddress,
                                            uint count,
                                            IActorDispatcher dispatcher,
                                            Action<int[], ModbusReceipt> successCallback,
                                            Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUInt(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             IActorDispatcher dispatcher,
                                             Action<uint[], ModbusReceipt> successCallback,
                                             Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsFloat(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<float[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsLong(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             IActorDispatcher dispatcher,
                                             Action<long[], ModbusReceipt> successCallback,
                                             Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsULong(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<ulong[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsDouble(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<double[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsString(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               IActorDispatcher dispatcher,
                                               Action<string, ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
                                               TextEncoding textEncoding = TextEncoding.Ascii,
                                               TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadInputRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBytesToString(responseData, textEncoding),
                               dispatcher,
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
                                            IActorDispatcher dispatcher,
                                            Action<byte[], ModbusReceipt> successCallback,
                                            Action<Exception, ModbusReceipt>? errorCallback = null,
                                            TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => responseData.ToArray(),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsShort(int unitIdentifier,
                                                ushort startingAddress,
                                                ushort quantity,
                                                IActorDispatcher dispatcher,
                                                Action<short[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<short>(responseData, unitIdentifier, startingAddress, byteOrder),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUShort(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 IActorDispatcher dispatcher,
                                                 Action<ushort[], ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => Process16BitResponse<ushort>(responseData, unitIdentifier, startingAddress, byteOrder),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsInt(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<int[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUInt(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<uint[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsFloat(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                IActorDispatcher dispatcher,
                                                Action<float[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsLong(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<long[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsULong(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                IActorDispatcher dispatcher,
                                                Action<ulong[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsDouble(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint count,
                                                 IActorDispatcher dispatcher,
                                                 Action<double[], ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
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
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsString(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 IActorDispatcher dispatcher,
                                                 Action<string, ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
                                                 TextEncoding textEncoding = TextEncoding.Ascii,
                                                 TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(ModbusFunctionCode.ReadHoldingRegisters,
                               unitIdentifier,
                               startingAddress,
                               quantity,
                               responseData => _dataConverter.ConvertBytesToString(responseData, textEncoding),
                               dispatcher,
                               successCallback,
                               errorCallback,
                               operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               short value,
                                               IActorDispatcher dispatcher,
                                               Action<ModbusReceipt>? successCallback = null,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               ushort value,
                                               IActorDispatcher dispatcher,
                                               Action<ModbusReceipt>? successCallback = null,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                                     ushort startingAddress,
                                                     byte[] values,
                                                     IActorDispatcher dispatcher,
                                                     Action<ModbusReceipt>? successCallback = null,
                                                     Action<Exception, ModbusReceipt>? errorCallback = null,
                                                     TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => values,
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsShort(int unitIdentifier,
                                                         ushort startingAddress,
                                                         short[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format16BitData(values, byteOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUShort(int unitIdentifier,
                                                          ushort startingAddress,
                                                          ushort[] values,
                                                          IActorDispatcher dispatcher,
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format16BitData(values, byteOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsInt(int unitIdentifier,
                                                       ushort startingAddress,
                                                       int[] values,
                                                       IActorDispatcher dispatcher,
                                                       Action<ModbusReceipt>? successCallback = null,
                                                       Action<Exception, ModbusReceipt>? errorCallback = null,
                                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                       TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUInt(int unitIdentifier,
                                                        ushort startingAddress,
                                                        uint[] values,
                                                        IActorDispatcher dispatcher,
                                                        Action<ModbusReceipt>? successCallback = null,
                                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                        TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsFloat(int unitIdentifier,
                                                         ushort startingAddress,
                                                         float[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format32BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsLong(int unitIdentifier,
                                                        ushort startingAddress,
                                                        long[] values,
                                                        IActorDispatcher dispatcher,
                                                        Action<ModbusReceipt>? successCallback = null,
                                                        Action<Exception, ModbusReceipt>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                                        TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsULong(int unitIdentifier,
                                                         ushort startingAddress,
                                                         ulong[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder64 wordOrder = WordOrder64.ABCD,
                                                         TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsDouble(int unitIdentifier,
                                                          ushort startingAddress,
                                                          double[] values,
                                                          IActorDispatcher dispatcher,
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          WordOrder64 wordOrder = WordOrder64.ABCD,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => Format64BitData(values, byteOrder, wordOrder),
                                dispatcher,
                                successCallback,
                                errorCallback,
                                operationTimeout);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsString(int unitIdentifier,
                                                          ushort startingAddress,
                                                          string value,
                                                          IActorDispatcher dispatcher,
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
                                                          TextEncoding textEncoding = TextEncoding.Ascii,
                                                          TimeSpan? operationTimeout = null)
        {
            ExecuteWriteRequest(ModbusFunctionCode.WriteMultipleRegisters,
                                unitIdentifier,
                                startingAddress,
                                () => _dataConverter.ConvertStringToBytes(value, textEncoding),
                                dispatcher,
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
                                           IActorDispatcher dispatcher,
                                           Action<T[], ModbusReceipt> successCallback,
                                           Action<Exception, ModbusReceipt>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => quantity,
                               validatedQuantity => _requestFactory.CreateReadRequest(functionCode,
                                                                                      unitIdentifier,
                                                                                      startingAddress,
                                                                                      validatedQuantity,
                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                      MaxQueuedAge,
                                                                                      processResponse,
                                                                                      dispatcher,
                                                                                      successCallback,
                                                                                      errorCallback,
                                                                                      _linkAccumulator),
                               dispatcher,
                               errorCallback);
        }

        private void ExecuteReadRequest<T>(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           ushort quantity,
                                           Func<Memory<byte>, T> processResponse,
                                           IActorDispatcher dispatcher,
                                           Action<T, ModbusReceipt> successCallback,
                                           Action<Exception, ModbusReceipt>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => quantity,
                               validatedQuantity => _requestFactory.CreateReadRequest(functionCode,
                                                                                      unitIdentifier,
                                                                                      startingAddress,
                                                                                      validatedQuantity,
                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                      MaxQueuedAge,
                                                                                      processResponse,
                                                                                      dispatcher,
                                                                                      successCallback,
                                                                                      errorCallback,
                                                                                      _linkAccumulator),
                               dispatcher,
                               errorCallback);
        }

        private void ExecuteReadRequest<T>(ModbusFunctionCode functionCode,
                                           int unitIdentifier,
                                           ushort startingAddress,
                                           uint count,
                                           int bytesPerCount,
                                           Func<Memory<byte>, T[]> processResponse,
                                           IActorDispatcher dispatcher,
                                           Action<T[], ModbusReceipt> successCallback,
                                           Action<Exception, ModbusReceipt>? errorCallback,
                                           TimeSpan? operationTimeout = null)
        {
            ExecuteReadRequest(functionCode,
                               unitIdentifier,
                               startingAddress,
                               () => _dataConverter.ConvertCountToQuantity(count, bytesPerCount),
                               validatedQuantity => _requestFactory.CreateReadRequest(functionCode,
                                                                                      unitIdentifier,
                                                                                      startingAddress,
                                                                                      validatedQuantity,
                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                      MaxQueuedAge,
                                                                                      processResponse,
                                                                                      dispatcher,
                                                                                      successCallback,
                                                                                      errorCallback,
                                                                                      _linkAccumulator),
                               dispatcher,
                               errorCallback);
        }

        private void ExecuteReadRequest(ModbusFunctionCode functionCode,
                                        int unitIdentifier,
                                        ushort startingAddress,
                                        Func<ushort> resolveQuantity,
                                        Func<ushort, ReadModbusRtuRequest> createReadRequest,
                                        IActorDispatcher dispatcher,
                                        Action<Exception, ModbusReceipt>? errorCallback)
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
                var quantity = resolveQuantity();
                _validator.ValidateQuantity(quantity, ReadLimitFor(functionCode));
                var readRequest = createReadRequest(quantity);
                LogSendingReadRequest(LogicBlockContractId, functionCode, unitIdentifier, startingAddress, readRequest.CorrelationId);
                SendToContractHandler(new ContractMessage<ReadModbusRtuRequest>(LogicBlockContractId, readRequest));
            }
            catch (Exception exception)
            {
                FailRequest(exception,
                            functionCode,
                            unitIdentifier,
                            startingAddress,
                            dispatcher,
                            errorCallback);
            }
        }

        private void ExecuteWriteRequest(ModbusFunctionCode functionCode,
                                         int unitIdentifier,
                                         ushort address,
                                         Func<byte[]> formatData,
                                         IActorDispatcher dispatcher,
                                         Action<ModbusReceipt>? successCallback,
                                         Action<Exception, ModbusReceipt>? errorCallback,
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
                _validator.ValidateQuantity(WriteQuantityOf(functionCode, data), WriteLimitFor(functionCode));
                var writeRequest = _requestFactory.CreateWriteRequest(functionCode,
                                                                      unitIdentifier,
                                                                      address,
                                                                      data,
                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                      MaxQueuedAge,
                                                                      dispatcher,
                                                                      successCallback,
                                                                      errorCallback,
                                                                      _linkAccumulator);
                LogSendingWriteRequest(LogicBlockContractId, functionCode, unitIdentifier, address, writeRequest.CorrelationId);
                SendToContractHandler(new ContractMessage<WriteModbusRtuRequest>(LogicBlockContractId, writeRequest));
            }
            catch (Exception exception)
            {
                FailRequest(exception,
                            functionCode,
                            unitIdentifier,
                            address,
                            dispatcher,
                            errorCallback);
            }
        }

        /// <summary>
        ///     The protocol limit for a read function code: coils and discrete inputs are counted in bits, holding
        ///     and input registers in registers.
        /// </summary>
        private static int ReadLimitFor(ModbusFunctionCode functionCode)
        {
            return functionCode is ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs ? ModbusProtocolLimits.MaxBitsPerRead :
                       ModbusProtocolLimits.MaxRegistersPerRead;
        }

        /// <summary>The protocol limit for a write function code, in the units that code counts in.</summary>
        private static int WriteLimitFor(ModbusFunctionCode functionCode)
        {
            return functionCode is ModbusFunctionCode.WriteSingleCoil or ModbusFunctionCode.WriteMultipleCoils ? ModbusProtocolLimits.MaxBitsPerWrite :
                       ModbusProtocolLimits.MaxRegistersPerWrite;
        }

        /// <summary>
        ///     How many bits or registers a formatted write payload carries. A coil travels this hop as one byte
        ///     and is packed by the HAL; a register is two bytes.
        /// </summary>
        private static uint WriteQuantityOf(ModbusFunctionCode functionCode, byte[] data)
        {
            return functionCode is ModbusFunctionCode.WriteSingleCoil or ModbusFunctionCode.WriteMultipleCoils ? (uint)data.Length : (uint)(data.Length / 2);
        }

        /// <summary>
        ///     Completes a request that never left the block: the parameters were rejected, or the payload could not be
        ///     formatted.
        /// </summary>
        /// <remarks>
        ///     The failure travels the same asynchronous path a response takes, so a caller never has to handle the
        ///     same error both inside the call and later from its callback.
        /// </remarks>
        private void FailRequest(Exception exception,
                                 ModbusFunctionCode functionCode,
                                 int unitIdentifier,
                                 ushort address,
                                 IActorDispatcher dispatcher,
                                 Action<Exception, ModbusReceipt>? errorCallback)
        {
            LogRequestFailed(LogicBlockContractId, functionCode, unitIdentifier, address, exception);
            var receipt = new ModbusReceipt(_timeProvider.GetUtcNow().UtcDateTime, _timeProvider.GetTimestamp(), TimeSpan.Zero, TimeSpan.Zero, ModbusOutcome.Invalid);
            _linkAccumulator.Record(receipt);
            if (errorCallback != null)
            {
                dispatcher.InvokeSynchronized(() => errorCallback(exception, receipt));
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