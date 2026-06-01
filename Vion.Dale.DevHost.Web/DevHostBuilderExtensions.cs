using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.Web.Services;

namespace Vion.Dale.DevHost.Web
{
    public static class DevHostBuilderExtensions
    {
        public static DevHostBuilder WithWebUi(this DevHostBuilder builder, int port = 5000)
        {
            builder.ConfigureServices(services =>
                                      {
                                          // Register web-specific services. State/config/set all go through the
                                          // core IDevHostControl now (registered by DevHostBuilder), so there is
                                          // no web-only state provider — one abstraction, one API.
                                          services.AddSingleton<DevHostEventBroadcaster>();

                                          // Store port configuration
                                          services.AddSingleton(new WebHostConfiguration { Port = port });

                                          // Add hosted service to start web server
                                          services.AddHostedService<WebHostService>();
                                      });

            return builder;
        }
    }

    public class WebHostConfiguration
    {
        public int Port { get; set; }
    }
}
