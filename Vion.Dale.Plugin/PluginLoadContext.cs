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
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

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
            // Same tolerance the constructor's version scan applies: a caller pointing at a
            // directory that is not there gets its own error, not a DirectoryNotFoundException
            // raised one call after construction accepted the very same path.
            if (!Directory.Exists(_pluginPath))
            {
                return;
            }

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
                    var assembly = LoadFromAssemblyName(new AssemblyName(assemblyName));

                    // ...except when this context already holds the assembly: LoadFromAssemblyName
                    // then returns it without routing through Load(), and Load() is the only writer
                    // of the shared registry. The runtime loads a plugin's own dll by path before
                    // calling this method, so a marked plugin assembly arrives on exactly that path
                    // and would otherwise never reach the registry downstream code enumerates.
                    // Only register what THIS context owns: a bind that resolved to the host or to
                    // another plugin belongs to whoever loaded it, and TryAdd keeps that owner.
                    if (GetLoadContext(assembly) == this)
                    {
                        SharedExtensionAssemblies.TryAdd(assemblyName, assembly);
                    }
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
                var fullPath = Path.GetFullPath(pluginAssemblyPath);

                // THIS plugin's own copy decides whether it shares, and the check comes before the
                // cache: sharing on a simple-name cache hit alone hands a plugin that never applied
                // the attribute another plugin's assembly, so its types silently become code it
                // never shipped. The verdict is memoized per path, so the cost is a lookup.
                if (HasDaleSharedAssemblyAttribute(fullPath))
                {
                    // Fast path: already loaded and cached by another plugin
                    if (SharedExtensionAssemblies.TryGetValue(assemblyName.Name, out var cachedAssembly))
                    {
                        _logger.LogInformation("Reusing shared extension {AssemblyName} {Version} (first loaded by another plugin)",
                                               assemblyName.Name,
                                               cachedAssembly.GetName().Version);
                        return cachedAssembly;
                    }

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

                // A same-named shared instance exists but THIS plugin's copy is unmarked, so it
                // gets its own. The two plugins now hold distinct types of the same name, which is
                // correct and also the hardest kind of mismatch to read from a stack trace later —
                // so say so, naming the other plugin by where its copy came from.
                if (SharedExtensionAssemblies.TryGetValue(assemblyName.Name, out var sharedWithSameName))
                {
                    _logger.LogWarning("Plugin {PackageId} loads its own {AssemblyName} from {PluginPath} because that copy is not marked [DaleSharedAssembly], " +
                                       "while a shared instance of the same name is already loaded from {SharedAssemblyLocation} — types of that name are NOT interchangeable between the two plugins",
                                       _packageId,
                                       assemblyName.Name,
                                       _pluginPath,
                                       sharedWithSameName.Location);
                }

                // Strategy 4: Load plugin-specific assemblies from plugin folder (no sharing)
                _logger.LogInformation("Loading assembly {AssemblyName} {Version} from plugin path {PluginPath}", assemblyName.Name, assemblyName.Version, _pluginPath);
                return LoadFromAssemblyPath(fullPath);
            }

            // Let the default load context handle it
            return null;
        }

        /// <summary>
        ///     Throws when <paramref name="pluginReferencedVersion" /> has a different MAJOR component
        ///     than <paramref name="hostVersion" /> (both non-null). Minor and patch differences are
        ///     deliberately not this method's concern: they stay warn-and-continue.
        /// </summary>
        /// <remarks>
        ///     Accepted consequence: while the SDK is pre-1.0 every version's major is 0, so this gate
        ///     never fires — a 0.4.3 plugin against a 0.5.0 host is a warning, not a rejection. The gate
        ///     arms itself at 1.0. That is the intent, not an oversight.
        /// </remarks>
        internal static void EnsureSdkMajorCompatible(string packageId, string sdkAssemblyName, Version? hostVersion, Version? pluginReferencedVersion, ILogger logger)
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
                    _logger.LogDebug("Skipping {DllPath} during the SDK-version check: it is not a readable .NET assembly " + "or does not reference {SdkAssemblyName}",
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