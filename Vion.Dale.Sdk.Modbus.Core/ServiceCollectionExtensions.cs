using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Core
{
    /// <summary>
    ///     Extension methods for setting up Modbus core services in an <see cref="IServiceCollection" />.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///     Adds Modbus core services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
        public static IServiceCollection AddDaleModbusCoreSdk(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<IBitConverterProxy, BitConverterProxy>();
            serviceCollection.AddTransient<IModbusDataConverter, ModbusDataConverter>();
            serviceCollection.AddTransient<IModbusValidator, ModbusValidator>();

            return serviceCollection;
        }
    }
}