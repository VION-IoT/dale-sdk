using System;
using System.Net;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <inheritdoc />
    public partial class LogicBlockModbusTcpClient : ILogicBlockModbusTcpClient
    {
        private readonly IModbusTcpClientWrapper _clientWrapper;

        private readonly ILogger<LogicBlockModbusTcpClient> _logger;

        private readonly IRequestQueue _requestQueue;

        private bool _disposed;

        private bool _requestQueueInitialized;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LogicBlockModbusTcpClient" /> class.
        /// </summary>
        /// <param name="clientWrapper">The wrapper around the Modbus TCP client that provides data conversion and validation.</param>
        /// <param name="requestQueue">The queue that manages Modbus requests.</param>
        /// <param name="logger">The logger used for logging.</param>
        public LogicBlockModbusTcpClient(IModbusTcpClientWrapper clientWrapper, IRequestQueue requestQueue, ILogger<LogicBlockModbusTcpClient> logger)
        {
            _requestQueue = requestQueue;
            _logger = logger;
            _clientWrapper = clientWrapper;
            _clientWrapper.ConnectionTimeout = TimeSpan.FromSeconds(3);
            _clientWrapper.Port = 502;
        }

        #region Client

        /// <inheritdoc />
        public bool IsEnabled
        {
            get;

            set
            {
                field = value;
                if (!value)
                {
                    LogClientDisabled();
                    return;
                }

                LogClientEnabled();
                if (_requestQueueInitialized)
                {
                    return;
                }

                _requestQueue.Initialize(QueueCapacity, QueueOverflowPolicy);
                _requestQueueInitialized = true;
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Client enabled.")]
        partial void LogClientEnabled();

        [LoggerMessage(Level = LogLevel.Information, Message = "Client disabled.")]
        partial void LogClientDisabled();

        #endregion

        #region Queue

        /// <inheritdoc />
        public int QueueCapacity
        {
            get;

            set => field = _requestQueueInitialized ? field : value;
        } = 256;

        /// <inheritdoc />
        public QueueOverflowPolicy QueueOverflowPolicy
        {
            get;

            set => field = _requestQueueInitialized ? field : value;
        } = QueueOverflowPolicy.DropOldest;

        /// <inheritdoc />
        public int QueuedRequestCount
        {
            get => _requestQueue.QueuedRequestCount;
        }

        #endregion

        #region Connection

        /// <inheritdoc />
        public int Port
        {
            get => _clientWrapper.Port;

            set
            {
                if (value is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
                {
                    throw new FormatException($"Port {value} is out of valid range ({IPEndPoint.MinPort}-{IPEndPoint.MaxPort}).");
                }

                _clientWrapper.Port = value;
            }
        }

        /// <inheritdoc />
        public string? IpAddress
        {
            get => _clientWrapper.IpAddress?.ToString();

            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new FormatException("IP address cannot be null or empty.");
                }

                if (!IPAddress.TryParse(value, out var parsedIpAddress))
                {
                    throw new FormatException($"'{value}' is not a valid IP address.");
                }

                _clientWrapper.IpAddress = parsedIpAddress;
            }
        }

        /// <inheritdoc />
        public TimeSpan ConnectionTimeout
        {
            get => _clientWrapper.ConnectionTimeout;

            set => _clientWrapper.ConnectionTimeout = value;
        }

        /// <inheritdoc />
        public void Disconnect(IActorDispatcher dispatcher, Action? successCallback = null, Action<Exception>? errorCallback = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(Disconnect));
                return;
            }

            _requestQueue.Enqueue(nameof(Disconnect), dispatcher, cancellationToken => _clientWrapper.DisconnectAsync(cancellationToken), successCallback, errorCallback);
        }

        #endregion

        #region ModbusDataAccess

        /// <inheritdoc />
        public TimeSpan DefaultOperationTimeout { get; set; } = TimeSpan.FromSeconds(1);

        #region DiscreteInputs

        /// <inheritdoc />
        public void ReadDiscreteInputs(int unitIdentifier,
                                       ushort startingAddress,
                                       ushort quantity,
                                       IActorDispatcher dispatcher,
                                       Action<bool[]> successCallback,
                                       Action<Exception>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadDiscreteInputs));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadDiscreteInputs),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadDiscreteInputsAsync(unitIdentifier,
                                                                                              startingAddress,
                                                                                              quantity,
                                                                                              operationTimeout ?? DefaultOperationTimeout,
                                                                                              cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        #endregion

        #region Coils

        /// <inheritdoc />
        public void ReadCoils(int unitIdentifier,
                              ushort startingAddress,
                              ushort quantity,
                              IActorDispatcher dispatcher,
                              Action<bool[]> successCallback,
                              Action<Exception>? errorCallback = null,
                              TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadCoils));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadCoils),
                                  dispatcher,
                                  cancellationToken =>
                                      _clientWrapper.ReadCoilsAsync(unitIdentifier, startingAddress, quantity, operationTimeout ?? DefaultOperationTimeout, cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteSingleCoil(int unitIdentifier,
                                    ushort registerAddress,
                                    bool value,
                                    IActorDispatcher dispatcher,
                                    Action? successCallback = null,
                                    Action<Exception>? errorCallback = null,
                                    TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteSingleCoil));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteSingleCoil),
                                  dispatcher,
                                  cancellationToken =>
                                      _clientWrapper.WriteSingleCoilAsync(unitIdentifier, registerAddress, value, operationTimeout ?? DefaultOperationTimeout, cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleCoils(int unitIdentifier,
                                       ushort startingAddress,
                                       bool[] values,
                                       IActorDispatcher dispatcher,
                                       Action? successCallback = null,
                                       Action<Exception>? errorCallback = null,
                                       TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleCoils));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleCoils),
                                  dispatcher,
                                  cancellationToken =>
                                      _clientWrapper.WriteMultipleCoilsAsync(unitIdentifier,
                                                                             startingAddress,
                                                                             values,
                                                                             operationTimeout ?? DefaultOperationTimeout,
                                                                             cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        #endregion

        #region InputRegisters

        /// <inheritdoc />
        public void ReadInputRegistersRaw(int unitIdentifier,
                                          ushort startingAddress,
                                          ushort quantity,
                                          IActorDispatcher dispatcher,
                                          Action<byte[]> successCallback,
                                          Action<Exception>? errorCallback = null,
                                          TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersRaw));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersRaw),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersRawAsync(unitIdentifier,
                                                                                                 startingAddress,
                                                                                                 quantity,
                                                                                                 operationTimeout ?? DefaultOperationTimeout,
                                                                                                 cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsShort(int unitIdentifier,
                                              ushort startingAddress,
                                              ushort quantity,
                                              IActorDispatcher dispatcher,
                                              Action<short[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsShort));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsShortAsync(unitIdentifier,
                                                                                                     startingAddress,
                                                                                                     quantity,
                                                                                                     byteOrder,
                                                                                                     operationTimeout ?? DefaultOperationTimeout,
                                                                                                     cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUShort(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               IActorDispatcher dispatcher,
                                               Action<ushort[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsUShort));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsUShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsUShortAsync(unitIdentifier,
                                                                                                      startingAddress,
                                                                                                      quantity,
                                                                                                      byteOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsInt(int unitIdentifier,
                                            ushort startingAddress,
                                            uint count,
                                            IActorDispatcher dispatcher,
                                            Action<int[]> successCallback,
                                            Action<Exception>? errorCallback = null,
                                            ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                            WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                            TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsInt));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsIntAsync(unitIdentifier,
                                                                                                   startingAddress,
                                                                                                   count,
                                                                                                   byteOrder,
                                                                                                   wordOrder,
                                                                                                   operationTimeout ?? DefaultOperationTimeout,
                                                                                                   cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsUInt(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             IActorDispatcher dispatcher,
                                             Action<uint[]> successCallback,
                                             Action<Exception>? errorCallback = null,
                                             ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                             WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                             TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsUInt));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsUInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsUIntAsync(unitIdentifier,
                                                                                                    startingAddress,
                                                                                                    count,
                                                                                                    byteOrder,
                                                                                                    wordOrder,
                                                                                                    operationTimeout ?? DefaultOperationTimeout,
                                                                                                    cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsFloat(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<float[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                              TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsFloat));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsFloat),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsFloatAsync(unitIdentifier,
                                                                                                     startingAddress,
                                                                                                     count,
                                                                                                     byteOrder,
                                                                                                     wordOrder,
                                                                                                     operationTimeout ?? DefaultOperationTimeout,
                                                                                                     cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsLong(int unitIdentifier,
                                             ushort startingAddress,
                                             uint count,
                                             IActorDispatcher dispatcher,
                                             Action<long[]> successCallback,
                                             Action<Exception>? errorCallback = null,
                                             ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                             WordOrder64 wordOrder = WordOrder64.ABCD,
                                             TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsLong));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsLong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsLongAsync(unitIdentifier,
                                                                                                    startingAddress,
                                                                                                    count,
                                                                                                    byteOrder,
                                                                                                    wordOrder,
                                                                                                    operationTimeout ?? DefaultOperationTimeout,
                                                                                                    cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsULong(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<ulong[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder64 wordOrder = WordOrder64.ABCD,
                                              TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsULong));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsULong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsULongAsync(unitIdentifier,
                                                                                                     startingAddress,
                                                                                                     count,
                                                                                                     byteOrder,
                                                                                                     wordOrder,
                                                                                                     operationTimeout ?? DefaultOperationTimeout,
                                                                                                     cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsDouble(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<double[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder64 wordOrder = WordOrder64.ABCD,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsDouble));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsDouble),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsDoubleAsync(unitIdentifier,
                                                                                                      startingAddress,
                                                                                                      count,
                                                                                                      byteOrder,
                                                                                                      wordOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadInputRegistersAsString(int unitIdentifier,
                                               ushort startingAddress,
                                               ushort quantity,
                                               IActorDispatcher dispatcher,
                                               Action<string> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               TextEncoding textEncoding = TextEncoding.Ascii,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadInputRegistersAsString));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadInputRegistersAsString),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadInputRegistersAsStringAsync(unitIdentifier,
                                                                                                      startingAddress,
                                                                                                      quantity,
                                                                                                      textEncoding,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        #endregion

        #region HoldingRegisters

        /// <inheritdoc />
        public void ReadHoldingRegistersRaw(int unitIdentifier,
                                            ushort startingAddress,
                                            ushort quantity,
                                            IActorDispatcher dispatcher,
                                            Action<byte[]> successCallback,
                                            Action<Exception>? errorCallback = null,
                                            TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersRaw));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersRaw),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersRawAsync(unitIdentifier,
                                                                                                   startingAddress,
                                                                                                   quantity,
                                                                                                   operationTimeout ?? DefaultOperationTimeout,
                                                                                                   cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsShort(int unitIdentifier,
                                                ushort startingAddress,
                                                ushort quantity,
                                                IActorDispatcher dispatcher,
                                                Action<short[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsShort));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsShortAsync(unitIdentifier,
                                                                                                       startingAddress,
                                                                                                       quantity,
                                                                                                       byteOrder,
                                                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                                                       cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUShort(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 IActorDispatcher dispatcher,
                                                 Action<ushort[]> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsUShort));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsUShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsUShortAsync(unitIdentifier,
                                                                                                        startingAddress,
                                                                                                        quantity,
                                                                                                        byteOrder,
                                                                                                        operationTimeout ?? DefaultOperationTimeout,
                                                                                                        cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsInt(int unitIdentifier,
                                              ushort startingAddress,
                                              uint count,
                                              IActorDispatcher dispatcher,
                                              Action<int[]> successCallback,
                                              Action<Exception>? errorCallback = null,
                                              ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                              WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                              TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsInt));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsIntAsync(unitIdentifier,
                                                                                                     startingAddress,
                                                                                                     count,
                                                                                                     byteOrder,
                                                                                                     wordOrder,
                                                                                                     operationTimeout ?? DefaultOperationTimeout,
                                                                                                     cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsUInt(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<uint[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsUInt));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsUInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsUIntAsync(unitIdentifier,
                                                                                                      startingAddress,
                                                                                                      count,
                                                                                                      byteOrder,
                                                                                                      wordOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsFloat(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                IActorDispatcher dispatcher,
                                                Action<float[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsFloat));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsFloat),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsFloatAsync(unitIdentifier,
                                                                                                       startingAddress,
                                                                                                       count,
                                                                                                       byteOrder,
                                                                                                       wordOrder,
                                                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                                                       cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsLong(int unitIdentifier,
                                               ushort startingAddress,
                                               uint count,
                                               IActorDispatcher dispatcher,
                                               Action<long[]> successCallback,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               WordOrder64 wordOrder = WordOrder64.ABCD,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsLong));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsLong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsLongAsync(unitIdentifier,
                                                                                                      startingAddress,
                                                                                                      count,
                                                                                                      byteOrder,
                                                                                                      wordOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsULong(int unitIdentifier,
                                                ushort startingAddress,
                                                uint count,
                                                IActorDispatcher dispatcher,
                                                Action<ulong[]> successCallback,
                                                Action<Exception>? errorCallback = null,
                                                ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                WordOrder64 wordOrder = WordOrder64.ABCD,
                                                TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsULong));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsULong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsULongAsync(unitIdentifier,
                                                                                                       startingAddress,
                                                                                                       count,
                                                                                                       byteOrder,
                                                                                                       wordOrder,
                                                                                                       operationTimeout ?? DefaultOperationTimeout,
                                                                                                       cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsDouble(int unitIdentifier,
                                                 ushort startingAddress,
                                                 uint count,
                                                 IActorDispatcher dispatcher,
                                                 Action<double[]> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                 WordOrder64 wordOrder = WordOrder64.ABCD,
                                                 TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsDouble));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsDouble),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsDoubleAsync(unitIdentifier,
                                                                                                        startingAddress,
                                                                                                        count,
                                                                                                        byteOrder,
                                                                                                        wordOrder,
                                                                                                        operationTimeout ?? DefaultOperationTimeout,
                                                                                                        cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void ReadHoldingRegistersAsString(int unitIdentifier,
                                                 ushort startingAddress,
                                                 ushort quantity,
                                                 IActorDispatcher dispatcher,
                                                 Action<string> successCallback,
                                                 Action<Exception>? errorCallback = null,
                                                 TextEncoding textEncoding = TextEncoding.Ascii,
                                                 TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(ReadHoldingRegistersAsString));
                return;
            }

            _requestQueue.Enqueue(nameof(ReadHoldingRegistersAsString),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.ReadHoldingRegistersAsStringAsync(unitIdentifier,
                                                                                                        startingAddress,
                                                                                                        quantity,
                                                                                                        textEncoding,
                                                                                                        operationTimeout ?? DefaultOperationTimeout,
                                                                                                        cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               short value,
                                               IActorDispatcher dispatcher,
                                               Action? successCallback = null,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteSingleHoldingRegister));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteSingleHoldingRegister),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteSingleHoldingRegisterAsync(unitIdentifier,
                                                                                                      registerAddress,
                                                                                                      value,
                                                                                                      byteOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteSingleHoldingRegister(int unitIdentifier,
                                               ushort registerAddress,
                                               ushort value,
                                               IActorDispatcher dispatcher,
                                               Action? successCallback = null,
                                               Action<Exception>? errorCallback = null,
                                               ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                               TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteSingleHoldingRegister));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteSingleHoldingRegister),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteSingleHoldingRegisterAsync(unitIdentifier,
                                                                                                      registerAddress,
                                                                                                      value,
                                                                                                      byteOrder,
                                                                                                      operationTimeout ?? DefaultOperationTimeout,
                                                                                                      cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersRaw(int unitIdentifier,
                                                     ushort startingAddress,
                                                     byte[] values,
                                                     IActorDispatcher dispatcher,
                                                     Action? successCallback = null,
                                                     Action<Exception>? errorCallback = null,
                                                     TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersRaw));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersRaw),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersRawAsync(unitIdentifier,
                                                                                                            startingAddress,
                                                                                                            values,
                                                                                                            operationTimeout ?? DefaultOperationTimeout,
                                                                                                            cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsShort(int unitIdentifier,
                                                         ushort startingAddress,
                                                         short[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsShort));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsShortAsync(unitIdentifier,
                                                                                                                startingAddress,
                                                                                                                values,
                                                                                                                byteOrder,
                                                                                                                operationTimeout ?? DefaultOperationTimeout,
                                                                                                                cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUShort(int unitIdentifier,
                                                          ushort startingAddress,
                                                          ushort[] values,
                                                          IActorDispatcher dispatcher,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsUShort));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsUShort),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsUShortAsync(unitIdentifier,
                                                                                                                 startingAddress,
                                                                                                                 values,
                                                                                                                 byteOrder,
                                                                                                                 operationTimeout ?? DefaultOperationTimeout,
                                                                                                                 cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsInt(int unitIdentifier,
                                                       ushort startingAddress,
                                                       int[] values,
                                                       IActorDispatcher dispatcher,
                                                       Action? successCallback = null,
                                                       Action<Exception>? errorCallback = null,
                                                       ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                       WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                       TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsInt));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsIntAsync(unitIdentifier,
                                                                                                              startingAddress,
                                                                                                              values,
                                                                                                              byteOrder,
                                                                                                              wordOrder,
                                                                                                              operationTimeout ?? DefaultOperationTimeout,
                                                                                                              cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsUInt(int unitIdentifier,
                                                        ushort startingAddress,
                                                        uint[] values,
                                                        IActorDispatcher dispatcher,
                                                        Action? successCallback = null,
                                                        Action<Exception>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                        TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsUInt));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsUInt),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsUIntAsync(unitIdentifier,
                                                                                                               startingAddress,
                                                                                                               values,
                                                                                                               byteOrder,
                                                                                                               wordOrder,
                                                                                                               operationTimeout ?? DefaultOperationTimeout,
                                                                                                               cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsFloat(int unitIdentifier,
                                                         ushort startingAddress,
                                                         float[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder32 wordOrder = WordOrder32.MswToLsw,
                                                         TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsFloat));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsFloat),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsFloatAsync(unitIdentifier,
                                                                                                                startingAddress,
                                                                                                                values,
                                                                                                                byteOrder,
                                                                                                                wordOrder,
                                                                                                                operationTimeout ?? DefaultOperationTimeout,
                                                                                                                cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsLong(int unitIdentifier,
                                                        ushort startingAddress,
                                                        long[] values,
                                                        IActorDispatcher dispatcher,
                                                        Action? successCallback = null,
                                                        Action<Exception>? errorCallback = null,
                                                        ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                        WordOrder64 wordOrder = WordOrder64.ABCD,
                                                        TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsLong));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsLong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsLongAsync(unitIdentifier,
                                                                                                               startingAddress,
                                                                                                               values,
                                                                                                               byteOrder,
                                                                                                               wordOrder,
                                                                                                               operationTimeout ?? DefaultOperationTimeout,
                                                                                                               cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsULong(int unitIdentifier,
                                                         ushort startingAddress,
                                                         ulong[] values,
                                                         IActorDispatcher dispatcher,
                                                         Action? successCallback = null,
                                                         Action<Exception>? errorCallback = null,
                                                         ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                         WordOrder64 wordOrder = WordOrder64.ABCD,
                                                         TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsULong));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsULong),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsULongAsync(unitIdentifier,
                                                                                                                startingAddress,
                                                                                                                values,
                                                                                                                byteOrder,
                                                                                                                wordOrder,
                                                                                                                operationTimeout ?? DefaultOperationTimeout,
                                                                                                                cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsDouble(int unitIdentifier,
                                                          ushort startingAddress,
                                                          double[] values,
                                                          IActorDispatcher dispatcher,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          ByteOrder byteOrder = ByteOrder.MsbToLsb,
                                                          WordOrder64 wordOrder = WordOrder64.ABCD,
                                                          TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsDouble));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsDouble),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsDoubleAsync(unitIdentifier,
                                                                                                                 startingAddress,
                                                                                                                 values,
                                                                                                                 byteOrder,
                                                                                                                 wordOrder,
                                                                                                                 operationTimeout ?? DefaultOperationTimeout,
                                                                                                                 cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        /// <inheritdoc />
        public void WriteMultipleHoldingRegistersAsString(int unitIdentifier,
                                                          ushort startingAddress,
                                                          string value,
                                                          IActorDispatcher dispatcher,
                                                          Action? successCallback = null,
                                                          Action<Exception>? errorCallback = null,
                                                          TextEncoding textEncoding = TextEncoding.Ascii,
                                                          TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(WriteMultipleHoldingRegistersAsString));
                return;
            }

            _requestQueue.Enqueue(nameof(WriteMultipleHoldingRegistersAsString),
                                  dispatcher,
                                  cancellationToken => _clientWrapper.WriteMultipleHoldingRegistersAsStringAsync(unitIdentifier,
                                                                                                                 startingAddress,
                                                                                                                 value,
                                                                                                                 textEncoding,
                                                                                                                 operationTimeout ?? DefaultOperationTimeout,
                                                                                                                 cancellationToken),
                                  successCallback,
                                  errorCallback);
        }

        #endregion

        [LoggerMessage(Level = LogLevel.Debug, Message = "{Operation} operation skipped because client is disabled")]
        partial void LogOperationSkipped(string operation);

        #endregion

        #region Dispose

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _requestQueue.Dispose();
                _clientWrapper.Dispose();
            }

            _disposed = true;
        }

        #endregion
    }
}