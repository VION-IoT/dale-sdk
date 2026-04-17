using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost.Mocking
{
    public class MockHalDigitalOutputHandler : IActorReceiver
    {
        private readonly DevHostEvents _devHostEvents;

        private readonly ILogger<MockHalDigitalOutputHandler> _logger;

        private readonly Dictionary<ServiceProviderContractId, bool> _state = new();

        private Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> _contractLogicBlockActorReferences = new();

        public MockHalDigitalOutputHandler(ILogger<MockHalDigitalOutputHandler> logger, DevHostEvents devHostEvents)
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

                case ContractMessage<SetDigitalOutput> m: // From LogicBlock
                    SetDigitalOutput(m.LogicBlockContractId, m.Data.Value, actorContext);
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

        private void SetDigitalOutput(LogicBlockContractId logicBlockContractId, bool value, IActorContext actorContext)
        {
            _logger.LogInformation("Digital output set: {LogicBlockContractId} = {Value}", logicBlockContractId, value);

            // Find the ServiceProviderContractId for this LogicBlockContractId
            foreach (var (serviceProviderContractId, contractMappings) in _contractLogicBlockActorReferences)
            {
                if (contractMappings.ContainsKey(logicBlockContractId))
                {
                    _state[serviceProviderContractId] = value;

                    // Raise event for Web UI
                    _devHostEvents.RaiseDigitalOutputChanged(serviceProviderContractId.ServiceProviderIdentifier, serviceProviderContractId.ServiceIdentifier, serviceProviderContractId.ContractIdentifier, value);

                    // Send state change back to LogicBlock
                    foreach (var (mappedContractId, logicBlockActorRef) in contractMappings)
                    {
                        actorContext.SendTo(logicBlockActorRef, new ContractMessage<DigitalOutputChanged>(mappedContractId, new DigitalOutputChanged(value)));
                    }

                    break;
                }
            }
        }

        private void PublishAllStates()
        {
            foreach (var (serviceProviderContractId, value) in _state)
            {
                _devHostEvents.RaiseDigitalOutputChanged(serviceProviderContractId.ServiceProviderIdentifier, serviceProviderContractId.ServiceIdentifier, serviceProviderContractId.ContractIdentifier, value);
            }
        }
    }
}
