using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost
{
    public interface IDevHost
    {
        /// <summary>
        ///     Starts the development host
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Runs the development host until cancellation is requested
        /// </summary>
        Task RunAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Stops the development host
        /// </summary>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}