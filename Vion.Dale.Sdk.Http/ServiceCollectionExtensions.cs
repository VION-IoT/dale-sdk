using System;
using System.Net.Http;
using System.Text.Json;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Extension methods for setting up logic block HTTP client services in an <see cref="IServiceCollection" />.
    /// </summary>
    [PublicApi]
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///     Adds HTTP services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <param name="configureClient">Action to configure additional settings and/or override defaults.</param>
        /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
        /// <remarks>
        ///     JSON serialization uses <see cref="JsonSerializerOptions" /> configured via <c>services.Configure&lt;JsonSerializerOptions&gt;(...)</c>.
        ///     If not configured, default System.Text.Json settings are used.
        /// </remarks>
        public static IServiceCollection AddDaleHttpSdk(this IServiceCollection serviceCollection, Action<HttpClient>? configureClient = null)
        {
            serviceCollection.AddHttpClient(HttpRequestExecutor.HttpClientName,
                                            client =>
                                            {
                                                // Apply default configuration
                                                client.DefaultRequestHeaders.Add("User-Agent", "Vion-DALE (info@vion-iot.com)");
                                                client.Timeout = TimeSpan.FromSeconds(30);

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