using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockServiceMeasuringPointHandler : IActorReceiver
    {
        private readonly DevHostEvents _devHostEvents;

        private readonly ILogger<MockServiceMeasuringPointHandler> _logger;

        private readonly Dictionary<ServiceIdentifier, Dictionary<string, object?>> _state = new();

        public MockServiceMeasuringPointHandler(ILogger<MockServiceMeasuringPointHandler> logger, DevHostEvents devHostEvents)
        {
            _logger = logger;
            _devHostEvents = devHostEvents;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case ServiceMeasuringPointValueChanged m:
                    _logger.LogInformation("Service measuring point changed: {ServiceIdentifier}.{MeasuringPoint} = {Value}",
                                           m.ServiceIdentifier,
                                           m.MeasuringPointIdentifier,
                                           m.Value);

                    // Store the value
                    if (!_state.ContainsKey(m.ServiceIdentifier))
                    {
                        _state[m.ServiceIdentifier] = new Dictionary<string, object?>();
                    }

                    _state[m.ServiceIdentifier][m.MeasuringPointIdentifier] = m.Value;

                    // Raise event for Web UI
                    _devHostEvents.RaiseServiceMeasuringPointChanged(m.ServiceIdentifier.ToString(), m.MeasuringPointIdentifier, m.Value);
                    break;

                case LinkLogicBlockServiceActors m: // Initialization
                    _logger.LogInformation("Linked {Count} service actors", m.ServiceLogicBlockActorReferences.Count);
                    break;

                case MockPublishAllStatesMessage: // From Web UI
                    PublishAllStates();
                    break;

                default:
                    _logger.LogDebug("Received message: {MessageType}", message.GetType().Name);
                    break;
            }

            return Task.CompletedTask;
        }

        private void PublishAllStates()
        {
            foreach (var (serviceIdentifier, values) in _state)
            {
                foreach (var (measuringPointIdentifier, value) in values)
                {
                    _devHostEvents.RaiseServiceMeasuringPointChanged(serviceIdentifier.ToString(), measuringPointIdentifier, value);
                }
            }
        }
    }
}
