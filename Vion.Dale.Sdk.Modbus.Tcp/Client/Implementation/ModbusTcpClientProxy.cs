using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
using Microsoft.Extensions.Logging;
using ModbusException = Vion.Dale.Sdk.Modbus.Core.Exceptions.ModbusException;
using ModbusExceptionCode = Vion.Dale.Sdk.Modbus.Core.Exceptions.ModbusExceptionCode;
using ModbusFunctionCode = Vion.Contracts.FlatBuffers.Hw.Modbus.ModbusFunctionCode;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation
{
    [ExcludeFromCodeCoverage]
    internal partial class ModbusTcpClientProxy : IModbusTcpClientProxy
    {
        private readonly ILogger<ModbusTcpClientProxy> _logger;

        private readonly ModbusTcpClient _modbusTcpClient;

        private bool _disposed;

        private TcpClient? _tcpClient;

        public ModbusTcpClientProxy(ILogger<ModbusTcpClientProxy> logger)
        {
            _modbusTcpClient = new ModbusTcpClient();
            _logger = logger;
        }

        /// <inheritdoc />
        public bool IsConnected
        {
            get => _modbusTcpClient.IsConnected;
        }

        /// <inheritdoc />
        public async Task ConnectAsync(IPAddress ipAddress, int port, TimeSpan connectionTimeout, CancellationToken cancellationToken)
        {
            _tcpClient = new TcpClient();
            var connectTask = _tcpClient.ConnectAsync(ipAddress, port);
            var timeoutOrCancellationTask = Task.Delay(connectionTimeout, cancellationToken);
            var completed = await Task.WhenAny(connectTask, timeoutOrCancellationTask).ConfigureAwait(false);
            if (completed == connectTask)
            {
                await connectTask.ConfigureAwait(false); // await to observe exceptions
                _modbusTcpClient.Initialize(_tcpClient, ModbusEndianness.LittleEndian); // Endianness is irrelevant, all byte and word swapping is done manually
                return;
            }

            _tcpClient.Dispose();
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }

            throw new ConnectionTimeoutException(connectionTimeout.TotalSeconds);
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            _tcpClient?.Close();
            _modbusTcpClient.Disconnect();
        }

        /// <inheritdoc />
        public async Task<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.ReadDiscreteInputs;
            LogExecutingReadOperation(functionCode, unitIdentifier, startingAddress, quantity);
            try
            {
                var discreteInputs = await _modbusTcpClient.ReadDiscreteInputsAsync(unitIdentifier, startingAddress, quantity, cancellationToken).ConfigureAwait(false);
                LogExecutedReadOperation(functionCode, unitIdentifier, startingAddress, quantity);

                return discreteInputs;
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task<Memory<byte>> ReadCoilsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.ReadCoils;
            LogExecutingReadOperation(functionCode, unitIdentifier, startingAddress, quantity);
            try
            {
                var coils = await _modbusTcpClient.ReadCoilsAsync(unitIdentifier, startingAddress, quantity, cancellationToken).ConfigureAwait(false);
                LogExecutedReadOperation(functionCode, unitIdentifier, startingAddress, quantity);

                return coils;
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.WriteSingleCoil;
            LogExecutingWriteOperation(functionCode, unitIdentifier, registerAddress);

            try
            {
                await _modbusTcpClient.WriteSingleCoilAsync(unitIdentifier, registerAddress, value, cancellationToken).ConfigureAwait(false);
                LogExecutedWriteOperation(functionCode, unitIdentifier, registerAddress);
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.WriteMultipleCoils;
            LogExecutedWriteOperation(functionCode, unitIdentifier, startingAddress);
            try
            {
                await _modbusTcpClient.WriteMultipleCoilsAsync(unitIdentifier, startingAddress, values, cancellationToken).ConfigureAwait(false);
                LogExecutedWriteOperation(functionCode, unitIdentifier, startingAddress);
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task<Memory<byte>> ReadInputRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.ReadInputRegisters;
            LogExecutingReadOperation(functionCode, unitIdentifier, startingAddress, quantity);
            try
            {
                var inputRegisters = await _modbusTcpClient.ReadInputRegistersAsync(unitIdentifier, startingAddress, quantity, cancellationToken).ConfigureAwait(false);
                LogExecutedReadOperation(functionCode, unitIdentifier, startingAddress, quantity);

                return inputRegisters;
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task<Memory<byte>> ReadHoldingRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.ReadHoldingRegisters;
            LogExecutingReadOperation(functionCode, unitIdentifier, startingAddress, quantity);
            try
            {
                var holdingRegisters = await _modbusTcpClient.ReadHoldingRegistersAsync(unitIdentifier, startingAddress, quantity, cancellationToken).ConfigureAwait(false);
                LogExecutedReadOperation(functionCode, unitIdentifier, startingAddress, quantity);

                return holdingRegisters;
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task WriteSingleRegisterAsync(byte unitIdentifier, ushort registerAddress, byte[] value, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.WriteSingleRegister;
            LogExecutingWriteOperation(functionCode, unitIdentifier, registerAddress);
            try
            {
                await _modbusTcpClient.WriteSingleRegisterAsync(unitIdentifier, registerAddress, value, cancellationToken).ConfigureAwait(false);
                LogExecutedWriteOperation(functionCode, unitIdentifier, registerAddress);
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

        /// <inheritdoc />
        public async Task WriteMultipleRegistersAsync(byte unitIdentifier, ushort startingAddress, byte[] values, CancellationToken cancellationToken)
        {
            const ModbusFunctionCode functionCode = ModbusFunctionCode.WriteMultipleRegisters;
            LogExecutingWriteOperation(functionCode, unitIdentifier, startingAddress);
            try
            {
                await _modbusTcpClient.WriteMultipleRegistersAsync(unitIdentifier, startingAddress, values, cancellationToken).ConfigureAwait(false);
                LogExecutingWriteOperation(functionCode, unitIdentifier, startingAddress);
            }
            catch (Exception exception)
            {
                throw ToSdkException(exception);
            }
        }

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
                LogDisposing(nameof(TcpClient));
                _tcpClient?.Dispose();
                LogDisposed(nameof(TcpClient));

                LogDisposing(nameof(ModbusTcpClient));
                _modbusTcpClient.Dispose();
                LogDisposed(nameof(ModbusTcpClient));
            }

            _disposed = true;
        }

        private static Exception ToSdkException(Exception exception)
        {
            if (exception is not FluentModbus.ModbusException modbusException)
            {
                return exception;
            }

            return modbusException.ExceptionCode == (FluentModbus.ModbusExceptionCode)0xFF ? new ModbusException(modbusException.Message) :
                       new ModbusException((ModbusExceptionCode)modbusException.ExceptionCode, modbusException.Message);
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Executing read operation (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Quantity={Quantity})")]
        partial void LogExecutingReadOperation(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, ushort quantity);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Executed read operation (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address}, Quantity={Quantity})")]
        partial void LogExecutedReadOperation(ModbusFunctionCode functionCode, int unitIdentifier, ushort address, ushort quantity);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Executing write operation (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address})")]
        partial void LogExecutingWriteOperation(ModbusFunctionCode functionCode, int unitIdentifier, ushort address);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Executed write operation (FunctionCode={FunctionCode}, UnitIdentifier={UnitIdentifier}, Address={Address})")]
        partial void LogExecutedWriteOperation(ModbusFunctionCode functionCode, int unitIdentifier, ushort address);

        [LoggerMessage(Level = LogLevel.Information, Message = "Disposing {name}")]
        partial void LogDisposing(string name);

        [LoggerMessage(Level = LogLevel.Information, Message = "{name} disposed")]
        partial void LogDisposed(string name);
    }

    /// <summary>
    ///     Exception thrown when a connection attempt does not complete within the specified timeout period.
    /// </summary>
    public class ConnectionTimeoutException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ConnectionTimeoutException" /> class with the specified timeout duration.
        /// </summary>
        /// <param name="seconds">The connection timeout limit in seconds that was exceeded.</param>
        public ConnectionTimeoutException(double seconds) : base($"The connection could not be established within {seconds} seconds.")
        {
        }
    }
}