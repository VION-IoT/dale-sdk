using System.Linq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test
{
    [TestClass]
    public class ServiceCollectionExtensionsShould
    {
        [TestMethod]
        public void RegisterModbusTcpRelatedServices()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();

            // Act
            serviceCollection.AddDaleModbusTcpSdk();

            // Assert
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(ILogicBlockModbusTcpClientFactory) && sd.Lifetime == ServiceLifetime.Singleton));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(ILogicBlockModbusTcpClient) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IRequestFactory) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IRequestQueue) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusTcpClientWrapper) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusTcpClientProxy) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IBitConverterProxy) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusDataConverter) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusValidator) && sd.Lifetime == ServiceLifetime.Transient));
        }
    }
}