using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vion.Dale.DevHost.Web.Api.Hubs;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.DevHost.Web.Services
{
    /// <summary>
    ///     Hosted service that runs an ASP.NET Core web server to serve the DevHost web UI and API.
    /// </summary>
    public class WebHostService : IHostedService
    {
        private readonly WebHostConfiguration _config;

        private readonly DevConfiguration _devConfiguration;

        private readonly DevHostEvents _devHostEvents;

        private readonly IDevHostStateProvider _stateProvider;

        private WebApplication? _app;

        public WebHostService(WebHostConfiguration config, DevConfiguration devConfiguration, IDevHostStateProvider stateProvider, DevHostEvents devHostEvents)
        {
            _config = config;
            _devConfiguration = devConfiguration;
            _stateProvider = stateProvider;
            _devHostEvents = devHostEvents;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel to listen on specified port
            builder.WebHost.UseKestrel(options => { options.ListenLocalhost(_config.Port); });

            // Register ASP.NET Core services with application parts
            builder.Services
                   .AddControllers()
                   .AddApplicationPart(Assembly.GetExecutingAssembly())
                   .AddControllersAsServices()
                   .AddJsonOptions(options =>
                                   {
                                       options.JsonSerializerOptions.PropertyNamingPolicy = JsonSerialization.DefaultOptions.PropertyNamingPolicy;
                                       options.JsonSerializerOptions.DictionaryKeyPolicy = JsonSerialization.DefaultOptions.DictionaryKeyPolicy;
                                       foreach (var converter in JsonSerialization.DefaultOptions.Converters)
                                       {
                                           options.JsonSerializerOptions.Converters.Add(converter);
                                       }
                                   });
            ;

            // SignalR maintains its own JSON serializer; without explicit config it would emit enums as integers,
            // breaking the rich-types contract that enums travel as member-name strings on the wire (spec §5.4.1).
            builder.Services
                   .AddSignalR()
                   .AddJsonProtocol(opts =>
                                    {
                                        opts.PayloadSerializerOptions.PropertyNamingPolicy = JsonSerialization.DefaultOptions.PropertyNamingPolicy;
                                        foreach (var converter in JsonSerialization.DefaultOptions.Converters)
                                        {
                                            opts.PayloadSerializerOptions.Converters.Add(converter);
                                        }
                                    });
            builder.Services.AddCors(options => { options.AddDefaultPolicy(policy => { policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader(); }); });

            // Register DevHost services as singletons in the WebApplication
            builder.Services.AddSingleton(_devConfiguration);
            builder.Services.AddSingleton(_stateProvider);
            builder.Services.AddSingleton(_devHostEvents);
            builder.Services.AddSingleton<DevHostEventBroadcaster>();

            _app = builder.Build();

            // IMPORTANT: Eagerly instantiate the broadcaster so it subscribes to events!
            _app.Services.GetRequiredService<DevHostEventBroadcaster>();

            // Configure middleware pipeline
            _app.UseRouting();
            _app.UseCors();

            // Map endpoints
            _app.MapControllers();
            _app.MapHub<DevHostHub>("/hub");

            // Serve embedded SPA
            var assembly = typeof(DevHostBuilderExtensions).Assembly;
            var embeddedProvider = new EmbeddedFileProvider(assembly, "Vion.Dale.DevHost.Web.wwwroot");

            _app.UseDefaultFiles(new DefaultFilesOptions
                                 {
                                     FileProvider = embeddedProvider,
                                 });

            _app.UseStaticFiles(new StaticFileOptions
                                {
                                    FileProvider = embeddedProvider,
                                });

            _app.MapFallbackToFile("index.html",
                                   new StaticFileOptions
                                   {
                                       FileProvider = embeddedProvider,
                                   });

            Console.WriteLine($"DevHost Web UI running at http://localhost:{_config.Port}");

            return _app.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                await _app.StopAsync(cancellationToken);
                await _app.DisposeAsync();
            }
        }
    }
}
