using System;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;

// ReSharper disable MemberCanBePrivate.Global

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces.V1
{
    public static class EnergyBufferControl
    {
        /// <summary>
        ///     Request-Response interface between controller (EnergyManager) and Buffer
        /// </summary>

        #region ObservableEnergyBuffer/Controller

        // request response (request)
        public readonly record struct ObservableBufferDataRequest;

        // request response (response)
        public readonly record struct ObservableBufferDataResponse(
            double CurrentL1,
            double CurrentL2,
            double CurrentL3,
            double EnergyChargingTotal, // meter value, increasing, maybe aggregated from active power, differences used for energy flow calculation
            double EnergyDischargingTotal, // meter value, increasing, maybe aggregated from active power, differences used for energy flow calculation
            double ActivePowerTotal // maybe not strictly needed if flow calculation is done with Energy, still could be useful
        );

        /// <summary>
        ///     Represents a controllable energy Buffer that can be observed by the EnergyManager
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableBufferController))]
        public interface IObservableBuffer
        {
            // empty, not requesting/commanding anything, only receiving/responding via implementation
        }

        /// <summary>
        ///     Buffers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IObservableBufferImplementation : IHandleRequest<ObservableBufferDataRequest, ObservableBufferDataResponse>
        {
        }

        /// <summary>
        ///     Represents a controller for EnergyManager that can send requests to the Buffer.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableBuffer))]
        public interface IObservableBufferController : ILogicSenderInterface, ISendRequest<ObservableBufferDataRequest>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable Buffer
        /// </summary>
        public interface IObservableBufferControllerImplementation : IHandleResponse<ObservableBufferDataResponse>
        {
        }

        #endregion

        /// <summary>
        ///     Additional interfaces for controllable energy buffers
        ///     Interfaces derive from the Observable interfaces above
        /// </summary>

        #region ControllableEnergyBuffer/Controller

        // state update from Buffer to EnergyManager
        public readonly record struct ControllableBufferConfigurationStateUpdate(
            int ChargingOperatingMode, // should be enum, the ems mode including peak shaving, off-grid, etc.
            int DischargingOperatingMode, // should be enum, the ems mode including peak shaving, off-grid, etc.
            int PrioritySelfConsumptionCharging,
            int PriorityGridFeedOptimizedCharging,
            double NetBatteryCapacity, // netto, in kWh
            TimeSpan ResponseTime, // time allowed for the inverter to ramp up
            double StandbyThresholdPower, // minimum current in A
            bool UnbalancedLoadCompensation, // flag
            bool ChargingFromGrid, // flag
            bool ChargingWhileHighRateTariff // flag
        );

        // state update from Buffer to EnergyManager
        public readonly record struct ControllableBufferDataStateUpdate(
            bool Active, // 
            double StageOfCharge, // SoC
            double MaximumChargingCurrent, // dynamic based on SoC/Capacity
            double MaximumDischargingCurrent //dynamic based on SoC/Capacity
        );

        // command from EnergyManager to Buffer
        public readonly record struct ControllableBufferCommand(
            double AllocatedCurrentL1, // sent from ems to Buffer, extension to allow commanding by phase based on BufferPhaseType
            double AllocatedCurrentL2,
            double AllocatedCurrentL3

            // reactive allocation??
        );

        // state update from Buffer to EnergyManager
        public readonly record struct BufferOffGridState(
            bool OffGridConditionActive // forwarded from ns protection, but it could be implemented somewhere else
        );

        /// <summary>
        ///     Represents a controllable energy Buffer that can be observed by the EnergyManager.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableBufferController))]
        public interface IControllableBuffer : IObservableBuffer,
                                               ISendStateUpdate<ControllableBufferConfigurationStateUpdate>,
                                               ISendStateUpdate<ControllableBufferDataStateUpdate>,
                                               ISendStateUpdate<BufferOffGridState>
        {
        }

        /// <summary>
        ///     Buffers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IControllableBufferImplementation : IObservableBufferImplementation, IHandleCommand<ControllableBufferCommand>
        {
        }

        /// <summary>
        ///     Represents a controller for an observable energy Buffer that can send requests to the Buffer.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableBuffer))]
        public interface IControllableBufferController : IObservableBufferController, ISendCommand<ControllableBufferCommand>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable Buffer
        /// </summary>
        public interface IControllableBufferControllerImplementation : IObservableBufferControllerImplementation,
                                                                       IHandleStateUpdate<ControllableBufferConfigurationStateUpdate>,
                                                                       IHandleStateUpdate<ControllableBufferDataStateUpdate>,
                                                                       IHandleStateUpdate<BufferOffGridState>

        {
        }

        #endregion
    }
}