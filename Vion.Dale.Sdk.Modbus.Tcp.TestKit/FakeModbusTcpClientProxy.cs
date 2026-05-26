using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     In-memory fake for <see cref="IModbusTcpClientProxy" />. Stores register / coil contents in
    ///     raw bytes per (unitId, address); the SDK's real <c>ModbusTcpClientWrapper</c> sits above and
    ///     handles every byte / word-order conversion against the bytes returned here. This is the
    ///     substitution layer that lets a test exercise the SUT's byte-level wire-format handling
    ///     without a socket, without a background thread, and without the FluentModbus dependency.
    ///     <para>
    ///         All operations are recorded for verification — see <see cref="ReadHistory" />,
    ///         <see cref="WriteHistory" />, <see cref="ConnectionHistory" />. Day-one happy path only;
    ///         fault injection (exception codes, timeouts, disconnects) is planned for follow-up commits.
    ///     </para>
    /// </summary>
    [PublicApi]
    public sealed class FakeModbusTcpClientProxy : IModbusTcpClientProxy
    {
        private readonly Dictionary<(int UnitId, ushort Address), byte[]> _holdingRegisters = new();

        private readonly Dictionary<(int UnitId, ushort Address), byte[]> _inputRegisters = new();

        private readonly Dictionary<(int UnitId, ushort Address), bool> _coils = new();

        private readonly Dictionary<(int UnitId, ushort Address), bool> _discreteInputs = new();

        private readonly List<ConnectionEvent> _connectionHistory = new();

        private readonly List<ReadEvent> _readHistory = new();

        private readonly List<WriteEvent> _writeHistory = new();

        // FIFO queue of pending faults per (op kind, unitId, address). Consumed on the next matching
        // operation; once empty, the operation falls through to the in-memory store. Stacking multiple
        // faults supports "fail, fail, recover" sequences without explicit cleanup.
        private readonly Dictionary<(OperationKind Op, int UnitId, ushort Address), Queue<Exception>> _pendingFaults = new();

        /// <summary>
        ///     True after <c>ConnectAsync</c> has been called and before <c>Disconnect</c>.
        ///     The real wrapper calls <c>ConnectAsync</c> lazily on the first operation if disconnected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>Ordered log of every <c>ConnectAsync</c> / <c>Disconnect</c> the fake observed.</summary>
        public IReadOnlyList<ConnectionEvent> ConnectionHistory
        {
            get => _connectionHistory;
        }

        /// <summary>Ordered log of every read the SUT issued through the proxy layer.</summary>
        public IReadOnlyList<ReadEvent> ReadHistory
        {
            get => _readHistory;
        }

        /// <summary>Ordered log of every write the SUT issued through the proxy layer.</summary>
        public IReadOnlyList<WriteEvent> WriteHistory
        {
            get => _writeHistory;
        }

        #region Pre-population helpers

        /// <summary>
        ///     Pre-populates one 16-bit holding register at (unitId, address) with two bytes
        ///     <paramref name="msb" /> + <paramref name="lsb" /> in standard Modbus big-endian wire order.
        /// </summary>
        public void SetHoldingRegister(int unitId, ushort address, byte msb, byte lsb)
        {
            _holdingRegisters[(unitId, address)] = new[] { msb, lsb };
        }

        /// <summary>
        ///     Pre-populates a contiguous range of holding registers starting at <paramref name="startingAddress" />.
        ///     <paramref name="registerBytes" /> must be a multiple of 2 (one register = 2 bytes, big-endian).
        /// </summary>
        public void SetHoldingRegisters(int unitId, ushort startingAddress, byte[] registerBytes)
        {
            EnsureRegisterByteAlignment(registerBytes, nameof(registerBytes));
            StoreContiguous(_holdingRegisters, unitId, startingAddress, registerBytes);
        }

        /// <summary>Pre-populates a single input register. Mirrors <see cref="SetHoldingRegister" />.</summary>
        public void SetInputRegister(int unitId, ushort address, byte msb, byte lsb)
        {
            _inputRegisters[(unitId, address)] = new[] { msb, lsb };
        }

        /// <summary>Pre-populates a contiguous range of input registers. Mirrors <see cref="SetHoldingRegisters" />.</summary>
        public void SetInputRegisters(int unitId, ushort startingAddress, byte[] registerBytes)
        {
            EnsureRegisterByteAlignment(registerBytes, nameof(registerBytes));
            StoreContiguous(_inputRegisters, unitId, startingAddress, registerBytes);
        }

        /// <summary>Pre-populates a single coil value.</summary>
        public void SetCoil(int unitId, ushort address, bool value)
        {
            _coils[(unitId, address)] = value;
        }

        /// <summary>Pre-populates a single discrete input value.</summary>
        public void SetDiscreteInput(int unitId, ushort address, bool value)
        {
            _discreteInputs[(unitId, address)] = value;
        }

        #endregion

        #region Fault injection

        /// <summary>
        ///     Queues an exception to throw on the next read at (unitId, startingAddress). Faults are
        ///     consumed FIFO — calling this twice queues two faults; the third read at that address
        ///     succeeds from the in-memory store. Matches by the operation's <em>starting</em> address;
        ///     in Modbus, exception responses apply to the whole request, not per-register.
        /// </summary>
        public void EnqueueReadFault(int unitId, ushort startingAddress, Exception exception)
        {
            EnqueueFault(OperationKind.Read, unitId, startingAddress, exception);
        }

        /// <summary>
        ///     Queues an exception to throw on the next write at (unitId, address). FIFO consumption,
        ///     same as <see cref="EnqueueReadFault" />.
        /// </summary>
        public void EnqueueWriteFault(int unitId, ushort address, Exception exception)
        {
            EnqueueFault(OperationKind.Write, unitId, address, exception);
        }

        /// <summary>
        ///     Convenience: queues a Modbus protocol exception (e.g. <see cref="ModbusExceptionCode.IllegalDataAddress" />)
        ///     to surface on the next read at (unitId, startingAddress).
        /// </summary>
        public void EnqueueReadModbusException(int unitId, ushort startingAddress, ModbusExceptionCode code, string? message = null)
        {
            EnqueueReadFault(unitId, startingAddress, new ModbusException(code, message ?? $"Modbus exception {code} simulated by FakeModbusTcpClientProxy."));
        }

        /// <summary>
        ///     Convenience: queues a Modbus protocol exception (e.g. <see cref="ModbusExceptionCode.IllegalDataValue" />)
        ///     to surface on the next write at (unitId, address).
        /// </summary>
        public void EnqueueWriteModbusException(int unitId, ushort address, ModbusExceptionCode code, string? message = null)
        {
            EnqueueWriteFault(unitId, address, new ModbusException(code, message ?? $"Modbus exception {code} simulated by FakeModbusTcpClientProxy."));
        }

        private void EnqueueFault(OperationKind op, int unitId, ushort address, Exception exception)
        {
            var key = (op, unitId, address);
            if (!_pendingFaults.TryGetValue(key, out var queue))
            {
                queue = new Queue<Exception>();
                _pendingFaults[key] = queue;
            }

            queue.Enqueue(exception);
        }

        private bool TryDequeueFault(OperationKind op, int unitId, ushort address, out Exception? fault)
        {
            if (_pendingFaults.TryGetValue((op, unitId, address), out var queue) && queue.Count > 0)
            {
                fault = queue.Dequeue();
                return true;
            }

            fault = null;
            return false;
        }

        #endregion

        #region IModbusTcpClientProxy

        Task IModbusTcpClientProxy.ConnectAsync(IPAddress ipAddress, int port, TimeSpan connectionTimeout, CancellationToken cancellationToken)
        {
            _connectionHistory.Add(new ConnectionEvent(ConnectionEventKind.Connect, ipAddress, port));
            IsConnected = true;
            return Task.CompletedTask;
        }

        void IModbusTcpClientProxy.Disconnect()
        {
            _connectionHistory.Add(new ConnectionEvent(ConnectionEventKind.Disconnect, null, null));
            IsConnected = false;
        }

        Task<Memory<byte>> IModbusTcpClientProxy.ReadDiscreteInputsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            _readHistory.Add(new ReadEvent(ReadEventKind.DiscreteInputs, unitIdentifier, startingAddress, quantity));
            ThrowIfFaultEnqueued(OperationKind.Read, unitIdentifier, startingAddress);
            return Task.FromResult<Memory<byte>>(PackBitsAsCoilBytes(_discreteInputs, unitIdentifier, startingAddress, quantity));
        }

        Task<Memory<byte>> IModbusTcpClientProxy.ReadCoilsAsync(int unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            _readHistory.Add(new ReadEvent(ReadEventKind.Coils, unitIdentifier, startingAddress, quantity));
            ThrowIfFaultEnqueued(OperationKind.Read, unitIdentifier, startingAddress);
            return Task.FromResult<Memory<byte>>(PackBitsAsCoilBytes(_coils, unitIdentifier, startingAddress, quantity));
        }

        Task IModbusTcpClientProxy.WriteSingleCoilAsync(int unitIdentifier, ushort registerAddress, bool value, CancellationToken cancellationToken)
        {
            _writeHistory.Add(new WriteEvent(WriteEventKind.SingleCoil, unitIdentifier, registerAddress, new[] { value ? (byte)0xFF : (byte)0x00 }));
            ThrowIfFaultEnqueued(OperationKind.Write, unitIdentifier, registerAddress);
            _coils[(unitIdentifier, registerAddress)] = value;
            return Task.CompletedTask;
        }

        Task IModbusTcpClientProxy.WriteMultipleCoilsAsync(int unitIdentifier, ushort startingAddress, bool[] values, CancellationToken cancellationToken)
        {
            var packed = new byte[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                packed[i] = values[i] ? (byte)0x01 : (byte)0x00;
            }

            _writeHistory.Add(new WriteEvent(WriteEventKind.MultipleCoils, unitIdentifier, startingAddress, packed));
            ThrowIfFaultEnqueued(OperationKind.Write, unitIdentifier, startingAddress);
            for (var i = 0; i < values.Length; i++)
            {
                _coils[(unitIdentifier, (ushort)(startingAddress + i))] = values[i];
            }

            return Task.CompletedTask;
        }

        Task<Memory<byte>> IModbusTcpClientProxy.ReadInputRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            _readHistory.Add(new ReadEvent(ReadEventKind.InputRegisters, unitIdentifier, startingAddress, quantity));
            ThrowIfFaultEnqueued(OperationKind.Read, unitIdentifier, startingAddress);
            return Task.FromResult<Memory<byte>>(ReadRegisterBytes(_inputRegisters, unitIdentifier, startingAddress, quantity));
        }

        Task<Memory<byte>> IModbusTcpClientProxy.ReadHoldingRegistersAsync(byte unitIdentifier, ushort startingAddress, ushort quantity, CancellationToken cancellationToken)
        {
            _readHistory.Add(new ReadEvent(ReadEventKind.HoldingRegisters, unitIdentifier, startingAddress, quantity));
            ThrowIfFaultEnqueued(OperationKind.Read, unitIdentifier, startingAddress);
            return Task.FromResult<Memory<byte>>(ReadRegisterBytes(_holdingRegisters, unitIdentifier, startingAddress, quantity));
        }

        Task IModbusTcpClientProxy.WriteSingleRegisterAsync(byte unitIdentifier, ushort registerAddress, byte[] value, CancellationToken cancellationToken)
        {
            if (value.Length != 2)
            {
                throw new ArgumentException("Single register write must be exactly 2 bytes.", nameof(value));
            }

            // Defensive copy so the recorded snapshot doesn't alias the caller's buffer.
            _writeHistory.Add(new WriteEvent(WriteEventKind.SingleRegister, unitIdentifier, registerAddress, new[] { value[0], value[1] }));
            ThrowIfFaultEnqueued(OperationKind.Write, unitIdentifier, registerAddress);
            _holdingRegisters[(unitIdentifier, registerAddress)] = new[] { value[0], value[1] };
            return Task.CompletedTask;
        }

        Task IModbusTcpClientProxy.WriteMultipleRegistersAsync(byte unitIdentifier, ushort startingAddress, byte[] values, CancellationToken cancellationToken)
        {
            EnsureRegisterByteAlignment(values, nameof(values));
            // Defensive copy — the SDK reuses internal buffers in production; tests must not alias them.
            var snapshot = new byte[values.Length];
            Buffer.BlockCopy(values, 0, snapshot, 0, values.Length);
            _writeHistory.Add(new WriteEvent(WriteEventKind.MultipleRegisters, unitIdentifier, startingAddress, snapshot));
            ThrowIfFaultEnqueued(OperationKind.Write, unitIdentifier, startingAddress);
            StoreContiguous(_holdingRegisters, unitIdentifier, startingAddress, values);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            // No unmanaged resources — the fake holds plain managed state.
        }

        #endregion

        #region Internals

        private enum OperationKind
        {
            Read,

            Write,
        }

        private void ThrowIfFaultEnqueued(OperationKind op, int unitId, ushort address)
        {
            if (TryDequeueFault(op, unitId, address, out var fault))
            {
                throw fault!;
            }
        }

        private static void EnsureRegisterByteAlignment(byte[] bytes, string paramName)
        {
            if (bytes.Length % 2 != 0)
            {
                throw new ArgumentException("Register data must be a multiple of 2 bytes (one register = 2 bytes).", paramName);
            }
        }

        private static void StoreContiguous(Dictionary<(int UnitId, ushort Address), byte[]> store, int unitId, ushort startingAddress, byte[] bytes)
        {
            for (var i = 0; i < bytes.Length; i += 2)
            {
                var address = (ushort)(startingAddress + i / 2);
                store[(unitId, address)] = new[] { bytes[i], bytes[i + 1] };
            }
        }

        private static byte[] ReadRegisterBytes(Dictionary<(int UnitId, ushort Address), byte[]> store, int unitId, ushort startingAddress, ushort quantity)
        {
            var bytes = new byte[quantity * 2];
            for (var i = 0; i < quantity; i++)
            {
                var address = (ushort)(startingAddress + i);
                if (store.TryGetValue((unitId, address), out var data))
                {
                    bytes[i * 2] = data[0];
                    bytes[i * 2 + 1] = data[1];
                }
                // Not pre-populated → leaves zero, which matches what an uninitialised real register would return.
            }

            return bytes;
        }

        private static byte[] PackBitsAsCoilBytes(Dictionary<(int UnitId, ushort Address), bool> store, int unitId, ushort startingAddress, ushort quantity)
        {
            // Modbus packs N coils into ceil(N / 8) bytes, LSB-first within each byte (per spec).
            var byteCount = (quantity + 7) / 8;
            var packed = new byte[byteCount];
            for (var i = 0; i < quantity; i++)
            {
                var address = (ushort)(startingAddress + i);
                if (store.TryGetValue((unitId, address), out var value) && value)
                {
                    packed[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            return packed;
        }

        #endregion
    }

    /// <summary>The kind of connection event recorded on <see cref="FakeModbusTcpClientProxy.ConnectionHistory" />.</summary>
    [PublicApi]
    public enum ConnectionEventKind
    {
        Connect,

        Disconnect,
    }

    /// <summary>The Modbus function family of a read recorded on <see cref="FakeModbusTcpClientProxy.ReadHistory" />.</summary>
    [PublicApi]
    public enum ReadEventKind
    {
        HoldingRegisters,

        InputRegisters,

        Coils,

        DiscreteInputs,
    }

    /// <summary>The Modbus function family of a write recorded on <see cref="FakeModbusTcpClientProxy.WriteHistory" />.</summary>
    [PublicApi]
    public enum WriteEventKind
    {
        SingleRegister,

        MultipleRegisters,

        SingleCoil,

        MultipleCoils,
    }

    /// <summary>A single connect / disconnect event observed by the fake proxy.</summary>
    [PublicApi]
    public sealed record ConnectionEvent(ConnectionEventKind Kind, IPAddress? IpAddress, int? Port);

    /// <summary>A single read operation observed by the fake proxy.</summary>
    [PublicApi]
    public sealed record ReadEvent(ReadEventKind Kind, int UnitId, ushort Address, ushort Quantity);

    /// <summary>A single write operation observed by the fake proxy. <c>Bytes</c> is the raw wire-format payload (MSB-first per register).</summary>
    [PublicApi]
    public sealed record WriteEvent(WriteEventKind Kind, int UnitId, ushort Address, byte[] Bytes);
}
