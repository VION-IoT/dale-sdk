using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;

// ReSharper disable MemberCanBePrivate.Global

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces.V1
{
    public static class EnergySupplierControl
    {
        /// <summary>
        ///     Request-Response interface between controller (EnergyManager) and supplier
        /// </summary>

        #region ObservableEnergySupplier/Controller

        // request response (request)
        public readonly record struct ObservableSupplierDataRequest;

        // request response (response)
        public readonly record struct ObservableSupplierDataResponse(
            double CurrentL1, // used by ems during response time and for unbalanced load calculation
            double CurrentL2,
            double CurrentL3,
            double EnergyTotal, // meter value, increasing, maybe aggregated from active power, differences used for energy flow calculation
            double ActivePowerTotal // maybe not strictly needed if flow calculation is done with Energy, still could be useful
        );

        /// <summary>
        ///     Represents a controllable energy Supplier that can be observed by the EnergyManager
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableSupplierController))]
        public interface IObservableSupplier
        {
            // empty, not requesting/commanding anything, only receiving/responding via implementation
        }

        /// <summary>
        ///     Suppliers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IObservableSupplierImplementation : IHandleRequest<ObservableSupplierDataRequest, ObservableSupplierDataResponse>
        {
        }

        /// <summary>
        ///     Represents a controller for an EnergyManager that can send requests to the Supplier.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IObservableSupplier))]
        public interface IObservableSupplierController : ILogicSenderInterface, ISendRequest<ObservableSupplierDataRequest>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable Supplier
        /// </summary>
        public interface IObservableSupplierControllerImplementation : IHandleResponse<ObservableSupplierDataResponse>
        {
        }

        #endregion

        /// <summary>
        ///     Additional interfaces for controllable energy Suppliers
        ///     Interfaces derive from the Observable interfaces above
        /// </summary>

        #region ControllableEnergySupplier/Controller

        // state update from Supplier to EnergyManager
        public readonly record struct ControllableSupplierConfigurationStateUpdate(
            double InstalledActivePower // config value
        );

        // command from EnergyManager to Supplier
        public readonly record struct ControllableSupplierCommand(
            double ActivePowerLimit // sent from ems to Supplier, to limit supplier
        );

        /// <summary>
        ///     Represents a controllable energy Supplier that can be observed by the EnergyManager
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableSupplierController))]
        public interface IControllableSupplier : IObservableSupplier, ISendStateUpdate<ControllableSupplierConfigurationStateUpdate>
        {
        }

        /// <summary>
        ///     Suppliers need to implement this interface to handle requests from the EnergyManager
        /// </summary>
        public interface IControllableSupplierImplementation : IObservableSupplierImplementation, IHandleCommand<ControllableSupplierCommand>
        {
        }

        /// <summary>
        ///     Represents a controller for an observable energy Supplier that can send requests to the Supplier.
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IControllableSupplier))]
        public interface IControllableSupplierController : IObservableSupplierController, ISendCommand<ControllableSupplierCommand>
        {
        }

        /// <summary>
        ///     EnergyManager needs to implement this interface to handle responses from observable Supplier
        /// </summary>
        public interface IControllableSupplierControllerImplementation : IObservableSupplierControllerImplementation,
                                                                         IHandleStateUpdate<ControllableSupplierConfigurationStateUpdate>
        {
        }

        #endregion
    }
}