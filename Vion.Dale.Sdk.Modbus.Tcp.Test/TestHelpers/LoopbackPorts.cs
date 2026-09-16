using System.Net;
using System.Net.Sockets;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.TestHelpers
{
    /// <summary>
    ///     The ephemeral loopback port every suite in this project that binds a real socket takes. Asking the
    ///     operating system rather than picking a number is what lets these suites run beside each other and
    ///     beside anything else on the machine; the `modbus-smoke` skill's host is the exception, because it
    ///     binds a fixed port.
    /// </summary>
    public static class LoopbackPorts
    {
        /// <summary>
        ///     A port nothing was listening on when it was asked for. The listener that reserved it is stopped
        ///     before the value is returned, so the caller binds it itself.
        /// </summary>
        public static int Free()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}