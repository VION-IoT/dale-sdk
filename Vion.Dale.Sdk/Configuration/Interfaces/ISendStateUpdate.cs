namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Sends a state update message to all linked logic blocks.
    /// </summary>
    public interface ISendStateUpdate<in TStateUpdateMessage>
    {
        /// <summary>
        ///     Sends a state update message to all linked logic blocks.
        /// </summary>
        void SendStateUpdate(TStateUpdateMessage stateUpdate);
    }
}