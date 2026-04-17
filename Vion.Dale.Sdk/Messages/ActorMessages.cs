using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Utils;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Messages
{
    /// <summary>
    ///     Links the runtime actors to the logic block actors
    /// </summary>
    public readonly record struct LinkRuntimeActors
    {
        public IActorReference ServicePropertyHandlerActor { get; init; }

        public IActorReference ServiceMeasuringPointHandlerActor { get; init; }

        public IActorReference PersistenceManagerActor { get; init; }
    }

    /// <summary>
    ///     Links LogicBlock actors to a service handler
    ///     Key: ServiceIdentifier
    ///     Value: IActorReference to the LogicBlock actor that contains the service
    /// </summary>
    public readonly record struct LinkLogicBlockServiceActors(Dictionary<ServiceIdentifier, IActorReference> ServiceLogicBlockActorReferences);

    /// <summary>
    ///     Sets some configuration data on the LogicBlock
    ///     ServiceIdLookup: Key = ServiceIdentifier, Value = ServiceIdentifier
    ///     LogicBlockContractIdLookup: Key = ContractIdentifier, Value = LogicBlockContractId
    /// </summary>
    public readonly record struct InitializeLogicBlock(
        string LogicBlockId,
        string LogicBlockName,
        Dictionary<string, ServiceIdentifier> ServiceIdLookup,
        Dictionary<string, LogicBlockContractId> LogicBlockContractIdLookup,
        IServiceProvider ServiceProvider);

    /// <summary>
    ///     Links LogicBlock actors to a remote function interface proxy handler
    ///     Key: LogicBlock id
    ///     Value: IActorReference to the LogicBlock actor that contains the interface
    /// </summary>
    public readonly record struct LinkLogicBlockInterfaceActors(Dictionary<InterfaceId, IActorReference> InterfaceLogicBlockActorReferences);

    /// <summary>
    ///     Keys: InterfaceId of the remote function interface
    ///     Values: Remote topics used in the MQTT topics to address the remote function interface
    /// </summary>
    public readonly record struct SetRemoteFunctionInterfaceInstallationTopics(Dictionary<InterfaceId, string> RemoteInstallationTopics);

    /// <summary>
    ///     Keys: InterfaceId of the interface of the LogicBlock that is being initialized
    ///     Values: Dictionary where Key = InterfaceId of the linked interface, Value = IActorReference to the actor that
    ///     contains the linked interface
    /// </summary>
    public readonly record struct SetLinkedInterfaces(Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>> LinkedInterfaceIds);

    /// <summary>
    ///     Message from runtime to LogicBlock to start the LogicBlock
    /// </summary>
    public readonly record struct StartLogicBlockRequest;

    /// <summary>
    ///     Message from LogicBlock to runtime confirming that the LogicBlock has started
    /// </summary>
    public readonly record struct StartLogicBlockResponse;

    /// <summary>
    ///     Message from runtime to LogicBlock to stop the LogicBlock
    /// </summary>
    public readonly record struct StopLogicBlockRequest;

    /// <summary>
    ///     Message LogicBlock to runtime confirming that the LogicBlock has stopped
    /// </summary>
    public readonly record struct StopLogicBlockResponse;

    /// <summary>
    ///     Message from runtime to LogicBlock to publish the current service state
    /// </summary>
    public readonly record struct PublishServiceState;

    /// <summary>
    ///     Message from runtime to LogicBlock to restore persistent data
    /// </summary>
    public readonly record struct RestorePersistentDataRequest(List<PersistentDataEntry> PersistentDataValues);

    /// <summary>
    ///     Message from LogicBlock to runtime confirming that the persistent data was restored
    /// </summary>
    public readonly record struct RestorePersistentDataResponse;

    /// <summary>
    ///     Message from PersistentDataHandler to LogicBlock to get the current persistent data values
    /// </summary>
    public readonly record struct GetPersistentDataSnapshotRequest;

    /// <summary>
    ///     Message from LogicBlock to runtime containing persistent data
    /// </summary>
    public readonly record struct GetPersistentDataSnapshotResponse(LogicBlockId LogicBlockId, List<PersistentDataEntry> PersistentDataValues);

    /// <summary>
    ///     Message from LogicBlock to runtime containing persistent data snapshot to save
    /// </summary>
    public readonly record struct PersistentDataSnapshotChanged(LogicBlockId LogicBlockId, List<PersistentDataEntry> PersistentDataValues);

    /// <summary>
    ///     Message from ServicePropertyHandler to LogicBlock
    /// </summary>
    public readonly record struct SetServicePropertyValueRequest(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier, object Value);

    /// <summary>
    ///     Message from LogicBlock to ServicePropertyHandler
    /// </summary>
    public readonly record struct SetServicePropertyValueResponse(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier, object? Value);

    /// <summary>
    ///     Message from ServicePropertyHandler to LogicBlock
    /// </summary>
    public readonly record struct GetServicePropertyValueRequest(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier);

    /// <summary>
    ///     Message from LogicBlock to ServicePropertyHandler
    /// </summary>
    public readonly record struct GetServicePropertyValueResponse(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier, object? Value);

    /// <summary>
    ///     Message from LogicBlock to ServicePropertyHandler
    /// </summary>
    public readonly record struct ServicePropertyValueChanged(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier, object? Value);

    /// <summary>
    ///     Message from LogicBlock to ServicePropertyHandler to clear the retained value
    /// </summary>
    public readonly record struct ServicePropertyValueCleared(ServiceIdentifier ServiceIdentifier, string PropertyIdentifier);

    /// <summary>
    ///     Message from ServiceMeasuringPointHandler to LogicBlock
    /// </summary>
    public readonly record struct GetServiceMeasuringPointValueRequest(ServiceIdentifier ServiceIdentifier, string MeasuringPointIdentifier);

    /// <summary>
    ///     Message from LogicBlock to ServiceMeasuringPointHandler
    /// </summary>
    public readonly record struct GetServiceMeasuringPointValueResponse(ServiceIdentifier ServiceIdentifier, string MeasuringPointIdentifier, object? Value);

    /// <summary>
    ///     Message from LogicBlock to ServiceMeasuringPointHandler
    /// </summary>
    public readonly record struct ServiceMeasuringPointValueChanged(ServiceIdentifier ServiceIdentifier, string MeasuringPointIdentifier, object? Value);

    /// <summary>
    ///     Message from LogicBlock to ServiceMeasuringPointHandler to clear the retained value
    /// </summary>
    public readonly record struct ServiceMeasuringPointValueCleared(ServiceIdentifier ServiceIdentifier, string MeasuringPointIdentifier);

    /// <summary>
    ///     Message between LogicBlocks for function interface calls
    /// </summary>
    public record struct FunctionInterfaceMessage<T>(InterfaceId FromId, InterfaceId ToId, T Data) : IFunctionInterfaceMessage
        where T : struct;

    public interface IFunctionInterfaceMessage
    {
        InterfaceId FromId { get; }

        InterfaceId ToId { get; }
    }

    /// <summary>
    ///     Links logic block contract actors with service provider handler actors.
    ///     Key: ServiceProviderContractId (identifies the service provider contract)
    ///     Value: Dictionary of LogicBlockContractId -> IActorReference to the LogicBlock actor that contains the contract
    /// </summary>
    public readonly record struct LinkLogicBlockContractActors(
        Dictionary<ServiceProviderContractId, Dictionary<LogicBlockContractId, IActorReference>> ContractLogicBlockActorReferences);

    /// <summary>
    ///     Message between contract handlers and LogicBlocks for contract state changes.
    /// </summary>
    public readonly record struct ContractMessage<T>(LogicBlockContractId LogicBlockContractId, T Data) : IContractMessage;

    public interface IContractMessage
    {
        LogicBlockContractId LogicBlockContractId { get; }
    }
}