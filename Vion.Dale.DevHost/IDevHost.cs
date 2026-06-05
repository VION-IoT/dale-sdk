using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost
{
    public interface IDevHost : IAsyncDisposable
    {
        /// <summary>
        ///     Headless, scriptable control surface for the running network (CI / tests / agents).
        ///     Available after <see cref="StartAsync" />. See RFC 0003.
        /// </summary>
        IDevHostControl Control { get; }

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