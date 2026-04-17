using Vion.Dale.DevHost.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.DevHost.Web
{
    public static class DevHostBuilderExtensions
    {
        public static DevHostBuilder WithWebUi(this DevHostBuilder builder, int port = 5000)
        {
            builder.ConfigureServices(services =>
                                      {
                                          // Register web-specific services
                                          services.AddSingleton<IDevHostStateProvider, DevHostStateProvider>();
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