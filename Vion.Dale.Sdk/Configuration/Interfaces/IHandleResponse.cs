using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Receives a response message from another logic block function to handle.
    /// </summary>
    public interface IHandleResponse<in TResponseMessage>
    {
        /// <summary>
        ///     Receives a response message from another logic block function to handle.
        /// </summary>
        void HandleResponse(InterfaceId functionId, TResponseMessage response);
    }
}