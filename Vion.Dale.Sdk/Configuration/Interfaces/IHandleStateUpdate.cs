using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Receives a state update message from another logic block function to handle.
    /// </summary>
    public interface IHandleStateUpdate<in TStateUpdateMessage>
    {
        /// <summary>
        ///     Receives a state update message from another logic block function to handle.
        /// </summary>
        void HandleStateUpdate(InterfaceId functionId, TStateUpdateMessage response);
    }
}