using System;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;

// ReSharper disable MemberCanBePrivate.Global

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces.V1
{
    public static class EnergyConsumerControl
    {
        /// <summary>
        ///     Request-Response interface between controller (EnergyManager) and consumer
        /// </summary>

        #region ObservableEnergyConsumer/Controller

        // request response (request)
        public readonly record struct ObservableConsumerDataRequest;

        // request response (response)
        public readonly record struct ObservableConsumerDataResponse(
            double CurrentL1, // used by ems during response time and for unbalanced load calculation
            double CurrentL2,
            double CurrentL3,
            double EnergyTotal, // meter value, increasing, maybe aggregated from active power, differences used for energy flow calculation
            double ActivePowerTotal // maybe not strictly needed if flow calculation is done with Energy, still could be useful
        );

        /// <summary>
        ///     Represents a controllable energy consumer that can be observed by the EnergyManager
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableConsumerController))]
        public interface IObservableConsumer
        {
            // empty, not requesting/commanding anything, only receiving/responding via implementation
        }

        /// <summary>
        ///     Consumers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IObservableConsumerImplementation : IHandleRequest<ObservableConsumerDataRequest, ObservableConsumerDataResponse>
        {
        }

        /// <summary>
        ///     Represents a controller for an EnergyManager that can send requests to the consumer.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableConsumer))]
        public interface IObservableConsumerController : ILogicSenderInterface, ISendRequest<ObservableConsumerDataRequest>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable consumer
        /// </summary>
        public interface IObservableConsumerControllerImplementation : IHandleResponse<ObservableConsumerDataResponse>
        {
        }

        #endregion

        /// <summary>
        ///     Additional interfaces for controllable energy consumers
        ///     Interfaces derive from the Observable interfaces above
        /// </summary>

        #region ControllableEnergyConsumer/Controller

        // state update from consumer to ems
        public readonly record struct ControllableConsumerConfigurationStateUpdate(
            int OperatingMode, // should be enum, the ems mode including peak shaving, off-grid, etc.
            int Priority, // ems priority
            TimeSpan IdleTime, // Idle timeout
            TimeSpan ResponseTime, // Ramp-up timeout
            int PhaseOrder, // should be enum,
            TimeSpan MinimalControlIntervalTime,
            int ConsumerPhaseType, // should be enum, single-phase, 3-phase, switchable, ... this is an extension to the current implementation, which assumes 3-phase
            int[] CurrentAllocationStepConfiguration // []: 0A-requested, [6]: 6A-requested, [6, 12, 18]: steps: replaces stepwise-consumer and minimal current 
            // alternative for more explicit as object like this
            //private struct CurrentAllocationStepConfiguration
            //{
            //    private int AllocationType; //enum, 0: unlimited, 1: minimum, 2: steps
            //    private double MinimumCurrent; // only if 1
            //    private double[] Steps;
            //}
        );

        // state update from consumer to ems
        public readonly record struct ControllableConsumerDataStateUpdate(
            double RequestedCurrent // sent from consumer to ems when changed
        );

        // command from ems to consumer
        public readonly record struct ControllableConsumerCommand(
            double AllocatedCurrentL1, // sent from ems to consumer, extension to allow commanding by phase based on ConsumerPhaseType
            double AllocatedCurrentL2,
            double AllocatedCurrentL3);

        /// <summary>
        ///     Represents a controllable energy consumer that can be observed by the EMS (Energy Management System).
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableConsumerController))]
        public interface IControllableConsumer : IObservableConsumer,
                                                 ISendStateUpdate<ControllableConsumerConfigurationStateUpdate>,
                                                 ISendStateUpdate<ControllableConsumerDataStateUpdate>
        {
        }

        /// <summary>
        ///     Consumers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IControllableConsumerImplementation : IObservableConsumerImplementation, IHandleCommand<ControllableConsumerCommand>
        {
        }

        /// <summary>
        ///     Represents a controller for an observable energy consumer that can send requests to the consumer.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableConsumer))]
        public interface IControllableConsumerController : IObservableConsumerController, ISendCommand<ControllableConsumerCommand>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable consumer
        /// </summary>
        public interface IControllableConsumerControllerImplementation : IObservableConsumerControllerImplementation,
                                                                         IHandleStateUpdate<ControllableConsumerConfigurationStateUpdate>,
                                                                         IHandleStateUpdate<ControllableConsumerDataStateUpdate>
        {
        }

        #endregion
    }
}