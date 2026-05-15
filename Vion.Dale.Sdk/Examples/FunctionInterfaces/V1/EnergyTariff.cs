using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;

// ReSharper disable MemberCanBePrivate.Global

namespace Vion.Dale.Sdk.Examples.FunctionInterfaces.V1
{
    public static class EnergyTariff
    {
        /// <summary>
        ///     State update interface between provider (e.g. Grid? Separate component?) and receiver (e.g. consumers, that can
        ///     react to tariff changes)
        /// </summary>

        #region EnergyTariffProvicer/Receiver

        // state update from provider to receiver
        public readonly record struct EnergyTariffStateUpdate(
            bool HighRateTariffActive // additional tariff info could be added, currently there is only the high/low tariff flag
        );

        /// <summary>
        ///     Represents a provider for tariff information
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IEnergyTariffReceiver))]
        public interface IEnergyTariffProvider : ISendStateUpdate<EnergyTariffStateUpdate>
        {
        }

        /// <summary>
        ///     Tariff providers need to implement this interface
        /// </summary>
        public interface IEnergyTariffProviderImplementation
        {
            // empty, not receiving/responding to anything, only sending
        }

        /// <summary>
        ///     Represents a receiver of for tariff information
        /// </summary>
        [LogicFunctionMatchingInterface(typeof(IEnergyTariffProvider))]
        public interface IEnergyTariffReceiver
        {
            // empty, not requesting/commanding anything, only receiving/responding via implementation
        }

        /// <summary>
        ///     Receivers need to handle the message from the provider
        /// </summary>
        public interface IEnergyTariffReceiverImplementation : IHandleStateUpdate<EnergyTariffStateUpdate>
        {
        }

        #endregion
    }
}