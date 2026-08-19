using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;

namespace Vion.Examples.ModbusTcp.LogicBlocks
{
    /// <summary>
    ///     A synthetic Modbus TCP server so <see cref="ModbusTcpDebugClient" /> has something to talk to
    ///     without any hardware on the bench. The register map is documented in the example README and is
    ///     deliberately built to exercise every field type the debug client can decode — including the same
    ///     32-bit float stored twice under opposite word orders (input registers 6-7 vs 8-9), which is the
    ///     quickest way to see what a word-order mismatch does to a reading.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Listens on 15020 rather than the standard 502: an unprivileged port avoids elevation on any OS
    ///         and cannot be confused with a real device on the network.
    ///     </para>
    ///     <para>
    ///         It binds <c>127.0.0.1</c>, not the SDK server's default of <c>0.0.0.0</c>. A wildcard bind
    ///         answers on every address this machine has — including every other <c>127.0.0.x</c>, which are
    ///         all loopback — so pointing the debug client at a "wrong" address like <c>127.0.0.2</c> would
    ///         still reach this server and the link would truthfully read Online. Binding one address keeps a
    ///         wrong address wrong, and keeps a simulated device off the rest of the network. Set
    ///         <see cref="ListenAddress" /> if you want to reach it from another machine.
    ///     </para>
    /// </remarks>
    [LogicBlock(Name = "Modbus TCP Sim Server",
                Icon = "server-line",
                Groups = new[]
                         {
                             PropertyGroup.Status,
                             PropertyGroup.Configuration,
                             PropertyGroup.Diagnostics,
                         })]
    public class ModbusTcpSimServer : LogicBlockBase
    {
        private const ushort HoldingScratchStart = 0;

        private const ushort HoldingScratchCount = 10;

        private const ushort HoldingFloatAddress = 100;

        private const ushort InputEchoStart = 40;

        private readonly ILogger _logger;

        private readonly ILogicBlockModbusTcpServer _server;

        private string _listenAddress = "127.0.0.1";

        private int _listenPort = 15020;

        private bool _serverEnabled = true;

        private long _tick;

        [ServiceProperty(Title = "Server enabled", Description = "Binds the listener. Disable to free the port or to change it.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public bool ServerEnabled
        {
            get => _serverEnabled;

            set
            {
                if (_serverEnabled == value)
                {
                    return;
                }

                _serverEnabled = value;
                ApplyServerConfiguration();
            }
        }

        [ServiceProperty(Title = "Listen address",
                         StringFormat = StringFormats.Ipv4,
                         Description =
                             "The address the listener binds. 127.0.0.1 keeps the simulated device on this machine; 0.0.0.0 answers on every address it has, including every other 127.0.0.x.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public string ListenAddress
        {
            get => _listenAddress;

            set
            {
                if (_listenAddress == value)
                {
                    return;
                }

                _listenAddress = value;
                ApplyServerConfiguration();
            }
        }

        [ServiceProperty(Title = "Listen port",
                         Minimum = 1,
                         Maximum = 65535,
                         Description = "TCP port the simulated server listens on. 15020 keeps it clear of real Modbus devices on 502.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int ListenPort
        {
            get => _listenPort;

            set
            {
                if (_listenPort == value)
                {
                    return;
                }

                _listenPort = value;
                ApplyServerConfiguration();
            }
        }

        [ServiceProperty(Title = "Listening")]
        [Presentation(Group = PropertyGroup.Status, StatusIndicator = false, Importance = Importance.Primary)]
        public bool IsListening { get; private set; }

        [ServiceProperty(Title = "Connected clients")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Secondary)]
        public int ConnectionCount { get; private set; }

        [ServiceProperty(Title = "Last client write")]
        [Presentation(Group = PropertyGroup.Status, Format = Formats.Relative)]
        public DateTime? LastClientWriteAt { get; private set; }

        [ServiceProperty(Title = "Last error")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string LastError { get; private set; } = string.Empty;

        public ModbusTcpSimServer(ILogicBlockModbusTcpServer server, ILogger logger) : base(logger)
        {
            _server = server;
            _logger = logger;
        }

        /// <summary>
        ///     Advances everything that is supposed to move, then republishes the listener status. All buffer
        ///     access happens in one <c>Sync</c> so a client polling mid-tick never sees a half-updated map.
        /// </summary>
        [Timer(1)]
        public void OnTick()
        {
            _tick++;

            try
            {
                _server.Sync(snapshot =>
                             {
                                 WriteDynamicValues(snapshot);
                                 EchoHoldingScratch(snapshot);
                             });
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.LogWarning(ex, "Sim server tick failed");
            }

            RefreshServerStatus();
        }

        protected override void Ready()
        {
            SeedStaticValues();
            ApplyServerConfiguration();
        }

        protected override void Stopping()
        {
            _server.Dispose();
        }

        /// <summary>
        ///     Reconfigures and rebinds the listener. Configuration properties are only settable while the
        ///     server is disabled, so every change is a disable / apply / re-enable cycle.
        /// </summary>
        private void ApplyServerConfiguration()
        {
            try
            {
                // Both the address and the port are settable only while the listener is down, which is what
                // makes this a disable / apply / re-enable cycle rather than three assignments.
                _server.IsEnabled = false;
                _server.ListenAddress = _listenAddress;
                _server.Port = _listenPort;
                _server.IsEnabled = _serverEnabled;
                LastError = string.Empty;
            }
            catch (Exception ex)
            {
                // A bind failure (port in use) must not take the block down — surface it and stay alive so
                // the operator can pick another port from the UI.
                LastError = ex.Message;
                _logger.LogWarning(ex, "Sim server could not bind port {Port}", _listenPort);
            }

            RefreshServerStatus();
        }

        private void RefreshServerStatus()
        {
            IsListening = _server.IsListening;
            ConnectionCount = _server.ConnectionCount;
            LastClientWriteAt = _server.LastClientWriteAt?.UtcDateTime;
        }

        /// <summary>
        ///     Seeds the parts of the map that never change. Runs before the listener binds — the register
        ///     buffers exist independently of it.
        /// </summary>
        private void SeedStaticValues()
        {
            _server.HoldingRegisterCount = 128;
            _server.InputRegisterCount = 128;
            _server.CoilCount = 32;
            _server.DiscreteInputCount = 32;

            _server.Sync(snapshot =>
                         {
                             var input = snapshot.InputRegisters;
                             input.WriteAsShort(1, -1234);
                             input.WriteAsInt(4, -123456);
                             input.WriteAsFloat(6, (float)Math.PI);

                             // Same value, opposite word order: reading 8-9 with the default MswToLsw yields
                             // nonsense, which is exactly the symptom this example teaches you to recognise.
                             input.WriteAsFloat(8, (float)Math.PI, wordOrder: WordOrder32.LswToMsw);
                             input.WriteAsDouble(16, Math.E);
                             input.WriteAsString(20, "DALE-SIM-SERVER!");

                             snapshot.HoldingRegisters.WriteAsFloat(HoldingFloatAddress, (float)Math.PI);

                             for (ushort coil = 8; coil < 16; coil++)
                             {
                                 snapshot.Coils.Write(coil, coil % 2 == 0);
                             }
                         });
        }

        private void WriteDynamicValues(IModbusServerSnapshot snapshot)
        {
            var input = snapshot.InputRegisters;
            input.WriteAsUShort(0, (ushort)(_tick % ushort.MaxValue));
            input.WriteAsUInt(2, (uint)(_tick * 10));
            input.WriteAsFloat(10, (float)(Math.Sin(_tick * 2 * Math.PI / 60) * 100));
            input.WriteAsULong(12, (ulong)(_tick * 1000));

            for (ushort coil = 0; coil < 8; coil++)
            {
                snapshot.Coils.Write(coil, (_tick >> coil) % 2 == 1);
            }

            var walkingBit = (ushort)(_tick % 16);

            for (ushort bit = 0; bit < 16; bit++)
            {
                snapshot.DiscreteInputs.Write(bit, bit == walkingBit);
            }
        }

        /// <summary>
        ///     Mirrors the client-writable scratch registers into the read-only input area, so a write can be
        ///     confirmed from a register the client cannot have written itself.
        /// </summary>
        private static void EchoHoldingScratch(IModbusServerSnapshot snapshot)
        {
            var scratch = snapshot.HoldingRegisters.ReadRaw(HoldingScratchStart, HoldingScratchCount);
            snapshot.InputRegisters.WriteRaw(InputEchoStart, scratch);
        }
    }
}