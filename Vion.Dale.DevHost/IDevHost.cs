using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost
{
    public interface IDevHost : IAsyncDisposable
    {
        /// <summary>
        ///     Headless, scriptable control surface for the network (CI / tests / agents). Resolvable as soon
        ///     as the host is built: reading the configuration introspects on demand, which is what the
        ///     boot-dump-exit export path depends on. Everything that observes a RUNNING network — values, the
        ///     event stream, the message tap — is empty until <see cref="StartAsync" />.
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