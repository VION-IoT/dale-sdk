using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentModbus;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    /// <summary>
    ///     The FluentModbus listener provider the server binds through, so the SDK and not the library decides how the
    ///     listening socket is bound. <see cref="ModbusTcpServerProxy" /> injects it through the public
    ///     <see cref="ModbusTcpServer.Start(ITcpClientProvider, bool)" /> hook, with <c>leaveOpen</c> left false, so the
    ///     server disposes it on <c>Stop()</c> — the same teardown path as the built-in provider.
    /// </summary>
    internal sealed class ReuseAddressTcpClientProvider : ITcpClientProvider
    {
        private readonly TcpListener _listener;

        public ReuseAddressTcpClientProvider(IPEndPoint endpoint)
        {
            // No address-reuse option is set, and none may be. Bound plainly, the listener rebinds a port whose closed
            // connections still linger in TIME_WAIT, as a same-version redeploy's new server needs, and is refused a port
            // another listener holds. Windows allows the first by default, and .NET sets SO_REUSEADDR on every TCP bind on
            // Linux. ExclusiveAddressUse=false and ReuseAddress=true both add SO_REUSEPORT on Linux, which lets a second
            // server share a held port and split its masters' connections with the first.
            _listener = new TcpListener(endpoint);
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