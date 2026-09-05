using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation
{
    internal partial class ModbusTcpClientWrapper : IModbusTcpClientWrapper
    {
        private const int BytesPer32BitValue = 4;

        private const int BytesPer64BitValue = 8;

        // The backoff is armed from here on: a single failure is a transient the next request pays for anyway.
        private const int ConnectFailuresBeforeBackoff = 2;

        private static readonly TimeSpan DefaultConnectBackoff = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan DefaultConnectBackoffMax = TimeSpan.FromSeconds(30);

        private readonly IModbusTcpClientProxy _clientProxy;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ILogger<ModbusTcpClientWrapper> _logger;

        private readonly TimeProvider _timeProvider;

        private readonly IModbusValidator _validator;

        // Written on the block's actor thread, read on the queue consumer: kept as ticks behind Interlocked so a
        // 32-bit gateway cannot read half of one value and half of another, which is a silent policy change.
        private long _backoffUntilTicks;

        private long _connectBackoffMaxTicks = DefaultConnectBackoffMax.Ticks;

        private long _connectBackoffTicks = DefaultConnectBackoff.Ticks;

        // Never null: the link policy keys off the consecutive-failure run this holds, so it has to work whether or
        // not an owning client has handed its own accumulator over yet.
        private ModbusTcpConnectionAccumulator _connectionAccumulator = new();

        private bool _disposed;

        private bool _reconnectRequired;

        public ModbusTcpClientWrapper(IModbusTcpClientProxy clientProxy,
                                      IModbusValidator validator,
                                      IModbusDataConverter dataConverter,
                                      TimeProvider timeProvider,
                                      ILogger<ModbusTcpClientWrapper> logger)
        {
            _clientProxy = clientProxy;
            _validator = validator;
            _dataConverter = dataConverter;
            _timeProvider = timeProvider;
            _logger = logger;
            _connectionAccumulator.UseClock(timeProvider);
        }

        /// <inheritdoc />
        public void SetConnectionAccumulator(ModbusTcpConnectionAccumulator accumulator)
        {
            _connectionAccumulator = accumulator;
            _connectionAccumulator.UseClock(_timeProvider);
        }

        #region Connection

        /// <inheritdoc />
        public TimeSpan ConnectionTimeout
        {
            get;

            set
            {
                field = value;
                LogConnectTimeoutSet(value);
            }
        }

        /// <inheritdoc />
        public int Port
        {
            get;

            set
            {
                // Re-setting the value in force is a no-op: a consumer that re-applies its whole configuration
                // whenever one field is edited would otherwise drop the connection on every unrelated edit.
                if (field == value)
                {
                    return;
                }

                field = value;
                LogPortSet(value);
                _reconnectRequired = true;
                ClearConnectBackoff(nameof(Port));
            }
        }

        /// <inheritdoc />
        public IPAddress? IpAddress
        {
            get;

            set
            {
                if (Equals(field, value))
                {
                    return;
                }

                field = value;
                LogIpAddressSet(value!);
                _reconnectRequired = true;
                ClearConnectBackoff(nameof(IpAddress));
            }
        }

        /// <inheritdoc />
        public TimeSpan ConnectBackoff
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _connectBackoffTicks));

            set => Interlocked.Exchange(ref _connectBackoffTicks, value.Ticks);
        }

        /// <inheritdoc />
        public TimeSpan ConnectBackoffMax
        {
            get => TimeSpan.FromTicks(Interlocked.Read(ref _connectBackoffMaxTicks));

            set => Interlocked.Exchange(ref _connectBackoffMaxTicks, value.Ticks);
        }

        /// <inheritdoc />
        public void ResetConnectBackoff(string change)
        {
            ClearConnectBackoff(change);
        }

        /// <inheritdoc />
        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            LogDisconnecting(IpAddress!, Port);
            if (!_clientProxy.IsConnected)
            {
                LogNotConnected(IpAddress!, Port);
                return Task.CompletedTask;
            }

            _clientProxy.Disconnect();
            _connectionAccumulator.RecordDisconnected();
            LogDisconnected(IpAddress!, Port);

            return Task.CompletedTask;
        }

        private async Task EnsureClientIsConnectedAsync(CancellationToken cancellationToken)
        {
            if (_clientProxy.IsConnected && !_reconnectRequired)
            {
                LogAlreadyConnected(IpAddress!, Port);
                return;
            }

            ThrowIfBackingOff();

            await DisconnectAsync(cancellationToken);

            if (IpAddress == null)
            {
                throw new IpAddressNotSetException();
            }

            LogConnecting(IpAddress, Port);
            _connectionAccumulator.RecordConnectAttempt();
            var startedAt = _timeProvider.GetTimestamp();
            try
            {
                await _clientProxy.ConnectAsync(IpAddress, Port, ConnectionTimeout, cancellationToken);
            }
            catch (Exception)
            {
                var failedAt = _timeProvider.GetUtcNow().UtcDateTime;
                _connectionAccumulator.RecordConnectFailed(failedAt);
                ArmConnectBackoff(failedAt);

                throw;
            }

            _reconnectRequired = false;
            var wasBackingOff = Interlocked.Exchange(ref _backoffUntilTicks, 0) != 0;
            _connectionAccumulator.RecordConnected(_timeProvider.GetUtcNow().UtcDateTime, _timeProvider.GetElapsedTime(startedAt));
            LogConnected(IpAddress, Port);
            if (wasBackingOff)
            {
                LogConnectBackoffEnded(IpAddress, Port);
            }
        }

        /// <summary>
        ///     Fails the request outright while a backoff is still running, so a device that cannot be reached is not
        ///     contacted once per queued request. An elapsed backoff lets exactly one attempt through.
        /// </summary>
        private void ThrowIfBackingOff()
        {
            var backoffUntilTicks = Interlocked.Read(ref _backoffUntilTicks);
            if (backoffUntilTicks == 0)
            {
                return;
            }

            var nextAttemptAt = new DateTime(backoffUntilTicks, DateTimeKind.Utc);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            if (now >= nextAttemptAt)
            {
                return;
            }

            throw new LinkBackoffException(IpAddress ?? IPAddress.None, Port, _connectionAccumulator.ConsecutiveConnectFailures, nextAttemptAt, nextAttemptAt - now);
        }

        private void ArmConnectBackoff(DateTime failedAt)
        {
            var consecutiveConnectFailures = _connectionAccumulator.ConsecutiveConnectFailures;
            if (consecutiveConnectFailures < ConnectFailuresBeforeBackoff)
            {
                return;
            }

            var backoff = Exponential(ConnectBackoff, consecutiveConnectFailures - ConnectFailuresBeforeBackoff, ConnectBackoffMax);

            // "Never back off again" is a legitimate maximum, and the clock cannot hold failedAt plus one: the
            // addition threw out of the connect it was arming, so the caller saw an argument fault classified as a
            // transport error instead of the connect failure that actually happened.
            var nextAttemptAt = backoff.Ticks > DateTime.MaxValue.Ticks - failedAt.Ticks ? DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) : failedAt + backoff;
            Interlocked.Exchange(ref _backoffUntilTicks, nextAttemptAt.Ticks);
            _connectionAccumulator.RecordConnectBackoff(backoff, nextAttemptAt);
            LogConnectBackoffArmed(IpAddress!, Port, consecutiveConnectFailures, backoff, nextAttemptAt);
        }

        private void ClearConnectBackoff(string change)
        {
            var wasBackingOff = Interlocked.Exchange(ref _backoffUntilTicks, 0) != 0;
            _connectionAccumulator.ResetConnectBackoff();
            if (wasBackingOff)
            {
                LogConnectBackoffReset(change);
            }
        }

        /// <summary>
        ///     Closes the socket after a fault that says the stream can no longer be trusted, so the next request
        ///     reconnects instead of continuing on a half-open connection or reading a stray response.
        /// </summary>
        private void CloseSocketOnWireFault(Exception exception)
        {
            var outcome = ModbusOutcomeClassifier.Classify(exception);
            if (outcome is not (ModbusOutcome.Timeout or ModbusOutcome.TransportError or ModbusOutcome.ProtocolError))
            {
                return;
            }

            _reconnectRequired = true;
            if (!_clientProxy.IsConnected)
            {
                return;
            }

            _clientProxy.Disconnect();
            _connectionAccumulator.RecordDisconnected();
            LogSocketClosedAfterWireFault(IpAddress!, Port, outcome);
        }

        /// <summary><paramref name="unit" /> doubled <paramref name="doublings" /> times, capped at <paramref name="max" />.</summary>
        private static TimeSpan Exponential(TimeSpan unit, int doublings, TimeSpan max)
        {
            var ticks = unit.Ticks;
            for (var doubling = 0; doubling < doublings; doubling++)
            {
                // Stopping before the multiplication rather than after keeps a long run of failures from overflowing.
                if (ticks >= max.Ticks / 2)
                {
                    return max;
                }

                ticks *= 2;
            }

            return ticks >= max.Ticks ? max : TimeSpan.FromTicks(ticks);
        }

        [LoggerMessage(Level = LogLevel.Debug, Message = "Connect timeout set to {ConnectionTimeout}")]
        partial void LogConnectTimeoutSet(TimeSpan connectionTimeout);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Port set to {Port}")]
        partial void LogPortSet(int port);

        [LoggerMessage(Level = LogLevel.Debug, Message = "IP address set to {IpAddress}")]
        partial void LogIpAddressSet(IPAddress ipAddress);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Client is already connected to {IpAddress}:{Port}")]
        partial void LogAlreadyConnected(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to {IpAddress}:{Port}")]
        partial void LogConnecting(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Connected to {IpAddress}:{Port}")]
        partial void LogConnected(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Disconnecting from {IpAddress}:{Port}")]
        partial void LogDisconnecting(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Client is not connected to {IpAddress}:{Port}, nothing to disconnect")]
        partial void LogNotConnected(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected from {IpAddress}:{Port}")]
        partial void LogDisconnected(IPAddress ipAddress, int port);

        // Transitions only. A line per backed-off or reconnecting request would put tens of lines a second through
        // the edge log pipeline for the length of an outage; what a single request did is on its receipt.
        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Backing off from {IpAddress}:{Port} for {Backoff} after {ConsecutiveConnectFailures} consecutive failed connects; next attempt at {NextAttemptAt:O}")]
        partial void LogConnectBackoffArmed(IPAddress ipAddress, int port, int consecutiveConnectFailures, TimeSpan backoff, DateTime nextAttemptAt);

        [LoggerMessage(Level = LogLevel.Information, Message = "Connect backoff to {IpAddress}:{Port} ended: the connection was established")]
        partial void LogConnectBackoffEnded(IPAddress ipAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Connect backoff cleared by a change to {Change}; the next request attempts a connection")]
        partial void LogConnectBackoffReset(string change);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Closed the socket to {IpAddress}:{Port} after a {Outcome}; the next request reconnects")]
        partial void LogSocketClosedAfterWireFault(IPAddress ipAddress, int port, ModbusOutcome outcome);

        #endregion

        #region ModbusDataAccess

        #region DiscreteInputs

        /// <inheritdoc />
        public Task<bool[]> ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxBitsPerRead,
                                             operationTimeout,
                                             (unitId, _, token) => _clientProxy.ReadDiscreteInputsAsync(unitId, startingAddress, quantity, token),
                                             responseBuffer => _dataConverter.ConvertBitsToBools(responseBuffer, quantity),
                                             cancellationToken);
        }

        #endregion

        #region Coils

        /// <inheritdoc />
        public Task<bool[]> ReadCoilsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxBitsPerRead,
                                             operationTimeout,
                                             (unitId, _, token) => _clientProxy.ReadCoilsAsync(unitId, startingAddress, quantity, token),
                                             responseBuffer => _dataConverter.ConvertBitsToBools(responseBuffer, quantity),
                                             cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
                                              1,
                                              ModbusProtocolLimits.MaxBitsPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteSingleCoilAsync(unitId, registerAddress, value, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)values.Length,
                                              ModbusProtocolLimits.MaxBitsPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleCoilsAsync(unitId, startingAddress, values, token),
                                              cancellationToken);
        }

        #endregion

        #region InputRegisters

        /// <inheritdoc />
        public Task<byte[]> ReadInputRegistersRawAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxRegistersPerRead,
                                             operationTimeout,
                                             (unitId, _, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                             responseBuffer => responseBuffer.ToArray(),
                                             cancellationToken);
        }

        /// <inheritdoc />
        public Task<short[]> ReadInputRegistersAsShortAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            ushort quantity,
                                                            ByteOrder byteOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return ReadRegistersAs16BitAsync<short>(unitIdentifier,
                                                    startingAddress,
                                                    quantity,
                                                    operationTimeout,
                                                    (unitId, _, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<ushort[]> ReadInputRegistersAsUShortAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              ushort quantity,
                                                              ByteOrder byteOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return ReadRegistersAs16BitAsync<ushort>(unitIdentifier,
                                                     startingAddress,
                                                     quantity,
                                                     operationTimeout,
                                                     (unitId, _, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                     byteOrder,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task<int[]> ReadInputRegistersAsIntAsync(int unitIdentifier,
                                                        ushort startingAddress,
                                                        uint count,
                                                        ByteOrder byteOrder,
                                                        WordOrder32 wordOrder,
                                                        TimeSpan operationTimeout,
                                                        CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<int>(unitIdentifier,
                                                  startingAddress,
                                                  count,
                                                  operationTimeout,
                                                  (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                  byteOrder,
                                                  wordOrder,
                                                  cancellationToken);
        }

        /// <inheritdoc />
        public Task<uint[]> ReadInputRegistersAsUIntAsync(int unitIdentifier,
                                                          ushort startingAddress,
                                                          uint count,
                                                          ByteOrder byteOrder,
                                                          WordOrder32 wordOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<uint>(unitIdentifier,
                                                   startingAddress,
                                                   count,
                                                   operationTimeout,
                                                   (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                   byteOrder,
                                                   wordOrder,
                                                   cancellationToken);
        }

        /// <inheritdoc />
        public Task<float[]> ReadInputRegistersAsFloatAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            uint count,
                                                            ByteOrder byteOrder,
                                                            WordOrder32 wordOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<float>(unitIdentifier,
                                                    startingAddress,
                                                    count,
                                                    operationTimeout,
                                                    (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    wordOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<long[]> ReadInputRegistersAsLongAsync(int unitIdentifier,
                                                          ushort startingAddress,
                                                          uint count,
                                                          ByteOrder byteOrder,
                                                          WordOrder64 wordOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<long>(unitIdentifier,
                                                   startingAddress,
                                                   count,
                                                   operationTimeout,
                                                   (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                   byteOrder,
                                                   wordOrder,
                                                   cancellationToken);
        }

        /// <inheritdoc />
        public Task<ulong[]> ReadInputRegistersAsULongAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            uint count,
                                                            ByteOrder byteOrder,
                                                            WordOrder64 wordOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<ulong>(unitIdentifier,
                                                    startingAddress,
                                                    count,
                                                    operationTimeout,
                                                    (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    wordOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<double[]> ReadInputRegistersAsDoubleAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              uint count,
                                                              ByteOrder byteOrder,
                                                              WordOrder64 wordOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<double>(unitIdentifier,
                                                     startingAddress,
                                                     count,
                                                     operationTimeout,
                                                     (unitId, quantity, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                     byteOrder,
                                                     wordOrder,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> ReadInputRegistersAsStringAsync(int unitIdentifier,
                                                                  ushort startingAddress,
                                                                  ushort quantity,
                                                                  TextEncoding textEncoding,
                                                                  TimeSpan operationTimeout,
                                                                  CancellationToken cancellationToken)
        {
            var registerBytes = await ExecuteReadOperationAsync(unitIdentifier,
                                                                quantity,
                                                                ModbusProtocolLimits.MaxRegistersPerRead,
                                                                operationTimeout,
                                                                (unitId, _, token) => _clientProxy.ReadInputRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                                responseBuffer => responseBuffer.ToArray(),
                                                                cancellationToken)
                                    .ConfigureAwait(false);

            return _dataConverter.ConvertBytesToString(registerBytes, textEncoding);
        }

        #endregion

        #region HoldingRegisters

        /// <inheritdoc />
        public Task<byte[]> ReadHoldingRegistersRawAsync(int unitIdentifier,
                                                         ushort startingAddress,
                                                         ushort quantity,
                                                         TimeSpan operationTimeout,
                                                         CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxRegistersPerRead,
                                             operationTimeout,
                                             (unitId, _, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                             responseBuffer => responseBuffer.ToArray(),
                                             cancellationToken);
        }

        /// <inheritdoc />
        public Task<short[]> ReadHoldingRegistersAsShortAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              ushort quantity,
                                                              ByteOrder byteOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return ReadRegistersAs16BitAsync<short>(unitIdentifier,
                                                    startingAddress,
                                                    quantity,
                                                    operationTimeout,
                                                    (unitId, _, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<ushort[]> ReadHoldingRegistersAsUShortAsync(int unitIdentifier,
                                                                ushort startingAddress,
                                                                ushort quantity,
                                                                ByteOrder byteOrder,
                                                                TimeSpan operationTimeout,
                                                                CancellationToken cancellationToken)
        {
            return ReadRegistersAs16BitAsync<ushort>(unitIdentifier,
                                                     startingAddress,
                                                     quantity,
                                                     operationTimeout,
                                                     (unitId, _, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                     byteOrder,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task<int[]> ReadHoldingRegistersAsIntAsync(int unitIdentifier,
                                                          ushort startingAddress,
                                                          uint count,
                                                          ByteOrder byteOrder,
                                                          WordOrder32 wordOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<int>(unitIdentifier,
                                                  startingAddress,
                                                  count,
                                                  operationTimeout,
                                                  (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                  byteOrder,
                                                  wordOrder,
                                                  cancellationToken);
        }

        /// <inheritdoc />
        public Task<uint[]> ReadHoldingRegistersAsUIntAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            uint count,
                                                            ByteOrder byteOrder,
                                                            WordOrder32 wordOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<uint>(unitIdentifier,
                                                   startingAddress,
                                                   count,
                                                   operationTimeout,
                                                   (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                   byteOrder,
                                                   wordOrder,
                                                   cancellationToken);
        }

        /// <inheritdoc />
        public Task<float[]> ReadHoldingRegistersAsFloatAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              uint count,
                                                              ByteOrder byteOrder,
                                                              WordOrder32 wordOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return ReadRegistersAs32BitAsync<float>(unitIdentifier,
                                                    startingAddress,
                                                    count,
                                                    operationTimeout,
                                                    (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    wordOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<long[]> ReadHoldingRegistersAsLongAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            uint count,
                                                            ByteOrder byteOrder,
                                                            WordOrder64 wordOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<long>(unitIdentifier,
                                                   startingAddress,
                                                   count,
                                                   operationTimeout,
                                                   (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                   byteOrder,
                                                   wordOrder,
                                                   cancellationToken);
        }

        /// <inheritdoc />
        public Task<ulong[]> ReadHoldingRegistersAsULongAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              uint count,
                                                              ByteOrder byteOrder,
                                                              WordOrder64 wordOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<ulong>(unitIdentifier,
                                                    startingAddress,
                                                    count,
                                                    operationTimeout,
                                                    (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                    byteOrder,
                                                    wordOrder,
                                                    cancellationToken);
        }

        /// <inheritdoc />
        public Task<double[]> ReadHoldingRegistersAsDoubleAsync(int unitIdentifier,
                                                                ushort startingAddress,
                                                                uint count,
                                                                ByteOrder byteOrder,
                                                                WordOrder64 wordOrder,
                                                                TimeSpan operationTimeout,
                                                                CancellationToken cancellationToken)
        {
            return ReadRegistersAs64BitAsync<double>(unitIdentifier,
                                                     startingAddress,
                                                     count,
                                                     operationTimeout,
                                                     (unitId, quantity, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                     byteOrder,
                                                     wordOrder,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> ReadHoldingRegistersAsStringAsync(int unitIdentifier,
                                                                    ushort startingAddress,
                                                                    ushort quantity,
                                                                    TextEncoding textEncoding,
                                                                    TimeSpan operationTimeout,
                                                                    CancellationToken cancellationToken)
        {
            var registerBytes = await ExecuteReadOperationAsync(unitIdentifier,
                                                                quantity,
                                                                ModbusProtocolLimits.MaxRegistersPerRead,
                                                                operationTimeout,
                                                                (unitId, _, token) => _clientProxy.ReadHoldingRegistersAsync((byte)unitId, startingAddress, quantity, token),
                                                                responseBuffer => responseBuffer.ToArray(),
                                                                cancellationToken)
                                    .ConfigureAwait(false);

            return _dataConverter.ConvertBytesToString(registerBytes, textEncoding);
        }

        /// <inheritdoc />
        public Task WriteSingleHoldingRegisterAsync(int unitIdentifier,
                                                    ushort registerAddress,
                                                    short value,
                                                    ByteOrder byteOrder,
                                                    TimeSpan operationTimeout,
                                                    CancellationToken cancellationToken)
        {
            var registerBytes = _dataConverter.GetBytes(value);
            _dataConverter.SwapBytes(registerBytes, byteOrder);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              1,
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteSingleRegisterAsync((byte)unitId, registerAddress, registerBytes, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteSingleHoldingRegisterAsync(int unitIdentifier,
                                                    ushort registerAddress,
                                                    ushort value,
                                                    ByteOrder byteOrder,
                                                    TimeSpan operationTimeout,
                                                    CancellationToken cancellationToken)
        {
            var registerBytes = _dataConverter.GetBytes(value);
            _dataConverter.SwapBytes(registerBytes, byteOrder);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              1,
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteSingleRegisterAsync((byte)unitId, registerAddress, registerBytes, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersRawAsync(int unitIdentifier, ushort startingAddress, byte[] values, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)(values.Length / 2),
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleRegistersAsync((byte)unitId, startingAddress, values, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsShortAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              short[] values,
                                                              ByteOrder byteOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs16BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsUShortAsync(int unitIdentifier,
                                                               ushort startingAddress,
                                                               ushort[] values,
                                                               ByteOrder byteOrder,
                                                               TimeSpan operationTimeout,
                                                               CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs16BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsIntAsync(int unitIdentifier,
                                                            ushort startingAddress,
                                                            int[] values,
                                                            ByteOrder byteOrder,
                                                            WordOrder32 wordOrder,
                                                            TimeSpan operationTimeout,
                                                            CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs32BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsUIntAsync(int unitIdentifier,
                                                             ushort startingAddress,
                                                             uint[] values,
                                                             ByteOrder byteOrder,
                                                             WordOrder32 wordOrder,
                                                             TimeSpan operationTimeout,
                                                             CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs32BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsFloatAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              float[] values,
                                                              ByteOrder byteOrder,
                                                              WordOrder32 wordOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs32BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsLongAsync(int unitIdentifier,
                                                             ushort startingAddress,
                                                             long[] values,
                                                             ByteOrder byteOrder,
                                                             WordOrder64 wordOrder,
                                                             TimeSpan operationTimeout,
                                                             CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs64BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsULongAsync(int unitIdentifier,
                                                              ushort startingAddress,
                                                              ulong[] values,
                                                              ByteOrder byteOrder,
                                                              WordOrder64 wordOrder,
                                                              TimeSpan operationTimeout,
                                                              CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs64BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsDoubleAsync(int unitIdentifier,
                                                               ushort startingAddress,
                                                               double[] values,
                                                               ByteOrder byteOrder,
                                                               WordOrder64 wordOrder,
                                                               TimeSpan operationTimeout,
                                                               CancellationToken cancellationToken)
        {
            return WriteHoldingRegistersAs64BitAsync(unitIdentifier,
                                                     startingAddress,
                                                     values,
                                                     byteOrder,
                                                     wordOrder,
                                                     operationTimeout,
                                                     cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersAsStringAsync(int unitIdentifier,
                                                               ushort startingAddress,
                                                               string value,
                                                               TextEncoding textEncoding,
                                                               TimeSpan operationTimeout,
                                                               CancellationToken cancellationToken)
        {
            var registerBytes = _dataConverter.ConvertStringToBytes(value, textEncoding);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)(registerBytes.Length / 2),
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleRegistersAsync((byte)unitId, startingAddress, registerBytes, token),
                                              cancellationToken);
        }

        private Task WriteHoldingRegistersAs16BitAsync<T>(int unitIdentifier,
                                                          ushort startingAddress,
                                                          T[] values,
                                                          ByteOrder byteOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
            where T : unmanaged
        {
            var registerBytes = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(registerBytes, byteOrder);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)(registerBytes.Length / 2),
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleRegistersAsync((byte)unitId, startingAddress, registerBytes, token),
                                              cancellationToken);
        }

        private Task WriteHoldingRegistersAs32BitAsync<T>(int unitIdentifier,
                                                          ushort startingAddress,
                                                          T[] values,
                                                          ByteOrder byteOrder,
                                                          WordOrder32 wordOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
            where T : unmanaged
        {
            var registerBytes = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(registerBytes, byteOrder);
            _dataConverter.SwapWords(registerBytes, wordOrder);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)(registerBytes.Length / 2),
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleRegistersAsync((byte)unitId, startingAddress, registerBytes, token),
                                              cancellationToken);
        }

        private Task WriteHoldingRegistersAs64BitAsync<T>(int unitIdentifier,
                                                          ushort startingAddress,
                                                          T[] values,
                                                          ByteOrder byteOrder,
                                                          WordOrder64 wordOrder,
                                                          TimeSpan operationTimeout,
                                                          CancellationToken cancellationToken)
            where T : unmanaged
        {
            var registerBytes = _dataConverter.CastToBytes(values);
            _dataConverter.SwapBytes(registerBytes, byteOrder);
            _dataConverter.SwapWords(registerBytes, wordOrder);

            return ExecuteWriteOperationAsync(unitIdentifier,
                                              (uint)(registerBytes.Length / 2),
                                              ModbusProtocolLimits.MaxRegistersPerWrite,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteMultipleRegistersAsync((byte)unitId, startingAddress, registerBytes, token),
                                              cancellationToken);
        }

        #endregion

        private Task<T[]> ReadRegistersAs16BitAsync<T>(int unitIdentifier,
                                                       ushort startingAddress,
                                                       ushort quantity,
                                                       TimeSpan operationTimeout,
                                                       Func<int, ushort, CancellationToken, Task<Memory<byte>>> operation,
                                                       ByteOrder byteOrder,
                                                       CancellationToken cancellationToken)
            where T : unmanaged
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxRegistersPerRead,
                                             operationTimeout,
                                             operation,
                                             responseBuffer =>
                                             {
                                                 _validator.ValidateResponseAlignment(responseBuffer.Length, 2, unitIdentifier, startingAddress);
                                                 _dataConverter.SwapBytes(responseBuffer, byteOrder);

                                                 return _dataConverter.CastFromBytes<T>(responseBuffer);
                                             },
                                             cancellationToken);
        }

        private Task<T[]> ReadRegistersAs32BitAsync<T>(int unitIdentifier,
                                                       ushort startingAddress,
                                                       uint count,
                                                       TimeSpan operationTimeout,
                                                       Func<int, ushort, CancellationToken, Task<Memory<byte>>> operation,
                                                       ByteOrder byteOrder,
                                                       WordOrder32 wordOrder,
                                                       CancellationToken cancellationToken)
            where T : unmanaged
        {
            var quantity = _dataConverter.ConvertCountToQuantity(count, BytesPer32BitValue);

            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxRegistersPerRead,
                                             operationTimeout,
                                             operation,
                                             responseBuffer =>
                                             {
                                                 _validator.ValidateResponseAlignment(responseBuffer.Length, BytesPer32BitValue, unitIdentifier, startingAddress);
                                                 _dataConverter.SwapBytes(responseBuffer, byteOrder);
                                                 _dataConverter.SwapWords(responseBuffer, wordOrder);

                                                 return _dataConverter.CastFromBytes<T>(responseBuffer);
                                             },
                                             cancellationToken);
        }

        private Task<T[]> ReadRegistersAs64BitAsync<T>(int unitIdentifier,
                                                       ushort startingAddress,
                                                       uint count,
                                                       TimeSpan operationTimeout,
                                                       Func<int, ushort, CancellationToken, Task<Memory<byte>>> operation,
                                                       ByteOrder byteOrder,
                                                       WordOrder64 wordOrder,
                                                       CancellationToken cancellationToken)
            where T : unmanaged
        {
            var quantity = _dataConverter.ConvertCountToQuantity(count, BytesPer64BitValue);

            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
                                             ModbusProtocolLimits.MaxRegistersPerRead,
                                             operationTimeout,
                                             operation,
                                             responseBuffer =>
                                             {
                                                 _validator.ValidateResponseAlignment(responseBuffer.Length, BytesPer64BitValue, unitIdentifier, startingAddress);
                                                 _dataConverter.SwapBytes(responseBuffer, byteOrder);
                                                 _dataConverter.SwapWords(responseBuffer, wordOrder);

                                                 return _dataConverter.CastFromBytes<T>(responseBuffer);
                                             },
                                             cancellationToken);
        }

        private async Task<T[]> ExecuteReadOperationAsync<T>(int unitIdentifier,
                                                             ushort quantity,
                                                             int protocolLimit,
                                                             TimeSpan operationTimeout,
                                                             Func<int, ushort, CancellationToken, Task<Memory<byte>>> operation,
                                                             Func<Memory<byte>, T[]> processResponse,
                                                             CancellationToken cancellationToken)
        {
            _validator.ValidateUnitIdentifier(unitIdentifier);
            _validator.ValidateQuantity(quantity, protocolLimit);

            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? linkedCts = null;
            try
            {
                await EnsureClientIsConnectedAsync(cancellationToken);

                timeoutCts = new CancellationTokenSource(operationTimeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                var responseBuffer = await operation(unitIdentifier, quantity, linkedCts.Token).ConfigureAwait(false);

                return processResponse(responseBuffer);
            }
            catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true })
            {
                var timeout = new OperationTimeoutException();
                CloseSocketOnWireFault(timeout);

                throw timeout;
            }
            catch (Exception exception)
            {
                CloseSocketOnWireFault(exception);

                throw;
            }
            finally
            {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }

        private async Task ExecuteWriteOperationAsync(int unitIdentifier,
                                                      uint quantity,
                                                      int protocolLimit,
                                                      TimeSpan operationTimeout,
                                                      Func<int, CancellationToken, Task> operation,
                                                      CancellationToken cancellationToken)
        {
            _validator.ValidateUnitIdentifier(unitIdentifier);
            _validator.ValidateQuantity(quantity, protocolLimit);

            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? linkedCts = null;
            try
            {
                await EnsureClientIsConnectedAsync(cancellationToken);

                timeoutCts = new CancellationTokenSource(operationTimeout);
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
                await operation(unitIdentifier, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true })
            {
                var timeout = new OperationTimeoutException();
                CloseSocketOnWireFault(timeout);

                throw timeout;
            }
            catch (Exception exception)
            {
                CloseSocketOnWireFault(exception);

                throw;
            }
            finally
            {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
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

            if (!disposing)
            {
                _disposed = true;
                return;
            }

            try
            {
                _clientProxy.Dispose();
            }
            catch (Exception exception)
            {
                LogFailedToDisposeModbusClient(exception);
            }

            _disposed = true;
        }

        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to dispose Modbus client")]
        partial void LogFailedToDisposeModbusClient(Exception exception);

        #endregion
    }

    /// <summary>
    ///     Exception thrown when attempting to connect without setting an IP address.
    /// </summary>
    public class IpAddressNotSetException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="IpAddressNotSetException" /> class.
        /// </summary>
        public IpAddressNotSetException() : base("IP address must be set before attempting to connect.")
        {
        }
    }
}