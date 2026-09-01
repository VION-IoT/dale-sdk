using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Plugin.Test.TestHelpers;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Plugin.Test
{
    /// <summary>
    ///     The plugin loading ABI: docs/specs/plugin-loading.md. Tests are ordered to mirror the
    ///     SUT — construction and the SDK version gate, then the four resolution strategies in the
    ///     order <c>Load</c> applies them, then eager loading and the shared registry.
    /// </summary>
    /// <remarks>
    ///     Fixtures are compiled onto disk rather than mocked: the loader reads PE metadata to
    ///     decide sharing and to read a plugin's SDK reference, so nothing short of a real assembly
    ///     exercises it. Every fixture takes a name unique to its test — the shared-extension
    ///     registry is keyed by simple name and outlives the test that filled it.
    /// </remarks>
    [TestClass]
    public class PluginLoadContextShould
    {
        private const string PackageId = "Acme.Sample.Plugin";

        private const string SdkAssemblyName = "Vion.Dale.Sdk";

        private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);

        private readonly List<string> _directories = new();

        private readonly ILogger _logger = NullLogger.Instance;

        private string _pluginDirectory = null!;

        private string _stagingDirectory = null!;

        /// <summary>The major version of the SDK this test process has loaded — what the gate compares against.</summary>
        private static int HostSdkMajor
        {
            get => typeof(LogicBlockBase).Assembly.GetName().Version!.Major;
        }

        [TestInitialize]
        public void Initialize()
        {
            _pluginDirectory = CreateDirectory();
            _stagingDirectory = CreateDirectory();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Best-effort: a plugin load context is non-collectible (AC-PLUG-001.1), so any fixture
            // a test actually loaded stays locked for the lifetime of the process and its directory
            // cannot be removed. Leaving it is the cost of the fixtures; failing the test is not.
            foreach (var directory in _directories)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-001.1")]
        public void CreateNonCollectibleContext()
        {
            // Arrange
            // Act
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Assert
            Assert.IsFalse(sut.IsCollectible);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.1")]
        public void RejectPluginBuiltAgainstDifferingSdkMajor()
        {
            // Arrange
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, new Version(HostSdkMajor + 1, 0, 0, 0));
            EmitBuiltAgainstSdk(_pluginDirectory, FixtureAssembly.UniqueName("Fixture"), standInSdk);

            // Act / Assert
            Assert.ThrowsExactly<PluginSdkVersionMismatchException>(() => new PluginLoadContext(_pluginDirectory, PackageId, _logger));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.2")]
        [DataRow(9, 9, 0, DisplayName = "higher minor")]
        [DataRow(0, 0, 1, DisplayName = "higher revision only")]
        public void CreateContextWhenSdkMajorMatches(int minor, int build, int revision)
        {
            // Arrange
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, new Version(HostSdkMajor, minor, build, revision));
            EmitBuiltAgainstSdk(_pluginDirectory, FixtureAssembly.UniqueName("Fixture"), standInSdk);

            // Act
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Assert
            CollectionAssert.Contains(AssemblyLoadContext.All.ToList(), sut, "Minor, build and revision skew leaves a usable context behind.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.3")]
        public void ReportPackageAndBothVersionsWhenRejectingPlugin()
        {
            // Arrange
            var hostVersion = new Version(1, 4, 0);
            var pluginVersion = new Version(2, 0, 0);

            // Act
            var exception =
                Assert.ThrowsExactly<PluginSdkVersionMismatchException>(() => PluginLoadContext.EnsureSdkMajorCompatible(PackageId,
                                                                            SdkAssemblyName,
                                                                            hostVersion,
                                                                            pluginVersion,
                                                                            _logger));

            // Assert
            StringAssert.Contains(exception.Message, PackageId);
            StringAssert.Contains(exception.Message, hostVersion.ToString());
            StringAssert.Contains(exception.Message, pluginVersion.ToString());
            StringAssert.Contains(exception.Message, SdkAssemblyName);
            StringAssert.Contains(exception.Message, "Rebuild the plugin");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.4")]
        public void CreateContextWhenPluginDirectoryMissing()
        {
            // Arrange
            var missingDirectory = Path.Combine(Path.GetTempPath(), $"dale-missing-{Guid.NewGuid():N}");

            // Act
            var sut = new PluginLoadContext(missingDirectory, PackageId, _logger);

            // Assert
            CollectionAssert.Contains(AssemblyLoadContext.All.ToList(), sut, "A directory the gate cannot enumerate is the caller's to report, not a construction failure.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.5")]
        public void CreateContextWhenDirectoryHoldsFilesNotBuiltAgainstSdk()
        {
            // Arrange
            FixtureAssembly.EmitUnreadable(_pluginDirectory, FixtureAssembly.UniqueName("Corrupt"));
            FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Unrelated"));

            // Act
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Assert
            CollectionAssert.Contains(AssemblyLoadContext.All.ToList(), sut, "A corrupt dll and an assembly that never references the SDK are both skipped, not failures.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.5")]
        public void ReadNoSdkVersionFromAssemblyNotBuiltAgainstSdk()
        {
            // Arrange
            var unrelated = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Unrelated"));

            // Act
            var version = PluginLoadContext.TryReadReferencedSdkVersion(unrelated, SdkAssemblyName);

            // Assert
            Assert.IsNull(version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.5")]
        public void ReadNoSdkVersionFromNonAssemblyFile()
        {
            // Arrange
            var corrupt = FixtureAssembly.EmitUnreadable(_pluginDirectory, FixtureAssembly.UniqueName("Corrupt"));

            // Act
            var version = PluginLoadContext.TryReadReferencedSdkVersion(corrupt, SdkAssemblyName);

            // Assert
            Assert.IsNull(version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.6")]
        public void ReadSdkVersionPluginWasBuiltAgainst()
        {
            // Arrange
            var builtAgainst = new Version(HostSdkMajor, 7, 3, 0);
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, builtAgainst);
            var fixture = EmitBuiltAgainstSdk(_pluginDirectory, FixtureAssembly.UniqueName("Fixture"), standInSdk);

            // Act
            var version = PluginLoadContext.TryReadReferencedSdkVersion(fixture, SdkAssemblyName);

            // Assert
            Assert.AreEqual(builtAgainst, version);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.6")]
        public void ReadSdkVersionWithoutLoadingPlugin()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Fixture");
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, new Version(HostSdkMajor, 7, 3, 0));
            var fixture = EmitBuiltAgainstSdk(_pluginDirectory, fixtureName, standInSdk);

            // Act
            _ = PluginLoadContext.TryReadReferencedSdkVersion(fixture, SdkAssemblyName);

            // Assert
            Assert.IsFalse(LoadedAnywhere(fixtureName), "Reading the reference must not put the assembly into any load context.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-002.7")]
        public void RejectPluginNamingOneFailingAssembly()
        {
            // Arrange
            var firstSkew = new Version(HostSdkMajor + 1, 0, 0, 0);
            var secondSkew = new Version(HostSdkMajor + 2, 0, 0, 0);
            EmitBuiltAgainstSdk(_pluginDirectory, FixtureAssembly.UniqueName("FixtureA"), FixtureAssembly.EmitStandInSdk(_stagingDirectory, firstSkew));
            EmitBuiltAgainstSdk(_pluginDirectory, FixtureAssembly.UniqueName("FixtureB"), FixtureAssembly.EmitStandInSdk(CreateDirectory(), secondSkew));

            // Act
            var exception = Assert.ThrowsExactly<PluginSdkVersionMismatchException>(() => new PluginLoadContext(_pluginDirectory, PackageId, _logger));

            // Assert
            Assert.AreNotEqual(exception.Message.Contains(firstSkew.ToString()),
                               exception.Message.Contains(secondSkew.ToString()),
                               "Exactly one of the two failing assemblies is named — the rejection reports a failure, not an inventory of them.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-003.1")]
        public void ResolveHostInstanceForSdkReferencedAssembly()
        {
            // Google.FlatBuffers is in the shared set only because Vion.Dale.Sdk references it —
            // its name matches no prefix rule and it carries no sharing attribute.
            // Arrange
            var hostInstance = LoadIntoHost(Path.Combine(AppContext.BaseDirectory, "Google.FlatBuffers.dll"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Google.FlatBuffers.dll"), Path.Combine(_pluginDirectory, "Google.FlatBuffers.dll"));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName("Google.FlatBuffers"));

            // Assert
            Assert.AreSame(hostInstance, resolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-003.2")]
        public void ResolveHostSdkInstanceOverPluginCopy()
        {
            // Arrange
            File.Copy(Path.Combine(AppContext.BaseDirectory, $"{SdkAssemblyName}.dll"), Path.Combine(_pluginDirectory, $"{SdkAssemblyName}.dll"));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(SdkAssemblyName));

            // Assert
            Assert.AreSame(typeof(LogicBlockBase).Assembly, resolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-003.3")]
        public void ResolveUnloadedSharedAssemblyThroughHost()
        {
            // Vion.Contracts is in the shared set — Vion.Dale.Sdk references it — but nothing in
            // this process has loaded it, so this is the delegating arm of the same rule.
            // Arrange
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Vion.Contracts.dll"), Path.Combine(_pluginDirectory, "Vion.Contracts.dll"));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName("Vion.Contracts"));

            // Assert
            Assert.AreSame(AssemblyLoadContext.Default,
                           AssemblyLoadContext.GetLoadContext(resolved),
                           "A shared assembly belongs in the host's context even when the plugin directory is where the file came from.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-004.1")]
        [TestProperty("spec", "AC-PLUG-004.2")]
        [DataRow("System.")]
        [DataRow("Microsoft.")]
        public void ResolveHostInstanceForFrameworkPrefixedAssembly(string prefix)
        {
            // Arrange
            var fixtureName = prefix + FixtureAssembly.UniqueName("Fixture");
            var staged = FixtureAssembly.Emit(_stagingDirectory, fixtureName);
            var hostInstance = LoadIntoHost(staged);
            File.Copy(staged, Path.Combine(_pluginDirectory, $"{fixtureName}.dll"));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(hostInstance, resolved, "The host's copy wins over the one sitting in the plugin directory.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-004.3")]
        public void LoadFrameworkAssemblyFromPluginDirectoryWhenHostLacksIt()
        {
            // Arrange
            var fixtureName = "System." + FixtureAssembly.UniqueName("Fixture");
            FixtureAssembly.Emit(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(sut, AssemblyLoadContext.GetLoadContext(resolved));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.1")]
        public void LoadMarkedAssemblyIntoRequestingPluginContext()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(sut, AssemblyLoadContext.GetLoadContext(resolved));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.1")]
        public void RetainMarkedAssemblyAsSharedInstance()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            CollectionAssert.Contains(PluginLoadContext.GetLoadedSharedExtensionAssemblies().ToList(), resolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.2")]
        [TestProperty("spec", "AC-PLUG-005.7")]
        public void ResolveSharedInstanceForEveryFurtherPlugin()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            var marked = EmitMarked(_pluginDirectory, fixtureName);
            var secondPluginDirectory = CreateDirectory();
            File.Copy(marked, Path.Combine(secondPluginDirectory, $"{fixtureName}.dll"));
            var firstResolved = new PluginLoadContext(_pluginDirectory, PackageId, _logger).LoadFromAssemblyName(new AssemblyName(fixtureName));
            var secondPlugin = new PluginLoadContext(secondPluginDirectory, PackageId, _logger);

            // Act
            var secondResolved = secondPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(firstResolved, secondResolved, "Two plugins sharing a library must see one type identity.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.9")]
        public void ResolveSharedExtensionDependenciesAgainstOwnersDirectory()
        {
            // Arrange
            var dependencyName = FixtureAssembly.UniqueName("Dependency");
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            var dependency = FixtureAssembly.Emit(_pluginDirectory, dependencyName, body: "public class DependencyBase { }");
            var marked = FixtureAssembly.Emit(_pluginDirectory,
                                              fixtureName,
                                              new[] { Path.Combine(AppContext.BaseDirectory, $"{SdkAssemblyName}.dll"), dependency },
                                              new[] { "[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]" },
                                              "public class FixtureMarker : DependencyBase { }");
            var borrowingPluginDirectory = CreateDirectory();
            File.Copy(marked, Path.Combine(borrowingPluginDirectory, $"{fixtureName}.dll"));
            var owningPlugin = new PluginLoadContext(_pluginDirectory, PackageId, _logger);
            owningPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));
            var borrowingPlugin = new PluginLoadContext(borrowingPluginDirectory, PackageId, _logger);

            // Act
            var resolved = borrowingPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));
            var dependencyOfResolved = resolved.GetType("FixtureMarker")!.BaseType!.Assembly;

            // Assert
            Assert.AreSame(owningPlugin, AssemblyLoadContext.GetLoadContext(resolved), "The plugin that bound it first owns it.");
            Assert.AreSame(owningPlugin,
                           AssemblyLoadContext.GetLoadContext(dependencyOfResolved),
                           "The borrowing plugin ships no copy of the dependency; it resolves from the owner's directory, which is the consequence of that ownership.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.3")]
        public void LoadPrivateCopyForUnmarkedAssemblyDespiteSharedInstance()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, fixtureName);
            var sharedInstance = new PluginLoadContext(_pluginDirectory, PackageId, _logger).LoadFromAssemblyName(new AssemblyName(fixtureName));
            var unmarkedPluginDirectory = CreateDirectory();
            FixtureAssembly.Emit(unmarkedPluginDirectory, fixtureName);
            var unmarkedPlugin = new PluginLoadContext(unmarkedPluginDirectory, PackageId, _logger);

            // Act
            var resolved = unmarkedPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreNotSame(sharedInstance, resolved, "A plugin that never opted into sharing must not be handed another plugin's assembly.");
            Assert.AreSame(unmarkedPlugin, AssemblyLoadContext.GetLoadContext(resolved));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.4")]
        public void RecogniseMarkerOnAssemblyBuiltAgainstAnotherSdkVersion()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, new Version(HostSdkMajor, 9, 9, 0));
            EmitBuiltAgainstSdk(_pluginDirectory, fixtureName, standInSdk, true);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            CollectionAssert.Contains(PluginLoadContext.GetLoadedSharedExtensionAssemblies().ToList(),
                                      resolved,
                                      "The marker is matched by name and namespace, so the SDK it was built against does not matter.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.4")]
        public void IgnoreMarkerOfSameNameFromAnotherNamespace()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Private");
            var markerLibrary = FixtureAssembly.EmitStandInMarkerLibrary(_stagingDirectory, FixtureAssembly.UniqueName("Marker"), "Acme.Sharing");
            FixtureAssembly.Emit(_pluginDirectory, fixtureName, new[] { markerLibrary }, new[] { "[assembly: Acme.Sharing.DaleSharedAssembly]" });
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(sut, AssemblyLoadContext.GetLoadContext(resolved), "Only Vion's own attribute opts an assembly into sharing — the type name alone is not the marker.");
            CollectionAssert.DoesNotContain(SharedExtensionNames(), fixtureName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.8")]
        public void IgnoreMarkerAnAssemblyDeclaresItself()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Private");
            FixtureAssembly.Emit(_pluginDirectory,
                                 fixtureName,
                                 attributes: new[] { "[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]" },
                                 body: "namespace Vion.Dale.Sdk.Core { public sealed class DaleSharedAssemblyAttribute : System.Attribute { } }");
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(sut, AssemblyLoadContext.GetLoadContext(resolved), "Declaring the attribute is not applying Vion's — an assembly cannot opt itself into sharing.");
            CollectionAssert.DoesNotContain(SharedExtensionNames(), fixtureName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.6")]
        public async Task LoadSharedExtensionOnceUnderConcurrentBinding()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            var marked = EmitMarked(_pluginDirectory, fixtureName);
            var secondPluginDirectory = CreateDirectory();
            File.Copy(marked, Path.Combine(secondPluginDirectory, $"{fixtureName}.dll"));
            var firstPlugin = new PluginLoadContext(_pluginDirectory, PackageId, _logger);
            var secondPlugin = new PluginLoadContext(secondPluginDirectory, PackageId, _logger);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstBinding = BindWhenReleased(firstPlugin);
            var secondBinding = BindWhenReleased(secondPlugin);

            // Act
            release.SetResult();
            var bindings = await Task.WhenAll(firstBinding, secondBinding).WaitAsync(BindTimeout);

            // Assert
            Assert.AreSame(bindings[0], bindings[1]);
            return;

            Task<Assembly> BindWhenReleased(PluginLoadContext context)
            {
                return Task.Run(async () =>
                                {
                                    await release.Task.WaitAsync(BindTimeout);
                                    return context.LoadFromAssemblyName(new AssemblyName(fixtureName));
                                });
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-006.1")]
        public void LoadPrivateCopyOfUnmarkedAssemblyForEveryPlugin()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Private");
            var unmarked = FixtureAssembly.Emit(_pluginDirectory, fixtureName);
            var secondPluginDirectory = CreateDirectory();
            File.Copy(unmarked, Path.Combine(secondPluginDirectory, $"{fixtureName}.dll"));
            var firstPlugin = new PluginLoadContext(_pluginDirectory, PackageId, _logger);
            var secondPlugin = new PluginLoadContext(secondPluginDirectory, PackageId, _logger);

            // Act
            var firstResolved = firstPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));
            var secondResolved = secondPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreNotSame(firstResolved, secondResolved);
            Assert.AreSame(firstPlugin, AssemblyLoadContext.GetLoadContext(firstResolved));
            Assert.AreSame(secondPlugin, AssemblyLoadContext.GetLoadContext(secondResolved));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-006.2")]
        public void ResolveAssemblyMissingFromPluginDirectoryThroughHost()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Absent");
            var hostInstance = LoadIntoHost(FixtureAssembly.Emit(_stagingDirectory, fixtureName));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            var resolved = sut.LoadFromAssemblyName(new AssemblyName(fixtureName));

            // Assert
            Assert.AreSame(hostInstance, resolved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.1")]
        public void LoadEveryMarkedAssemblyInPluginDirectory()
        {
            // Arrange
            var fixtureName1 = FixtureAssembly.UniqueName("Shared");
            var fixtureName2 = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, fixtureName1);
            EmitMarked(_pluginDirectory, fixtureName2);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            var sharedNames = SharedExtensionNames();
            CollectionAssert.Contains(sharedNames, fixtureName1);
            CollectionAssert.Contains(sharedNames, fixtureName2);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.1")]
        public void LeaveUnmarkedAssembliesOutOfSharedRegistryDuringEagerLoad()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Private");
            FixtureAssembly.Emit(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            CollectionAssert.DoesNotContain(SharedExtensionNames(), fixtureName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-005.5")]
        public void TreatUnreadableFileAsUnmarkedDuringEagerLoad()
        {
            // A corrupt dll beside a plugin is a fact of deployment, not a sharing decision: the
            // marker read fails, the file is passed over, and the marked assembly beside it loads.
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            FixtureAssembly.EmitUnreadable(_pluginDirectory, FixtureAssembly.UniqueName("Corrupt"));
            EmitMarked(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            CollectionAssert.Contains(SharedExtensionNames(), fixtureName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.7")]
        public void FailEagerLoadWhenFileNameDiffersFromAssemblyName()
        {
            // Arrange
            EmitMarked(_pluginDirectory, FixtureAssembly.UniqueName("Actual"), FixtureAssembly.UniqueName("Shared"));
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act / Assert
            Assert.ThrowsExactly<FileLoadException>(() => sut.EagerlyLoadSharedExtensions());
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.6")]
        public void KeepRegisteredSharedInstancesWhenEagerLoadFails()
        {
            // Arrange
            var registeredName = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, registeredName);
            new PluginLoadContext(_pluginDirectory, PackageId, _logger).EagerlyLoadSharedExtensions();
            var brokenPluginDirectory = CreateDirectory();
            EmitMarked(brokenPluginDirectory, FixtureAssembly.UniqueName("Actual"), FixtureAssembly.UniqueName("Shared"));
            var brokenPlugin = new PluginLoadContext(brokenPluginDirectory, PackageId, _logger);

            // Act
            Assert.ThrowsExactly<FileLoadException>(() => brokenPlugin.EagerlyLoadSharedExtensions());

            // Assert
            CollectionAssert.Contains(SharedExtensionNames(), registeredName, "A plugin that fails halfway does not un-register what other plugins already share.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.4")]
        public void CompleteEagerLoadWhenPluginDirectoryMissing()
        {
            // Arrange
            var missingDirectory = Path.Combine(Path.GetTempPath(), $"dale-missing-{Guid.NewGuid():N}");
            var sut = new PluginLoadContext(missingDirectory, PackageId, _logger);
            var sharedBefore = PluginLoadContext.GetLoadedSharedExtensionAssemblies().ToList();

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            CollectionAssert.AreEquivalent(sharedBefore,
                                           PluginLoadContext.GetLoadedSharedExtensionAssemblies().ToList(),
                                           "Completing the call is the behaviour; nothing was loaded from a directory that does not exist.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.5")]
        public void RegisterMarkedAssemblyAlreadyLoadedByPath()
        {
            // The LogicBlockParser loads the plugin dll by path and only then eagerly loads shared
            // extensions, so a marked plugin assembly reaches this method already loaded.
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            var marked = EmitMarked(_pluginDirectory, fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);
            var loadedByPath = sut.LoadFromAssemblyPath(marked);

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            CollectionAssert.Contains(PluginLoadContext.GetLoadedSharedExtensionAssemblies().ToList(), loadedByPath);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-007.3")]
        public void ExposeEverySharedExtensionLoadedSoFar()
        {
            // Arrange
            var fixtureName1 = FixtureAssembly.UniqueName("Shared");
            var fixtureName2 = FixtureAssembly.UniqueName("Shared");
            EmitMarked(_pluginDirectory, fixtureName1);
            var secondPluginDirectory = CreateDirectory();
            EmitMarked(secondPluginDirectory, fixtureName2);
            var firstPlugin = new PluginLoadContext(_pluginDirectory, PackageId, _logger);
            var secondPlugin = new PluginLoadContext(secondPluginDirectory, PackageId, _logger);

            // Act
            firstPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName1));
            secondPlugin.LoadFromAssemblyName(new AssemblyName(fixtureName2));

            // Assert
            var sharedNames = SharedExtensionNames();
            CollectionAssert.Contains(sharedNames, fixtureName1);
            CollectionAssert.Contains(sharedNames, fixtureName2, "The registry spans contexts — it is what the runtime reads to reach every shared library.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-008.1")]
        public void IgnoreSubdirectoriesInVersionCheck()
        {
            // Arrange
            var standInSdk = FixtureAssembly.EmitStandInSdk(_stagingDirectory, new Version(HostSdkMajor + 1, 0, 0, 0));
            EmitBuiltAgainstSdk(CreateSubdirectory(_pluginDirectory), FixtureAssembly.UniqueName("Fixture"), standInSdk);

            // Act
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Assert
            CollectionAssert.Contains(AssemblyLoadContext.All.ToList(), sut, "An incompatible assembly one level down is not part of the plugin and does not reject it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-008.2")]
        public void IgnoreSubdirectoriesDuringEagerLoad()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Shared");
            EmitMarked(CreateSubdirectory(_pluginDirectory), fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act
            sut.EagerlyLoadSharedExtensions();

            // Assert
            CollectionAssert.DoesNotContain(SharedExtensionNames(), fixtureName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-008.3")]
        public void TreatAssemblyOnlyInSubdirectoryAsAbsent()
        {
            // Arrange
            var fixtureName = FixtureAssembly.UniqueName("Buried");
            FixtureAssembly.Emit(CreateSubdirectory(_pluginDirectory), fixtureName);
            var sut = new PluginLoadContext(_pluginDirectory, PackageId, _logger);

            // Act / Assert
            Assert.ThrowsExactly<FileNotFoundException>(() => sut.LoadFromAssemblyName(new AssemblyName(fixtureName)));
        }

        /// <summary>
        ///     Emits a fixture built against <paramref name="standInSdkPath" />. The fixture derives
        ///     from the stand-in's anchor type so the compiler keeps the assembly reference — the
        ///     reference row is what the version gate reads, and a fixture that merely names the
        ///     stand-in on the command line carries none.
        /// </summary>
        private static string EmitBuiltAgainstSdk(string directory, string simpleName, string standInSdkPath, bool marked = false)
        {
            return FixtureAssembly.Emit(directory,
                                        simpleName,
                                        new[] { standInSdkPath },
                                        marked ? new[] { "[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]" } : null,
                                        $"public class FixtureMarker : {FixtureAssembly.StandInSdkAnchorType} {{ }}");
        }

        /// <summary>Emits a fixture carrying <c>[DaleSharedAssembly]</c> from the host's own SDK.</summary>
        private static string EmitMarked(string directory, string simpleName, string? fileName = null)
        {
            return FixtureAssembly.Emit(directory,
                                        simpleName,
                                        new[] { Path.Combine(AppContext.BaseDirectory, $"{SdkAssemblyName}.dll") },
                                        new[] { "[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]" },
                                        fileName: fileName);
        }

        private static Assembly LoadIntoHost(string assemblyPath)
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }

        private static bool LoadedAnywhere(string simpleName)
        {
            return AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Any(assembly => assembly.GetName().Name == simpleName);
        }

        private static List<string?> SharedExtensionNames()
        {
            return PluginLoadContext.GetLoadedSharedExtensionAssemblies().Select(assembly => assembly.GetName().Name).ToList();
        }

        private static string CreateSubdirectory(string parent)
        {
            var path = Path.Combine(parent, "runtimes");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>A temp directory removed, as far as the loaded fixtures allow, when the test ends.</summary>
        private string CreateDirectory()
        {
            var path = FixtureAssembly.CreateDirectory();
            _directories.Add(path);
            return path;
        }
    }
}