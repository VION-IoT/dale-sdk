namespace Vion.Dale.Sdk.Configuration.Interfaces
{
    /// <summary>
    ///     Receives a command message from another logic block function.
    /// </summary>
    public interface IHandleCommand<in TCommandMessage>
    {
        /// <summary>
        ///     Receives a command message from another logic block function
        /// </summary>
        void HandleCommand(TCommandMessage command);
    }
}