using System;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Extension methods for setting up logic block HTTP client services in an <see cref="IServiceCollection" />.
    /// </summary>
    [PublicApi]
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///     The bound every request inherits when no caller sets one of its own.
        /// </summary>
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     The timeout an <see cref="HttpClient" /> starts life with. Ours is applied only while the
        ///     client still carries it, so a value an earlier registration's <c>configureClient</c> chose is
        ///     not overwritten by a later registration; the platform announces this number in its
        ///     documentation and nowhere a caller can read it.
        /// </summary>
        private static readonly TimeSpan HttpClientDefaultTimeout = TimeSpan.FromSeconds(100);

        /// <summary>
        ///     Adds HTTP services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <param name="configureClient">Action to configure additional settings and/or override defaults.</param>
        /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
        /// <remarks>
        ///     JSON serialization uses <see cref="JsonSerializerOptions" /> configured via
        ///     <c>services.Configure&lt;JsonSerializerOptions&gt;(...)</c>.
        ///     If not configured, default System.Text.Json settings are used.
        /// </remarks>
        public static IServiceCollection AddDaleHttpSdk(this IServiceCollection serviceCollection, Action<HttpClient>? configureClient = null)
        {
            serviceCollection.AddHttpClient(HttpRequestExecutor.HttpClientName,
                                            client =>
                                            {
                                                // Each default is applied only where the client still lacks it.
                                                // AddHttpClient keeps one configure action per call and runs them all
                                                // against the same client, so a plugin composed from two libraries
                                                // that each register the SDK runs this twice: applying again would
                                                // send the User-Agent twice, and applying again unconditionally would
                                                // discard whatever the first registration's configureClient had set.
                                                if (client.DefaultRequestHeaders.UserAgent.Count == 0)
                                                {
                                                    client.DefaultRequestHeaders.Add("User-Agent", "Vion-DALE (info@vion-iot.com)");
                                                }

                                                if (client.Timeout == HttpClientDefaultTimeout)
                                                {
                                                    client.Timeout = DefaultTimeout;
                                                }

                                                // Allow user to override/extend
                                                configureClient?.Invoke(client);
                                            });
            serviceCollection.AddTransient<IHttpRequestExecutor, HttpRequestExecutor>();
            serviceCollection.AddTransient<IHttpContentSerializer, HttpContentSerializer>();
            serviceCollection.AddTransient<ILogicBlockHttpClient, LogicBlockHttpClient>();

            return serviceCollection;
        }
    }
}