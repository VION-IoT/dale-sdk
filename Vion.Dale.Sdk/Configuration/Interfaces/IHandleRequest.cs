namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Receives a request message from another logic block function and returns a response message.
    /// </summary>
    public interface IHandleRequest<in TRequestMessage, out TResponseMessage>
    {
        /// <summary>
        ///     Receives a request message from another logic block function and returns a response message.
        /// </summary>
        TResponseMessage HandleRequest(TRequestMessage request);
    }
}