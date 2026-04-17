using System.Linq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Core.Test
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
            serviceCollection.AddDaleModbusCoreSdk();

            // Assert
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IBitConverterProxy) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusDataConverter) && sd.Lifetime == ServiceLifetime.Transient));
            Assert.IsTrue(serviceCollection.Any(sd => sd.ServiceType == typeof(IModbusValidator) && sd.Lifetime == ServiceLifetime.Transient));
        }
    }
}