using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentModbus;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    /// <summary>
    ///     A FluentModbus listener provider that binds the server socket with the address-reuse option, so a
    ///     same-version redeploy can rebind the port while the outgoing server's socket is still lingering
    ///     (TIME_WAIT) instead of failing with <c>EADDRINUSE</c>. FluentModbus's built-in
    ///     <c>DefaultTcpClientProvider</c> sets no socket options — which is exactly why the default
    ///     <see cref="ModbusTcpServer.Start(IPEndPoint)" /> is prone to the overlap conflict — so
    ///     <see cref="ModbusTcpServerProxy" /> injects this via the public
    ///     <see cref="ModbusTcpServer.Start(ITcpClientProvider, bool)" /> hook instead (RFC 0018 / DF-46,
    ///     Part B). Because the proxy passes <c>leaveOpen: false</c>, the server disposes this provider on
    ///     <c>Stop()</c>/<c>Dispose()</c> — the same teardown path as the built-in provider.
    /// </summary>
    internal sealed class ReuseAddressTcpClientProvider : ITcpClientProvider
    {
        private readonly TcpListener _listener;

        public ReuseAddressTcpClientProvider(IPEndPoint endpoint)
        {
            _listener = new TcpListener(endpoint);

            // Allow rebinding the local endpoint while a previous socket on it is still in TIME_WAIT. Must be
            // set before Start()/Bind(). ExclusiveAddressUse=false is the conventional .NET spelling and maps
            // to SO_REUSEADDR on the underlying socket.
            _listener.ExclusiveAddressUse = false;
            _listener.Start();
        }

        public Task<TcpClient> AcceptTcpClientAsync()
        {
            return _listener.AcceptTcpClientAsync();
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
