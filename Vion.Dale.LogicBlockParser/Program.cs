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
            // Keep console for argument validation - critical errors
            if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                Console.Error.WriteLine("Error: Missing plugin DLL path argument");
                Console.Error.WriteLine("Usage: Vion.Dale.LogicBlockParser.exe <path-to-plugin.dll> <output-json-path>");
                return 1;
            }

            if (args.Length == 1 || string.IsNullOrEmpty(args[1]))
            {
                Console.Error.WriteLine("Error: Missing output json path argument");
                Console.Error.WriteLine("Usage: Vion.Dale.LogicBlockParser.exe <path-to-plugin.dll> <output-json-path>");
                return 1;
            }

            var builder = Host.CreateApplicationBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Services.AddDaleSdk();

            var logger = CreateLogger(builder);

            var pluginDllPath = args[0];
            var outputJsonPath = args[1];

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
            InvokeConfigureServicesFromSharedAssemblies(builder.Services, logger);

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
                logger.LogError($"{Environment.NewLine}Failed to instantiate the following logic blocks because they are not registered in the DI:");
                foreach (var logicBlockName in unregisteredLogicBlocks)
                {
                    logger.LogInformation(logicBlockName);
                }
            }

            logger.LogInformation($"Instantiated and parsed the following {instantiatedLogicBlocks.Count} logic blocks:");
            foreach (var logicBlockName in instantiatedLogicBlocks)
            {
                logger.LogInformation(logicBlockName);
            }

            var result = new DalePluginInfo
                         {
                             PackageId = GetLogicBlockPackageId(pluginAssembly) ?? "Unknown",
                             PackageVersion = GetLogicBlockAssemblyVersion(pluginAssembly) ?? "0.0.0",
                             Annotations = new Dictionary<string, object>(),
                             LogicBlocks = logicBlockResults,
                         };

            WriteResultsToFile(result, outputJsonPath, logger);
            return 0;
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

        private static void InvokeConfigureServicesFromSharedAssemblies(IServiceCollection serviceCollection, ILogger logger)
        {
            foreach (var assembly in PluginLoadContext.GetLoadedSharedExtensionAssemblies())
            {
                var configureServicesTypes = assembly.GetTypes().Where(t => typeof(IConfigureServices).IsAssignableFrom(t) && !t.IsAbstract).ToList();
                foreach (var type in configureServicesTypes)
                {
                    var registration = (IConfigureServices)Activator.CreateInstance(type)!;
                    registration.ConfigureServices(serviceCollection);
                    logger.LogInformation("Auto-registered services from shared assembly {AssemblyName} via {TypeName}", assembly.GetName().Name, type.FullName);
                }
            }
        }

        private static string? GetLogicBlockPackageId(Assembly assembly)
        {
            return assembly.GetName().Name;
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
