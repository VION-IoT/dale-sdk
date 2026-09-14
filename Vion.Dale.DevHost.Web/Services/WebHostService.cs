using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        private readonly DevHostBudgets _budgets;

        private readonly WebHostConfiguration _config;

        private readonly IDevHostControl _control;

        private readonly DevConfiguration _devConfiguration;

        private readonly DevHostEvents _devHostEvents;

        // The running host's introspection — handed to the topology store so an editor Save / validate applies
        // the wire-type identity rule, which needs introspected blocks and so cannot live in the
        // host-independent DevTopologyLoader.Build.
        private readonly DevHostIntrospection _introspection;

        // How far a host walks from its preferred port: the preferred port and the nineteen above it.
        private const int PortWalk = 20;

        private WebApplication? _app;

        public WebHostService(WebHostConfiguration config,
                              DevConfiguration devConfiguration,
                              DevHostEvents devHostEvents,
                              IDevHostControl control,
                              DevBlockCatalog blockCatalog,
                              DevHostIntrospection introspection,
                              DevHostBudgets budgets)
        {
            _budgets = budgets;
            _config = config;
            _devConfiguration = devConfiguration;
            _devHostEvents = devHostEvents;
            _control = control;
            _blockCatalog = blockCatalog;
            _introspection = introspection;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var binding = WebUiBinding.For(_control);

            // A pinned port is the one an earlier generation served on; the page and any waiting client address it,
            // so a later generation takes that port or nothing. Otherwise the configured port is only preferred.
            var first = binding.PinnedPort ?? _config.Port;
            var last = binding.PinnedPort ?? Math.Min(first + PortWalk - 1, IPEndPoint.MaxPort);

            for (var port = first;; port++)
            {
                var app = BuildApplication(port);
                try
                {
                    // The real bind, never a probe: Kestrel binds both loopback families, and a port held on only
                    // one of them looks free to a probe on the other.
                    await app.StartAsync(cancellationToken);
                    _app = app;
                    binding.BoundPort = port;
                    break;
                }
                catch (IOException exception)
                {
                    await app.DisposeAsync();
                    if (port < last)
                    {
                        Console.WriteLine($"Port {port} is in use — trying {port + 1}.");
                        continue;
                    }

                    throw binding.PinnedPort is null ?
                              new InvalidOperationException($"The development host could not bind any port from {first} to {last}: {exception.Message} " +
                                                            "Every one of them is held by another process - stop one of them.",
                                                            exception) :
                              new InvalidOperationException($"The development host could not rebind port {port}, which it served on before the recycle: {exception.Message} " +
                                                            "Another process took it while the host recycled - restart the host.",
                                                            exception);
                }
            }

            Console.WriteLine($"DevHost Web UI running at http://localhost:{binding.BoundPort}");

            // Discovered scenario deep links — printed before the runner's readiness line so
            // both humans and agents see what's stageable on this host.
            var scenarios = _app.Services.GetRequiredService<ScenarioStore>().List();
            foreach (var scenario in scenarios)
            {
                Console.WriteLine(scenario.Error is null ? $"  scenario {scenario.Id}: http://localhost:{binding.BoundPort}/#/scenario/{scenario.Id}" :
                                      $"  scenario {scenario.Id}: INVALID — {scenario.Error}");
            }
        }

        private WebApplication BuildApplication(int port)
        {
            var builder = WebApplication.CreateBuilder();

            // A bind failure is reported once, by the walk above, not as a hosting error with a stack trace per port.
            builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.Critical);

            builder.WebHost.UseKestrel(options => { options.ListenLocalhost(port); });

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
            builder.Services.AddSingleton(_budgets);
            builder.Services.AddSingleton(_devConfiguration);
            builder.Services.AddSingleton(_devHostEvents);
            builder.Services.AddSingleton(_control);
            builder.Services.AddSingleton<DevHostEventBroadcaster>();
            builder.Services.AddSingleton(new ScenarioStore(_devConfiguration.ScenariosPath));
            builder.Services.AddSingleton<ScenarioRunRegistry>();
            builder.Services.AddSingleton(new DevTopologyStore(_devConfiguration.TopologiesPath, _introspection.ValidateContractPairings));
            builder.Services.AddSingleton(_blockCatalog);

            var app = builder.Build();

            // IMPORTANT: Eagerly instantiate the broadcaster so it subscribes to events!
            app.Services.GetRequiredService<DevHostEventBroadcaster>();

            // Configure middleware pipeline
            app.UseRouting();
            app.UseCors();

            // Origin/Host guard on mutating requests (the local-tool security posture): the server binds loopback
            // only, but a hostile page in the developer's own browser can still fire cross-origin POSTs at
            // http://localhost:{port} — CORS does not prevent cross-origin sends. Reads stay open; mutations
            // require a loopback Host (DNS-rebinding guard) and, when a browser declares an Origin, a
            // loopback Origin. Headless local tools (curl, agents) send no Origin and pass.
            app.Use(async (context, next) =>
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
            app.MapControllers();
            app.MapHub<DevHostHub>("/hub");

            // Serve embedded SPA
            var assembly = typeof(DevHostBuilderExtensions).Assembly;
            var embeddedProvider = new EmbeddedFileProvider(assembly, "Vion.Dale.DevHost.Web.wwwroot");

            app.UseDefaultFiles(new DefaultFilesOptions
                                 {
                                     FileProvider = embeddedProvider,
                                 });

            // Dev host on localhost: correctness over caching. The no-build discipline rules out
            // content-hashed asset filenames, so every SPA file lives at a stable URL (/components.js, …).
            // Without this a browser applies heuristic freshness and serves the OLD file after a NuGet
            // upgrade of this package — the DevHost UI stays stale until a manual hard reload. `no-cache`
            // forces revalidation, which the ETag makes a cheap 304; nothing is served stale after an upgrade.
            void NoStaleSpaCache(StaticFileResponseContext ctx)
            {
                ctx.Context.Response.Headers.CacheControl = "no-cache";
            }

            app.UseStaticFiles(new StaticFileOptions
                                {
                                    FileProvider = embeddedProvider,
                                    OnPrepareResponse = NoStaleSpaCache,
                                });

            // The API's own catch-all, registered BEFORE the SPA's so the more specific pattern wins: without
            // it, `{*path:nonfile}` matches /api/anything (no file extension, so the constraint does not
            // exclude it) and a mistyped route answers 200 text/html. A caller that checks the status code
            // then reports the host healthy, and one that parses JSON gets a parse error naming a `<`.
            app.MapFallback("/api/{**rest}",
                             (HttpContext context) => Results.NotFound(new
                                                                       {
                                                                           error = $"no such route: {context.Request.Method} {context.Request.Path}",
                                                                           reason = "unknownRoute",
                                                                       }));

            app.MapFallbackToFile("index.html",
                                   new StaticFileOptions
                                   {
                                       FileProvider = embeddedProvider,
                                       OnPrepareResponse = NoStaleSpaCache,
                                   });

            return app;
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