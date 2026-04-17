using Vion.Dale.DevHost.Web.Api.Dtos;
using Vion.Dale.DevHost.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Web.Api.Controllers
{
    [ApiController]
    [Route("api")]
    public class DevHostController : ControllerBase
    {
        private readonly IDevHostStateProvider _stateProvider;

        public DevHostController(IDevHostStateProvider stateProvider)
        {
            _stateProvider = stateProvider;
        }

        [HttpGet("configuration")]
        public async Task<ActionResult<ConfigurationOutput>> GetConfiguration()
        {
            var config = await _stateProvider.GetConfigurationAsync();
            return Ok(config);
        }

        [HttpPost("hal/di/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetDigitalInputValue(string serviceProviderIdentifier,
                                                             string serviceIdentifier,
                                                             string contractIdentifier,
                                                             [FromBody] SetValueInput<bool> input)
        {
            await _stateProvider.SetDigitalInputValueAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        [HttpPost("hal/ai/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]
        public async Task<ActionResult> SetAnalogInputValue(string serviceProviderIdentifier,
                                                            string serviceIdentifier,
                                                            string contractIdentifier,
                                                            [FromBody] SetValueInput<double> input)
        {
            await _stateProvider.SetAnalogInputValueAsync(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, input.Value);
            return Ok();
        }

        [HttpPost("dale/property/{serviceIdentifier}/{propertyIdentifier}")]
        public async Task<ActionResult> SetServicePropertyValue(string serviceIdentifier, string propertyIdentifier, [FromBody] SetValueInput<object> input)
        {
            await _stateProvider.SetServicePropertyValueAsync(serviceIdentifier, propertyIdentifier, input.Value);
            return Ok();
        }
    }
}