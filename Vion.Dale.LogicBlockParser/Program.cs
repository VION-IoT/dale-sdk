using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Introspection;
using Vion.Dale.Plugin;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.LogicBlockParser
{
    internal class Program
    {
        /// <summary>
        ///     Opt-in filter for the pack path: leave every development-only logic block out of the emitted
        ///     JSON. Passed by <c>Vion.Dale.Sdk.targets</c> on <c>dotnet pack</c>, so the artifact that travels
        ///     to the cloud carries only production surface. Omitted everywhere else — <c>dale list</c> and the
        ///     DevHost introspect the whole assembly.
        /// </summary>
        private const string ExcludeDevelopmentOnlyOption = "--exclude-development-only";

        /// <summary>
        ///     The package identity the emitted document reports. <c>Vion.Dale.Sdk.targets</c> passes the
        ///     consuming project's <c>PackageId</c> — the id <c>dotnet pack</c> writes into the nuspec and the
        ///     cloud registers the library under — because that, not the assembly name, is the namespace every
        ///     translation key is prefixed with. Omitted or blank, the assembly's simple name stands in, which
        ///     is what MSBuild defaults <c>PackageId</c> to anyway.
        /// </summary>
        private const string PackageIdOption = "--package-id";

        /// <summary>
        ///     Prefix on every notice line the parser writes to stdout. Stable: <c>dale upload</c> captures the
        ///     pack output rather than inheriting it, and repeats the lines carrying this prefix.
        /// </summary>
        private const string NoticePrefix = "Vion Dale: ";

        private const string Usage = "Usage: Vion.Dale.LogicBlockParser.exe <path-to-plugin.dll> <output-json-path> [" + ExcludeDevelopmentOnlyOption + "] [" + PackageIdOption +
                                     " <id>]";

        private static readonly JsonSerializerOptions JsonOptions = new()
                                                                    {
                                                                        WriteIndented = true,
                                                                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                                                                        Converters =
                                                                        {
                                                                            new JsonStringEnumConverter(),
                                                                        },
                                                                    };

        /// <summary>
        ///     Return exit code: 0 = success, 1 = failure
        /// </summary>
        public static int Main(string[] args)
        {
            try
            {
                return RunParser(args);
            }
            catch (Exception ex)
            {
                // Keep console for critical errors - always visible
                Console.Error.WriteLine("Dale Logic Block Parser failed with error:");
                Console.Error.WriteLine($"Message: {ex.Message}");
                Console.Error.WriteLine($"Type: {ex.GetType().Name}");

                // Only show stack trace in verbose mode or if environment variable is set
                if (Environment.GetEnvironmentVariable("DALE_PARSER_VERBOSE") == "true")
                {
                    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        Console.Error.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
                    }
                }

                return 1;
            }
        }

        private static int RunParser(string[] args)
        {
            var excludeDevelopmentOnly = args.Any(argument => string.Equals(argument, ExcludeDevelopmentOnlyOption, StringComparison.OrdinalIgnoreCase));
            var suppliedPackageId = ReadOptionValue(args, PackageIdOption);

            // The options are ours, not the host's — strip them, and the value of any option that takes one,
            // before the configuration builder sees them.
            var positional = PositionalArguments(args);

            // Keep console for argument validation - critical errors
            if (positional.Length == 0 || string.IsNullOrEmpty(positional[0]))
            {
                Console.Error.WriteLine("Error: Missing plugin DLL path argument");
                Console.Error.WriteLine(Usage);
                return 1;
            }

            if (positional.Length == 1 || string.IsNullOrEmpty(positional[1]))
            {
                Console.Error.WriteLine("Error: Missing output json path argument");
                Console.Error.WriteLine(Usage);
                return 1;
            }

            var builder = Host.CreateApplicationBuilder(positional);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddDaleSdk();

            var logger = CreateLogger(builder);

            var pluginDllPath = positional[0];
            var outputJsonPath = positional[1];

            if (!File.Exists(pluginDllPath))
            {
                Console.Error.WriteLine($"Error: Plugin DLL not found: {pluginDllPath}");
                return 1;
            }

            logger.LogInformation($"Plugin path: {pluginDllPath}");
            logger.LogInformation($"Output JSON path: {outputJsonPath}");

            // for local test against sdk, uncomment this line and comment the two lines below
            //var pluginAssembly = typeof(LogicBlockBase).Assembly;
            var pluginAssembly = LoadPluginAssembly(pluginDllPath, logger);
            InvokeConfigureServicesFromPlugin(pluginAssembly, builder.Services, logger);
            InvokeConfigureServicesFromSharedAssemblies(pluginAssembly, builder.Services, logger);

            var app = builder.Build();

            List<string> instantiatedLogicBlocks = [];
            List<string> unregisteredLogicBlocks = [];

            var logicBlockTypes = GetLogicBlockTypes(pluginAssembly);
            logger.LogInformation($"Found {logicBlockTypes.Count} logic block types in assembly");

            var logicBlockResults = new List<LogicBlockIntrospectionResult>();

            foreach (var logicBlockType in logicBlockTypes)
            {
                try
                {
                    if (app.Services.GetService(logicBlockType) is not LogicBlockBase logicBlock)
                    {
                        unregisteredLogicBlocks.Add(ReflectionHelper.GetDisplayFullName(logicBlockType));
                        continue;
                    }

                    instantiatedLogicBlocks.Add(ReflectionHelper.GetDisplayFullName(logicBlockType));

                    var introspectionResult = LogicBlockIntrospection.IntrospectLogicBlock(logicBlock, app.Services);
                    logicBlockResults.Add(introspectionResult);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to process logic block {logicBlockType.FullName}");
                    return 1;
                }
            }

            if (unregisteredLogicBlocks.Count != 0)
            {
                // A concrete logic block the plugin does not register is one the cloud never sees: the block is
                // simply missing from the artifact, and a pack that succeeded anyway made that discoverable only
                // by its absence in the dashboard. Refuse the run instead, naming every such type.
                Console.Error.WriteLine("Error: the following logic block(s) are not registered in the plugin's IConfigureServices, " +
                                        "so they cannot be introspected. Register each one, or make the type abstract:");
                foreach (var logicBlockName in unregisteredLogicBlocks)
                {
                    Console.Error.WriteLine($"  {logicBlockName}");
                }

                return 1;
            }

            logger.LogInformation($"Instantiated and parsed the following {instantiatedLogicBlocks.Count} logic blocks:");
            foreach (var logicBlockName in instantiatedLogicBlocks)
            {
                logger.LogInformation(logicBlockName);
            }

            if (excludeDevelopmentOnly)
            {
                logicBlockResults = ExcludeDevelopmentOnlyLogicBlocks(logicBlockResults);
            }

            var result = new DalePluginInfo
                         {
                             PackageId = ResolvePackageId(suppliedPackageId, pluginAssembly),
                             PackageVersion = GetLogicBlockAssemblyVersion(pluginAssembly) ?? "0.0.0",
                             Annotations = new Dictionary<string, object>(),
                             LogicBlocks = logicBlockResults,
                         };

            WriteResultsToFile(result, outputJsonPath, logger);
            return 0;
        }

        /// <summary>
        ///     Drops every logic block that binds a development-only contract (a provider face) and names each
        ///     one on stdout, so a <c>dotnet pack</c> log says which blocks the production artifact does not
        ///     carry. The block is not an error — it is bench surface the cloud is never told about — so this
        ///     is a notice, not a warning, and the packed assembly keeps the type either way.
        /// </summary>
        private static List<LogicBlockIntrospectionResult> ExcludeDevelopmentOnlyLogicBlocks(List<LogicBlockIntrospectionResult> logicBlockResults)
        {
            var kept = new List<LogicBlockIntrospectionResult>();
            var excluded = new List<string>();

            foreach (var logicBlockResult in logicBlockResults)
            {
                var developmentOnlyContracts = LogicBlockIntrospection.GetDevelopmentOnlyContracts(logicBlockResult);
                if (developmentOnlyContracts.Count == 0)
                {
                    kept.Add(logicBlockResult);
                    continue;
                }

                var bindings = string.Join(", ", developmentOnlyContracts.Select(contract => $"{contract.Identifier} ({contract.MatchingContractType})"));
                excluded.Add($"  {logicBlockResult.TypeFullName} — binds {bindings}");
            }

            if (excluded.Count == 0)
            {
                return kept;
            }

            // Written straight to stdout rather than through the logger, and every line carries the same
            // prefix: MSBuild echoes the parser's console output at high importance, so this is what makes the
            // exclusion visible in a pack run, and the prefix is what lets `dale upload` — which captures the
            // pack output instead of inheriting it — repeat the notice to its own user.
            WriteNotice($"{excluded.Count} logic block(s) are development-only — not part of the production artifact:");
            foreach (var line in excluded)
            {
                WriteNotice(line);
            }

            WriteNotice("The assembly is packed unchanged; only the introspection JSON the cloud reads is filtered.");

            return kept;
        }

        private static void WriteNotice(string message)
        {
            Console.Out.WriteLine(NoticePrefix + message);
        }

        private static ILogger CreateLogger(HostApplicationBuilder builder)
        {
            using var tempProvider = builder.Services.BuildServiceProvider();
            var loggerFactory = tempProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(nameof(Program));
            return logger;
        }

        private static Assembly LoadPluginAssembly(string pluginDllPath, ILogger logger)
        {
            try
            {
                // Convert to absolute path if it's relative
                var absolutePath = Path.GetFullPath(pluginDllPath);
                logger.LogInformation($"Loading assembly from absolute path: {absolutePath}");

                var directoryName = Path.GetDirectoryName(absolutePath);
                var packageId = Path.GetFileNameWithoutExtension(absolutePath);
                var context = new PluginLoadContext(directoryName ?? throw new InvalidOperationException("Directory name must not be null"), packageId, logger);
                var pluginAssembly = context.LoadFromAssemblyPath(absolutePath);
                context.EagerlyLoadSharedExtensions();
                return pluginAssembly;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to load plugin assembly from {pluginDllPath}");
                throw;
            }
        }

        private static void InvokeConfigureServicesFromPlugin(Assembly pluginAssembly, IServiceCollection serviceCollection, ILogger logger)
        {
            var configureServicesTypes = pluginAssembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract).ToList();

            if (configureServicesTypes.Count == 0)
            {
                logger.LogError($"Assembly {pluginAssembly.FullName} does not contain a valid implementation of {nameof(IConfigureServices)}");
                throw new ArgumentException($"Assembly {pluginAssembly.FullName} does not contain a valid implementation of {nameof(IConfigureServices)}");
            }

            foreach (var type in configureServicesTypes)
            {
                try
                {
                    var registration = (IConfigureServices)Activator.CreateInstance(type)!;
                    registration.ConfigureServices(serviceCollection);
                    logger.LogInformation($"Invoked {nameof(IConfigureServices.ConfigureServices)} from {type.FullName}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Failed to invoke ConfigureServices from {type.FullName}");
                    throw;
                }
            }
        }

        /// <summary>
        ///     Registers services from the shared extension libraries the plugin pulled in. The
        ///     plugin's own assembly is excluded: it reaches the shared registry too when it carries
        ///     <c>[DaleSharedAssembly]</c>, and <see cref="InvokeConfigureServicesFromPlugin" /> has
        ///     already run its registrations — invoking them twice registers every service twice.
        /// </summary>
        private static void InvokeConfigureServicesFromSharedAssemblies(Assembly pluginAssembly, IServiceCollection serviceCollection, ILogger logger)
        {
            foreach (var assembly in PluginLoadContext.GetLoadedSharedExtensionAssemblies())
            {
                if (assembly == pluginAssembly)
                {
                    continue;
                }

                var configureServicesTypes = assembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract).ToList();
                foreach (var type in configureServicesTypes)
                {
                    var registration = (IConfigureServices)Activator.CreateInstance(type)!;
                    registration.ConfigureServices(serviceCollection);
                    logger.LogInformation("Auto-registered services from shared assembly {AssemblyName} via {TypeName}", assembly.GetName().Name, type.FullName);
                }
            }
        }

        /// <summary>
        ///     The value that follows <paramref name="option" /> on the command line, or <c>null</c> when the
        ///     option is absent or is the last argument. Matched case-insensitively, like every option here.
        /// </summary>
        private static string? ReadOptionValue(string[] args, string option)
        {
            for (var index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
                {
                    return args[index + 1];
                }
            }

            return null;
        }

        /// <summary>
        ///     The arguments that are not options and not an option's value. An option's value does not start
        ///     with <c>--</c>, so dropping option-shaped arguments alone would leave it looking positional.
        /// </summary>
        private static string[] PositionalArguments(string[] args)
        {
            var positional = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    positional.Add(args[index]);
                    continue;
                }

                if (string.Equals(args[index], PackageIdOption, StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                }
            }

            return positional.ToArray();
        }

        /// <summary>
        ///     The document's package identity: the id the pack supplied, else the plugin assembly's simple
        ///     name. A blank supplied id is treated as absent — MSBuild expands an unset property to the empty
        ///     string, so an unset <c>PackageId</c> must not become the library's identity.
        /// </summary>
        private static string ResolvePackageId(string? suppliedPackageId, Assembly pluginAssembly)
        {
            return !string.IsNullOrWhiteSpace(suppliedPackageId) ? suppliedPackageId! : pluginAssembly.GetName().Name ?? "Unknown";
        }

        private static string? GetLogicBlockAssemblyVersion(Assembly assembly)
        {
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        }

        private static List<Type> GetLogicBlockTypes(Assembly assembly)
        {
            return assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(LogicBlockBase)) && !type.IsAbstract).OrderBy(type => type.FullName).ToList();
        }

        private static void WriteResultsToFile(DalePluginInfo dalePluginInfo, string outputPath, ILogger logger)
        {
            var json = JsonSerializer.Serialize(dalePluginInfo, JsonOptions);
            logger.LogDebug(json);
            File.WriteAllText(outputPath, json);
            var fullPath = Path.GetFullPath(outputPath);
            logger.LogInformation(File.Exists(outputPath) ? $"{Environment.NewLine}The results have been saved to the file: {fullPath}." :
                                      $"The results could not be saved to the file {fullPath}.");
        }
    }
}