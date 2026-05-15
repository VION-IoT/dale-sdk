using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockHalAnalogInputHandler : IActorReceiver
    {
        private readonly DevHostEvents _devHostEvents;

        private readonly ILogger<MockHalAnalogInputHandler> _logger;

        private readonly Dictionary<ServiceProviderContractId, double> _state = new();

        private Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> _contractLogicBlockActorReferences = new();

        public MockHalAnalogInputHandler(ILogger<MockHalAnalogInputHandler> logger, DevHostEvents devHostEvents)
        {
            _logger = logger;
            _devHostEvents = devHostEvents;
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            switch (message)
            {
                case LinkLogicBlockContractActors m:
                    _contractLogicBlockActorReferences = m.ContractLogicBlockActorReferences;
                    _logger.LogInformation("Linked {Count} logic block contract actors", m.ContractLogicBlockActorReferences.Count);
                    break;

                case MockSetAnalogInputMessage m: // From Web UI
                    SetAnalogInput(m.ServiceProviderIdentifier, m.ServiceIdentifier, m.ContractIdentifier, m.Value, actorContext);
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

        private void SetAnalogInput(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value, IActorContext actorContext)
        {
            var serviceProviderContractId = new ServiceProviderContractId(serviceProviderIdentifier, serviceIdentifier, contractIdentifier);
            _state[serviceProviderContractId] = value;

            _logger.LogInformation("Analog input set: {ServiceProviderContractId} = {Value}", serviceProviderContractId, value);

            // Raise event for Web UI
            _devHostEvents.RaiseAnalogInputChanged(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value);

            // Send to all linked LogicBlocks
            if (_contractLogicBlockActorReferences.TryGetValue(serviceProviderContractId, out var contractMappings))
            {
                foreach (var (logicBlockContractId, logicBlockActorRef) in contractMappings)
                {
                    _logger.LogDebug("Forwarding AI change to LogicBlock contract: {LogicBlockContractId}", logicBlockContractId);
                    actorContext.SendTo(logicBlockActorRef, new ContractMessage<AnalogInputChanged>(logicBlockContractId, new AnalogInputChanged(value)));
                }
            }
        }

        private void PublishAllStates()
        {
            foreach (var (serviceProviderContractId, value) in _state)
            {
                _devHostEvents.RaiseAnalogInputChanged(serviceProviderContractId.ServiceProviderIdentifier,
                                                       serviceProviderContractId.ServiceIdentifier,
                                                       serviceProviderContractId.ContractIdentifier,
                                                       value);
            }
        }
    }
}
