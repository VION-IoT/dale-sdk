using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;
using Vion.Dale.DevHost.Web.Api.Hubs;
using Vion.Dale.DevHost.Web.Api.Serialization;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.DevHost.Web.Services
{
    /// <summary>
    ///     Hosted service that runs an ASP.NET Core web server to serve the DevHost web UI and API.
    /// </summary>
    public class WebHostService : IHostedService
    {
        private readonly DevBlockCatalog _blockCatalog;

        private readonly WebHostConfiguration _config;

        private readonly IDevHostControl _control;

        private readonly DevConfiguration _devConfiguration;

        private readonly DevHostEvents _devHostEvents;

        private WebApplication? _app;

        public WebHostService(WebHostConfiguration config, DevConfiguration devConfiguration, DevHostEvents devHostEvents, IDevHostControl control, DevBlockCatalog blockCatalog)
        {
            _config = config;
            _devConfiguration = devConfiguration;
            _devHostEvents = devHostEvents;
            _control = control;
            _blockCatalog = blockCatalog;
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

                                       // Emit TimeSpan as ISO-8601 duration ("PT5S"), matching the codec/MQTT wire
                                       // form, not the .NET ToString form System.Text.Json defaults to.
                                       options.JsonSerializerOptions.Converters.Add(new Iso8601TimeSpanConverter());
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

                                        // ISO-8601 duration on the SignalR stream too (spec §5.4.1 rich-types wire form).
                                        opts.PayloadSerializerOptions.Converters.Add(new Iso8601TimeSpanConverter());
                                    });
            builder.Services.AddCors(options => { options.AddDefaultPolicy(policy => { policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader(); }); });

            // Register DevHost services as singletons in the WebApplication
            builder.Services.AddSingleton(_devConfiguration);
            builder.Services.AddSingleton(_devHostEvents);
            builder.Services.AddSingleton(_control);
            builder.Services.AddSingleton<DevHostEventBroadcaster>();
            builder.Services.AddSingleton(new ScenarioStore(_devConfiguration.ScenariosPath));
            builder.Services.AddSingleton<ScenarioRunRegistry>();
            builder.Services.AddSingleton(new DevTopologyStore(_devConfiguration.TopologiesPath));
            builder.Services.AddSingleton(_blockCatalog);

            _app = builder.Build();

            // IMPORTANT: Eagerly instantiate the broadcaster so it subscribes to events!
            _app.Services.GetRequiredService<DevHostEventBroadcaster>();

            // Configure middleware pipeline
            _app.UseRouting();
            _app.UseCors();

            // Origin/Host guard on mutating requests (RFC 0006 security note): the server binds loopback
            // only, but a hostile page in the developer's own browser can still fire cross-origin POSTs at
            // http://localhost:{port} — CORS does not prevent cross-origin sends. Reads stay open; mutations
            // require a loopback Host (DNS-rebinding guard) and, when a browser declares an Origin, a
            // loopback Origin. Headless local tools (curl, agents) send no Origin and pass.
            _app.Use(async (context, next) =>
                     {
                         var method = context.Request.Method;
                         var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
                         if (!safe && !IsLocalRequest(context.Request))
                         {
                             context.Response.StatusCode = StatusCodes.Status403Forbidden;
                             await context.Response.WriteAsJsonAsync(new
                                                                     {
                                                                         error =
                                                                             "cross-origin mutation rejected — the DevHost accepts state changes from localhost pages and local headless tools only",
                                                                     });
                             return;
                         }

                         await next(context);
                     });

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

            // Discovered scenario deep links (RFC 0006) — printed before the runner's readiness line so
            // both humans and agents see what's stageable on this host.
            var scenarios = _app.Services.GetRequiredService<ScenarioStore>().List();
            foreach (var scenario in scenarios)
            {
                Console.WriteLine(scenario.Error is null ? $"  scenario {scenario.Id}: http://localhost:{_config.Port}/#/scenario/{scenario.Id}" :
                                      $"  scenario {scenario.Id}: INVALID — {scenario.Error}");
            }

            return _app.StartAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app != null)
            {
                // A scenario run must not keep driving a host that is being torn down (reset recycles
                // the whole generation underneath it).
                _app.Services.GetRequiredService<ScenarioRunRegistry>().Shutdown();
                await _app.StopAsync(cancellationToken);
                await _app.DisposeAsync();
            }
        }

        // Mutations must target a loopback Host and, when the browser declares one, come from a loopback
        // Origin. An absent Origin is allowed (headless tools); "null" and non-URL Origins are not.
        private static bool IsLocalRequest(HttpRequest request)
        {
            var host = request.Host.Host;
            var hostIsLocal = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]" || host == "::1";
            if (!hostIsLocal)
            {
                return false;
            }

            var origin = request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin))
            {
                return true;
            }

            return Uri.TryCreate(origin, UriKind.Absolute, out var originUri) && originUri.IsLoopback;
        }
    }
}