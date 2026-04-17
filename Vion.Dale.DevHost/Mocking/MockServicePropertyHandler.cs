using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockServicePropertyHandler : IActorReceiver
    {
        private readonly DevHostEvents _devHostEvents;

        private readonly ILogger<MockServicePropertyHandler> _logger;

        private readonly Dictionary<ServiceIdentifier, Dictionary<string, object?>> _state = new();

        public MockServicePropertyHandler(ILogger<MockServicePropertyHandler> logger, DevHostEvents devHostEvents)
        {
            _logger = logger;
            _devHostEvents = devHostEvents;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case ServicePropertyValueChanged m:
                    _logger.LogInformation("Service property changed: {ServiceIdentifier}.{Property} = {Value}", m.ServiceIdentifier, m.PropertyIdentifier, m.Value);

                    // Store the value
                    if (!_state.ContainsKey(m.ServiceIdentifier))
                    {
                        _state[m.ServiceIdentifier] = new Dictionary<string, object?>();
                    }

                    _state[m.ServiceIdentifier][m.PropertyIdentifier] = m.Value;

                    // Raise event for Web UI
                    _devHostEvents.RaiseServicePropertyChanged(m.ServiceIdentifier.ToString(), m.PropertyIdentifier, m.Value);
                    break;

                case MockSetServicePropertyValue m:
                    HandleSetPropertyRequest(actorContext, m);
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

        private void HandleSetPropertyRequest(IActorContext actorContext, MockSetServicePropertyValue message)
        {
            _logger.LogInformation("Set property request: {ServiceIdentifier}.{Property} = {Value}",
                                   message.Request.ServiceIdentifier,
                                   message.Request.PropertyIdentifier,
                                   message.Request.Value);
            actorContext.SendTo(message.LogicBlock, message.Request);
        }

        private void PublishAllStates()
        {
            foreach (var (serviceIdentifier, values) in _state)
            {
                foreach (var (propertyIdentifier, value) in values)
                {
                    _devHostEvents.RaiseServicePropertyChanged(serviceIdentifier.ToString(), propertyIdentifier, value);
                }
            }
        }
    }
}