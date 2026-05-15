using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("Vion.Dale.Plugin.Test")]

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

            // Fail fast on a binary-incompatible SDK major-version skew BEFORE any plugin
            // assembly is loaded into this context and BEFORE the runtime reflects over plugin
            // types. The constructor is the earliest chokepoint that runs in this class: every
            // Load() / EagerlyLoadSharedExtensions() / runtime type-reflection happens strictly
            // after construction, and reading PE metadata does not require loading the assembly
            // into the context. Differing minor/patch stays warn-and-continue (see
            // LogDefaultContextLoad) — only a differing MAJOR is unrecoverable.
            EnforceSdkMajorCompatibility();
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
        ///     Scans the plugin DLLs in <see cref="_pluginPath" /> for an assembly reference to
        ///     <c>Vion.Dale.Sdk</c> and fails the load fast if one of them references a different
        ///     MAJOR version than the SDK the host runtime has actually loaded. Mirrors the
        ///     enumeration in <see cref="EagerlyLoadSharedExtensions" /> and the defensive metadata
        ///     posture of <see cref="HasDaleSharedAssemblyAttribute" />. Throws on the first plugin
        ///     assembly with a differing major version; remaining assemblies are not inspected.
        /// </summary>
        private void EnforceSdkMajorCompatibility()
        {
            // The host's loaded SDK — the same assembly GetSharedAssemblyNames() keys off.
            var sdkAssemblyName = typeof(LogicBlockBase).Assembly.GetName().Name!;
            var hostSdkVersion = typeof(LogicBlockBase).Assembly.GetName().Version;

            if (!Directory.Exists(_pluginPath))
            {
                return;
            }

            foreach (var dllPath in Directory.EnumerateFiles(_pluginPath, "*.dll"))
            {
                var fullPath = Path.GetFullPath(dllPath);
                var referencedSdkVersion = TryReadReferencedSdkVersion(fullPath, sdkAssemblyName);
                if (referencedSdkVersion == null)
                {
                    // Either the file is not a readable .NET assembly (a corrupt / non-managed
                    // dll is not an SDK-version failure) or it simply does not reference the SDK.
                    _logger.LogDebug("Skipping {DllPath} during the SDK-version check: it is not a readable .NET assembly " +
                                     "or does not reference {SdkAssemblyName}",
                                     fullPath,
                                     sdkAssemblyName);
                    continue;
                }

                // Throws PluginSdkVersionMismatchException on a differing major. Minor/patch
                // differences return normally and remain warn-and-continue via LogDefaultContextLoad.
                EnsureSdkMajorCompatible(_packageId, sdkAssemblyName, hostSdkVersion, referencedSdkVersion, _logger);
            }
        }

        /// <summary>
        ///     Pure, unit-testable seam: throws when <paramref name="pluginReferencedVersion" /> has a
        ///     different MAJOR component than <paramref name="hostVersion" /> (both non-null). Minor and
        ///     patch differences are intentionally NOT this method's concern — those stay with the
        ///     existing warn-and-continue path (<see cref="LogDefaultContextLoad" />).
        /// </summary>
        /// <remarks>
        ///     ACCEPTED, DELIBERATE CONSEQUENCE: during 0.x the major is always 0, so this gate is
        ///     dormant pre-1.0 — a 0.4.3 → 0.5.0 skew stays a WARNING, not a hard fail. See spec §E
        ///     "Consequence (accepted)" / decision 0022. Pinned by PluginSdkVersionGateShould.
        /// </remarks>
        internal static void EnsureSdkMajorCompatible(string packageId,
                                                      string sdkAssemblyName,
                                                      Version? hostVersion,
                                                      Version? pluginReferencedVersion,
                                                      ILogger logger)
        {
            if (hostVersion == null || pluginReferencedVersion == null)
            {
                return;
            }

            if (hostVersion.Major == pluginReferencedVersion.Major)
            {
                return;
            }

            var message = $"Plugin '{packageId}' was built against {sdkAssemblyName} {pluginReferencedVersion} " +
                          $"but the host runtime has loaded {sdkAssemblyName} {hostVersion}. These major versions " +
                          $"are incompatible (major {pluginReferencedVersion.Major} vs {hostVersion.Major}). " +
                          $"Rebuild the plugin against a compatible {sdkAssemblyName} (matching major version {hostVersion.Major}.x) and redeploy it.";

            logger.LogError("Plugin {PackageId} references {SdkAssemblyName} {PluginVersion} but the host loaded {HostVersion} — " +
                            "incompatible major versions, failing the plugin load",
                            packageId,
                            sdkAssemblyName,
                            pluginReferencedVersion,
                            hostVersion);

            throw new PluginSdkVersionMismatchException(message);
        }

        /// <summary>
        ///     Reads the version of the <paramref name="sdkAssemblyName" /> assembly reference declared
        ///     by the assembly at <paramref name="assemblyPath" />, using PEReader so nothing is loaded
        ///     into any context. Returns <c>null</c> if the file cannot be read as a .NET assembly or
        ///     does not reference the SDK at all — same defensive posture as
        ///     <see cref="HasDaleSharedAssemblyAttribute" /> (a corrupt / non-.NET dll is not an
        ///     SDK-version failure).
        /// </summary>
        internal static Version? TryReadReferencedSdkVersion(string assemblyPath, string sdkAssemblyName)
        {
            try
            {
                using var stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    return null;
                }

                var metadataReader = peReader.GetMetadataReader();

                foreach (var handle in metadataReader.AssemblyReferences)
                {
                    var assemblyRef = metadataReader.GetAssemblyReference(handle);
                    var name = metadataReader.GetString(assemblyRef.Name);
                    if (name == sdkAssemblyName)
                    {
                        return assemblyRef.Version;
                    }
                }
            }
            catch (Exception)
            {
                // If we can't read the metadata (corrupted file, not a .NET assembly, etc.),
                // treat it as "no SDK reference found" — it is not an SDK-version failure.
            }

            return null;
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