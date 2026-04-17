using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation
{
    internal partial class ModbusTcpClientWrapper : IModbusTcpClientWrapper
    {
        private const int BytesPer32BitValue = 4;

        private const int BytesPer64BitValue = 8;

        private readonly IModbusTcpClientProxy _clientProxy;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ILogger<ModbusTcpClientWrapper> _logger;

        private readonly IModbusValidator _validator;

        private bool _disposed;

        private bool _reconnectRequired;

        public ModbusTcpClientWrapper(IModbusTcpClientProxy clientProxy, IModbusValidator validator, IModbusDataConverter dataConverter, ILogger<ModbusTcpClientWrapper> logger)
        {
            _clientProxy = clientProxy;
            _validator = validator;
            _dataConverter = dataConverter;
            _logger = logger;
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
                field = value;
                LogPortSet(value);
                _reconnectRequired = true;
            }
        }

        /// <inheritdoc />
        public IPAddress? IpAddress
        {
            get;

            set
            {
                field = value;
                LogIpAddressSet(value!);
                _reconnectRequired = true;
            }
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

            await DisconnectAsync(cancellationToken);

            if (IpAddress == null)
            {
                throw new IpAddressNotSetException();
            }

            LogConnecting(IpAddress, Port);
            await _clientProxy.ConnectAsync(IpAddress, Port, ConnectionTimeout, cancellationToken);
            _reconnectRequired = false;
            LogConnected(IpAddress, Port);
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

        #endregion

        #region ModbusDataAccess

        #region DiscreteInputs

        /// <inheritdoc />
        public Task<bool[]> ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteReadOperationAsync(unitIdentifier,
                                             quantity,
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
                                             operationTimeout,
                                             (unitId, _, token) => _clientProxy.ReadCoilsAsync(unitId, startingAddress, quantity, token),
                                             responseBuffer => _dataConverter.ConvertBitsToBools(responseBuffer, quantity),
                                             cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteSingleCoilAsync(unitId, registerAddress, value, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
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
                                              operationTimeout,
                                              (unitId, token) => _clientProxy.WriteSingleRegisterAsync((byte)unitId, registerAddress, registerBytes, token),
                                              cancellationToken);
        }

        /// <inheritdoc />
        public Task WriteMultipleHoldingRegistersRawAsync(int unitIdentifier, ushort startingAddress, byte[] values, TimeSpan operationTimeout, CancellationToken cancellationToken)
        {
            return ExecuteWriteOperationAsync(unitIdentifier,
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
                                                             TimeSpan operationTimeout,
                                                             Func<int, ushort, CancellationToken, Task<Memory<byte>>> operation,
                                                             Func<Memory<byte>, T[]> processResponse,
                                                             CancellationToken cancellationToken)
        {
            _validator.ValidateUnitIdentifier(unitIdentifier);

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
                throw new OperationTimeoutException();
            }
            finally
            {
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }

        private async Task ExecuteWriteOperationAsync(int unitIdentifier,
                                                      TimeSpan operationTimeout,
                                                      Func<int, CancellationToken, Task> operation,
                                                      CancellationToken cancellationToken)
        {
            _validator.ValidateUnitIdentifier(unitIdentifier);

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
                throw new OperationTimeoutException();
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