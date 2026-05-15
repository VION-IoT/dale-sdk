using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Sends a command message to a specific logic block function.
    /// </summary>
    public interface ISendCommand<in TCommandMessage>
    {
        /// <summary>
        ///     Sends a command message to a specific logic block function
        /// </summary>
        void SendCommand(InterfaceId functionId, TCommandMessage command);
    }
}