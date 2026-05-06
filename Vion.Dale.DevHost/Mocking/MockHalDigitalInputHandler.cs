using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockHalDigitalInputHandler : IActorReceiver
    {
        private readonly DevHostEvents _devHostEvents;

        private readonly ILogger<MockHalDigitalInputHandler> _logger;

        private readonly Dictionary<ServiceProviderContractId, bool> _state = new();

        private Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> _contractLogicBlockActorReferences = new();

        public MockHalDigitalInputHandler(ILogger<MockHalDigitalInputHandler> logger, DevHostEvents devHostEvents)
        {
            _logger = logger;
            _devHostEvents = devHostEvents;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case LinkLogicBlockContractActors m: // Initialization
                    _contractLogicBlockActorReferences = m.ContractLogicBlockActorReferences;
                    _logger.LogInformation("Linked {Count} logic block contract actors", m.ContractLogicBlockActorReferences.Count);
                    break;

                case MockSetDigitalInputMessage m: // From Web UI
                    SetDigitalInput(m.ServiceProviderIdentifier, m.ServiceIdentifier, m.ContractIdentifier, m.Value, actorContext);
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

        private void SetDigitalInput(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value, IActorContext actorContext)
        {
            var serviceProviderContractId = new ServiceProviderContractId(serviceProviderIdentifier, serviceIdentifier, contractIdentifier);
            _state[serviceProviderContractId] = value;

            _logger.LogInformation("Digital input set: {ServiceProviderContractId} = {Value}", serviceProviderContractId, value);

            // Raise event for Web UI
            _devHostEvents.RaiseDigitalInputChanged(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value);

            // Send to all linked LogicBlocks
            if (_contractLogicBlockActorReferences.TryGetValue(serviceProviderContractId, out var contractMappings))
            {
                foreach (var (logicBlockContractId, logicBlockActorRef) in contractMappings)
                {
                    _logger.LogDebug("Forwarding DI change to LogicBlock contract: {LogicBlockContractId}", logicBlockContractId);
                    actorContext.SendTo(logicBlockActorRef, new ContractMessage<DigitalInputChanged>(logicBlockContractId, new DigitalInputChanged(value)));
                }
            }
        }

        private void PublishAllStates()
        {
            foreach (var (serviceProviderContractId, value) in _state)
            {
                _devHostEvents.RaiseDigitalInputChanged(serviceProviderContractId.ServiceProviderIdentifier,
                                                        serviceProviderContractId.ServiceIdentifier,
                                                        serviceProviderContractId.ContractIdentifier,
                                                        value);
            }
        }
    }
}
