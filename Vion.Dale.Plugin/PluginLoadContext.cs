using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Plugin
{
    public class PluginLoadContext : AssemblyLoadContext
    {
        /// <summary>
        ///     Cache of assemblies marked with [DaleSharedAssembly] that are shared across all plugins.
        ///     The first plugin to request such an assembly loads it into its own context and stores it here.
        ///     All subsequent plugins reuse the same Assembly instance, ensuring type identity across plugins.
        ///     We load into the first plugin's context (not the default context) so that transitive
        ///     dependencies (e.g. Vion.Contracts.FlatBuffers) can be resolved from the plugin folder.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Assembly> SharedExtensionAssemblies = new();

        /// <summary>
        ///     Cache of assembly paths that have been inspected for [DaleSharedAssembly].
        ///     true = has the attribute (should be shared), false = does not have it.
        ///     Avoids repeated PE metadata reads for the same assembly across plugins.
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> SharedAssemblyAttributeCache = new();

        /// <summary>
        ///     Lock to synchronize loading of shared extension assemblies.
        ///     Without this, two plugins loading simultaneously could both load the same assembly.
        /// </summary>
        private static readonly object SharedExtensionLoadLock = new();

        private readonly ILogger _logger;

        private readonly string _packageId;

        private readonly string _pluginPath;

        private readonly IReadOnlySet<string> _sharedAssemblyNames = GetSharedAssemblyNames();

        public PluginLoadContext(string pluginPath, string packageId, ILogger logger) : base(false)
        {
            _pluginPath = pluginPath;
            _packageId = packageId;
            _logger = logger;
            _logger.LogInformation("PluginLoadContext created for plugin {PackageId} at path: {PluginPath}", _packageId, _pluginPath);
        }

        /// <summary>
        ///     Returns all shared extension assemblies that have been loaded and cached.
        ///     Used by the runtime to auto-invoke IConfigureServices from shared assemblies.
        /// </summary>
        public static IReadOnlyCollection<Assembly> GetLoadedSharedExtensionAssemblies()
        {
            return SharedExtensionAssemblies.Values.ToList().AsReadOnly();
        }

        /// <summary>
        ///     Eagerly loads all assemblies marked with [DaleSharedAssembly] from the plugin directory.
        ///     This ensures handler actors (e.g. HalDigitalOutputHandler, HalAnalogOutputHandler) are
        ///     available in the AppDomain before CreateMqttHandlerActors scans for them.
        /// </summary>
        public void EagerlyLoadSharedExtensions()
        {
            foreach (var dllPath in Directory.EnumerateFiles(_pluginPath, "*.dll"))
            {
                var fullPath = Path.GetFullPath(dllPath);
                var assemblyName = Path.GetFileNameWithoutExtension(dllPath);

                // Skip if already loaded
                if (SharedExtensionAssemblies.ContainsKey(assemblyName))
                {
                    continue;
                }

                if (HasDaleSharedAssemblyAttribute(fullPath))
                {
                    // Trigger Load() which handles shared caching
                    LoadFromAssemblyName(new AssemblyName(assemblyName));
                }
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null)
            {
                return null;
            }

            // Strategy 1: Always load explicitly shared assemblies from default context
            if (_sharedAssemblyNames.Contains(assemblyName.Name))
            {
                var defaultAssembly = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (defaultAssembly != null)
                {
                    LogDefaultContextLoad(assemblyName, defaultAssembly);
                    return defaultAssembly;
                }

                // If explicitly shared but not loaded yet, delegate to default context to load it there
                _logger.LogInformation("Assembly {AssemblyNameName} is explicitly shared but not yet loaded, delegating to default context", assemblyName.Name);
                return null;
            }

            // Strategy 2: Share all BCL and Microsoft framework assemblies from default context if available
            // This prevents type mismatches for common framework types
            if (ShouldLoadFromDefaultContext(assemblyName.Name))
            {
                var defaultAssembly = Default.Assemblies.FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (defaultAssembly != null)
                {
                    LogDefaultContextLoad(assemblyName, defaultAssembly);
                    return defaultAssembly;
                }

                // Check if the assembly exists in the plugin folder before delegating
                // This handles cases where the plugin has dependencies not present in the host
                var assemblyPath = Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");
                if (File.Exists(assemblyPath))
                {
                    _logger.LogInformation("Loading assembly {AssemblyName} {Version} from plugin path (framework assembly not in default context)",
                                           assemblyName.Name,
                                           assemblyName.Version);
                    return LoadFromAssemblyPath(assemblyPath);
                }

                // If not in default context and not in plugin folder, delegate to default load
                // This will load it into the default context if it can be resolved
                _logger.LogInformation("Assembly {AssemblyName} not in default context or plugin folder, delegating to default load", assemblyName.Name);
                return null;
            }

            // Strategy 3: Check if the assembly is marked with [DaleSharedAssembly] and share it
            // across all plugins. This handles SDK extension assemblies (DigitalIo, AnalogIo, Mobus.Rtu
            // etc.) and service provider extension libraries that define contract handler actors
            // or message types used in cross-plugin communication.
            var pluginAssemblyPath = Path.Combine(_pluginPath, $"{assemblyName.Name}.dll");
            if (File.Exists(pluginAssemblyPath))
            {
                // Fast path: already loaded and cached by another plugin
                if (SharedExtensionAssemblies.TryGetValue(assemblyName.Name, out var cachedAssembly))
                {
                    _logger.LogInformation("Reusing shared extension {AssemblyName} {Version} (first loaded by another plugin)",
                                           assemblyName.Name,
                                           cachedAssembly.GetName().Version);
                    return cachedAssembly;
                }

                var fullPath = Path.GetFullPath(pluginAssemblyPath);
                if (HasDaleSharedAssemblyAttribute(fullPath))
                {
                    lock (SharedExtensionLoadLock)
                    {
                        // Double-check after acquiring lock — another plugin may have loaded it
                        if (SharedExtensionAssemblies.TryGetValue(assemblyName.Name, out cachedAssembly))
                        {
                            _logger.LogInformation("Reusing shared extension {AssemblyName} {Version} (first loaded by another plugin)",
                                                   assemblyName.Name,
                                                   cachedAssembly.GetName().Version);
                            return cachedAssembly;
                        }

                        _logger.LogInformation("Loading shared extension {AssemblyName} {Version} from plugin {PackageId} — " +
                                               "marked with [DaleSharedAssembly], will be shared with all plugins",
                                               assemblyName.Name,
                                               assemblyName.Version,
                                               _packageId);
                        var assembly = LoadFromAssemblyPath(fullPath);
                        SharedExtensionAssemblies[assemblyName.Name] = assembly;
                        return assembly;
                    }
                }

                // Strategy 4: Load plugin-specific assemblies from plugin folder (no sharing)
                _logger.LogInformation("Loading assembly {AssemblyName} {Version} from plugin path {PluginPath}", assemblyName.Name, assemblyName.Version, _pluginPath);
                return LoadFromAssemblyPath(fullPath);
            }

            // Let the default load context handle it
            return null;
        }

        /// <summary>
        ///     Checks whether an assembly file has the [DaleSharedAssembly] attribute applied at the assembly level.
        ///     Uses PEReader to inspect metadata without loading the assembly into any context.
        ///     Results are cached to avoid repeated file I/O for the same assembly across plugins.
        /// </summary>
        private bool HasDaleSharedAssemblyAttribute(string assemblyPath)
        {
            return SharedAssemblyAttributeCache.GetOrAdd(assemblyPath,
                                                         static path =>
                                                         {
                                                             try
                                                             {
                                                                 using var stream = File.OpenRead(path);
                                                                 using var peReader = new PEReader(stream);
                                                                 var metadataReader = peReader.GetMetadataReader();

                                                                 foreach (var attrHandle in metadataReader.GetAssemblyDefinition().GetCustomAttributes())
                                                                 {
                                                                     var attr = metadataReader.GetCustomAttribute(attrHandle);
                                                                     if (attr.Constructor.Kind != HandleKind.MemberReference)
                                                                     {
                                                                         continue;
                                                                     }

                                                                     var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                                                                     if (memberRef.Parent.Kind != HandleKind.TypeReference)
                                                                     {
                                                                         continue;
                                                                     }

                                                                     var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                                                                     var typeName = metadataReader.GetString(typeRef.Name);
                                                                     var typeNamespace = metadataReader.GetString(typeRef.Namespace);

                                                                     if (typeName == nameof(DaleSharedAssemblyAttribute) && typeNamespace == "Vion.Dale.Sdk.Core")
                                                                     {
                                                                         return true;
                                                                     }
                                                                 }
                                                             }
                                                             catch (Exception)
                                                             {
                                                                 // If we can't read the metadata (corrupted file, not a .NET assembly, etc.),
                                                                 // treat it as not shared — it will be loaded normally into the plugin context
                                                             }

                                                             return false;
                                                         });
        }

        /// <summary>
        ///     Explicitly shared assemblies that MUST be loaded from the host's default assembly load
        ///     context to avoid type mismatches. This is for application-specific assemblies.
        /// </summary>
        /// <remarks>
        ///     Microsoft.Extensions.* and System.* assemblies are automatically shared via ShouldLoadFromDefaultContext().
        ///     Extension assemblies marked with [DaleSharedAssembly] are handled dynamically in the Load() method —
        ///     the first plugin to request one loads it and caches the instance, and all subsequent plugins share
        ///     that same Assembly instance for type identity.
        /// </remarks>
        private static IReadOnlySet<string> GetSharedAssemblyNames()
        {
            var sdkAssembly = typeof(LogicBlockBase).Assembly;
            var sharedNames = new HashSet<string> { sdkAssembly.GetName().Name! };
            foreach (var referencedAssembly in sdkAssembly.GetReferencedAssemblies())
            {
                if (referencedAssembly.Name != null)
                {
                    sharedNames.Add(referencedAssembly.Name);
                }
            }

            return sharedNames;
        }

        /// <summary>
        ///     Determines if an assembly should be loaded from the default context rather than isolated in the plugin context.
        ///     Returns true for BCL, runtime, and Microsoft framework assemblies.
        /// </summary>
        private static bool ShouldLoadFromDefaultContext(string assemblyName)
        {
            // All System.* assemblies (BCL and runtime including System.Private.*)
            if (assemblyName.StartsWith("System.") || assemblyName == "System")
            {
                return true;
            }

            // Core runtime assemblies
            if (assemblyName == "netstandard" || assemblyName == "mscorlib")
            {
                return true;
            }

            // All Microsoft.* framework assemblies (Extensions, AspNetCore, etc.)
            // Note: Using StartsWith("Microsoft.") to catch all Microsoft framework assemblies
            if (assemblyName.StartsWith("Microsoft."))
            {
                return true;
            }

            return false;
        }

        private void LogDefaultContextLoad(AssemblyName assemblyName, Assembly defaultAssembly)
        {
            var loadedVersion = defaultAssembly.GetName().Version;
            if (assemblyName.Version != null && loadedVersion != null && assemblyName.Version != loadedVersion)
            {
                // Host providing a newer version is expected and safe (e.g. netstandard 2.1 > 2.0)
                if (loadedVersion > assemblyName.Version)
                {
                    _logger.LogDebug("Plugin {PackageId} references shared assembly {AssemblyName} version {RequestedVersion}, " + "host provides newer version {Version}",
                                     _packageId,
                                     assemblyName.Name,
                                     assemblyName.Version,
                                     loadedVersion);
                }
                else
                {
                    _logger.LogWarning("Plugin {PackageId} references shared assembly {AssemblyName} version {RequestedVersion}, " +
                                       "but the host provides older version {Version} which will be used instead — " +
                                       "this may cause runtime errors if there are breaking changes between the versions",
                                       _packageId,
                                       assemblyName.Name,
                                       assemblyName.Version,
                                       loadedVersion);
                }
            }
            else
            {
                _logger.LogInformation("Loading assembly {AssemblyName} {Version} from default context (BCL/framework)", assemblyName.Name, assemblyName.Version);
            }
        }
    }
}