using System.Threading.Tasks;
using Vion.Dale.DevHost.Web.Api.Dtos;

namespace Vion.Dale.DevHost.Web.Services
{
    public interface IDevHostStateProvider
    {
        Task<ConfigurationOutput> GetConfigurationAsync();

        Task SetDigitalInputValueAsync(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value);

        Task SetAnalogInputValueAsync(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value);

        Task SetServicePropertyValueAsync(string serviceIdentifier, string propertyIdentifier, object value);

        Task PublishAllStatesAsync();
    }
}