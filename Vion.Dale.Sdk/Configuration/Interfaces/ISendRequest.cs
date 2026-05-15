using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Sends a request message to a specific logic block function for responding to.
    /// </summary>
    public interface ISendRequest<in TRequestMessage>
    {
        /// <summary>
        ///     Sends a request message to a specific logic block function for responding to.
        /// </summary>
        void SendRequest(InterfaceId functionId, TRequestMessage request);
    }
}