using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Client;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <inheritdoc />
    public partial class LogicBlockModbusTcpClient : ILogicBlockModbusTcpClient
    {
        // IPEndPoint.MinPort is 0, which is the "let the OS choose" sentinel rather than an addressable port.
        private const int MinPort = 1;

        private static readonly TimeSpan DefaultMaxQueuedAge = TimeSpan.FromSeconds(30);

        private readonly IModbusTcpClientWrapper _clientWrapper;

        private readonly ModbusTcpConnectionAccumulator _connectionAccumulator = new();

        private readonly ModbusLinkAccumulator _linkAccumulator = new();

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

            // The container builds the wrapper and the queue without knowing which client will own them, so the
            // accumulators are handed over here rather than injected. These are the seams the diagnostics hang off.
            _clientWrapper.SetConnectionAccumulator(_connectionAccumulator);
            _requestQueue.MaxQueuedAge = DefaultMaxQueuedAge;
        }

        #region Client

        /// <inheritdoc />
        public bool IsEnabled
        {
            get;

            set
            {
                var wasEnabled = field;
                field = value;
                if (!value)
                {
                    LogClientDisabled();
                    return;
                }

                LogClientEnabled();
                if (!wasEnabled)
                {
                    // Re-enabling is how a block says its configuration is complete again, so it supersedes the
                    // failed connects that armed any backoff.
                    _clientWrapper.ResetConnectBackoff(nameof(IsEnabled));
                }

                if (_requestQueueInitialized)
                {
                    return;
                }

                _requestQueue.Initialize(QueueCapacity, QueueOverflowPolicy, _linkAccumulator);
                _requestQueueInitialized = true;
            }
        }

        /// <inheritdoc />
        public ModbusLinkSummary Link
        {
            get => _linkAccumulator.Snapshot(_requestQueue.QueuedRequestCount);
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

            set
            {
                EnsureQueueNotCreated(nameof(QueueCapacity), field, value);
                field = value;
            }
        } = 256;

        /// <inheritdoc />
        public QueueOverflowPolicy QueueOverflowPolicy
        {
            get;

            set
            {
                EnsureQueueNotCreated(nameof(QueueOverflowPolicy), field, value);
                field = value;
            }
        } = QueueOverflowPolicy.DropOldest;

        /// <inheritdoc />
        public int QueuedRequestCount
        {
            get => _requestQueue.QueuedRequestCount;
        }

        /// <inheritdoc />
        public TimeSpan? MaxQueuedAge
        {
            get => _requestQueue.MaxQueuedAge;

            set
            {
                if (value is { } age && age <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), age, $"{nameof(MaxQueuedAge)} must be greater than zero, or null to disable the check.");
                }

                _requestQueue.MaxQueuedAge = value;
            }
        }

        #endregion

        #region Connection

        /// <inheritdoc />
        public int Port
        {
            get => _clientWrapper.Port;

            set
            {
                // Port 0 asks the OS for an ephemeral port, which a client can never reach a device on: every
                // connect fails and two of them arm a backoff. It is what an unset configuration field binds to.
                if (value is < MinPort or > IPEndPoint.MaxPort)
                {
                    throw new FormatException($"Port {value} is out of valid range ({MinPort}-{IPEndPoint.MaxPort}).");
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

            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(ConnectionTimeout)} must be greater than zero.");
                }

                _clientWrapper.ConnectionTimeout = value;
            }
        }

        /// <inheritdoc />
        public TimeSpan ConnectBackoff
        {
            get => _clientWrapper.ConnectBackoff;

            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(ConnectBackoff)} must be greater than zero.");
                }

                if (value > _clientWrapper.ConnectBackoffMax)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                                                          value,
                                                          $"{nameof(ConnectBackoff)} must not exceed {nameof(ConnectBackoffMax)}, which is {_clientWrapper.ConnectBackoffMax}. " +
                                                          $"Raise {nameof(ConnectBackoffMax)} first.");
                }

                _clientWrapper.ConnectBackoff = value;
            }
        }

        /// <inheritdoc />
        public TimeSpan ConnectBackoffMax
        {
            get => _clientWrapper.ConnectBackoffMax;

            set
            {
                if (value < _clientWrapper.ConnectBackoff)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                                                          value,
                                                          $"{nameof(ConnectBackoffMax)} must be at least {nameof(ConnectBackoff)}, which is {_clientWrapper.ConnectBackoff}. " +
                                                          $"Lower {nameof(ConnectBackoff)} first.");
                }

                _clientWrapper.ConnectBackoffMax = value;
            }
        }

        /// <inheritdoc />
        public ModbusTcpConnectionSummary Connection
        {
            get => _connectionAccumulator.Snapshot();
        }

        /// <inheritdoc />
        public void Disconnect(IActorDispatcher dispatcher, Action? successCallback = null, Action<Exception>? errorCallback = null)
        {
            if (!IsEnabled)
            {
                LogOperationSkipped(nameof(Disconnect));
                return;
            }

            _requestQueue.EnqueueControlOperation(nameof(Disconnect),
                                                  dispatcher,
                                                  cancellationToken => _clientWrapper.DisconnectAsync(cancellationToken),
                                                  successCallback,
                                                  errorCallback);
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
        } = TimeSpan.FromSeconds(1);

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
                              Action<bool[], ModbusReceipt> successCallback,
                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                    Action<ModbusReceipt>? successCallback = null,
                                    Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                       Action<ModbusReceipt>? successCallback = null,
                                       Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                          Action<byte[], ModbusReceipt> successCallback,
                                          Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                              Action<short[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<ushort[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                            Action<int[], ModbusReceipt> successCallback,
                                            Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                             Action<uint[], ModbusReceipt> successCallback,
                                             Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                              Action<float[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                             Action<long[], ModbusReceipt> successCallback,
                                             Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                              Action<ulong[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<double[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<string, ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                            Action<byte[], ModbusReceipt> successCallback,
                                            Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                Action<short[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                 Action<ushort[], ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                              Action<int[], ModbusReceipt> successCallback,
                                              Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<uint[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                Action<float[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<long[], ModbusReceipt> successCallback,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                Action<ulong[], ModbusReceipt> successCallback,
                                                Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                 Action<double[], ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                 Action<string, ModbusReceipt> successCallback,
                                                 Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<ModbusReceipt>? successCallback = null,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                               Action<ModbusReceipt>? successCallback = null,
                                               Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                     Action<ModbusReceipt>? successCallback = null,
                                                     Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                       Action<ModbusReceipt>? successCallback = null,
                                                       Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                        Action<ModbusReceipt>? successCallback = null,
                                                        Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                        Action<ModbusReceipt>? successCallback = null,
                                                        Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                         Action<ModbusReceipt>? successCallback = null,
                                                         Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
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
                                                          Action<ModbusReceipt>? successCallback = null,
                                                          Action<Exception, ModbusReceipt>? errorCallback = null,
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

        /// <summary>
        ///     Refuses a change to a queue setting the queue was already built from. Re-setting the value in force
        ///     stays a no-op, so a consumer that re-applies its whole configuration is not punished for an unrelated
        ///     edit — the same rule the address and port setters follow.
        /// </summary>
        private void EnsureQueueNotCreated<T>(string propertyName, T current, T value)
        {
            if (!_requestQueueInitialized || Equals(current, value))
            {
                return;
            }

            throw new
                InvalidOperationException($"{propertyName} can only be changed before the client is first enabled — the request queue is built from it. "
                                          + "Set it while the client is disabled for the first time.");
        }

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