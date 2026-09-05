using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server
{
    /// <summary>
    ///     The address-reuse listener provider the server binds through (docs/specs/modbus.md). The
    ///     end-to-end serve/restart behaviour is covered by <see cref="ModbusTcpServerIntegrationShould" />
    ///     (every test there enables the server, which binds through this provider); this pins the provider in
    ///     isolation — it binds the endpoint and accepts a connection.
    /// </summary>
    [TestClass]
    public class ReuseAddressTcpClientProviderShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-MODB-014.4")]
        public async Task BindEndpoint_AndAcceptConnection()
        {
            // Arrange
            var port = GetFreePort();
            using var provider = new ReuseAddressTcpClientProvider(new IPEndPoint(IPAddress.Loopback, port));

            // Act
            var acceptTask = provider.AcceptTcpClientAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var accepted = await acceptTask;

            // Assert
            Assert.IsTrue(accepted.Connected, "The reuse-address provider must bind the endpoint and accept a connection.");
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}