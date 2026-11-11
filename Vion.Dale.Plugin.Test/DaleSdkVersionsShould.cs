using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Vion.Contracts.Conventions;
using Vion.Dale.Plugin.Test.TestHelpers;

namespace Vion.Dale.Plugin.Test
{
    /// <summary>
    ///     A library's minimum Dale SDK: docs/specs/plugin-loading.md § The library's minimum Dale SDK.
    ///     Tests mirror the SUT — reading a library's versions, then comparing them with the host's.
    /// </summary>
    /// <remarks>
    ///     Fixtures are compiled onto disk, as in <see cref="PluginLoadContextShould" />: the versions are
    ///     read from assembly references and assembly metadata, so only a real assembly carries them. A
    ///     fixture built against a stand-in SDK is what gives a library a known target, because every
    ///     assembly this repository builds references a <c>0.0.0</c> SDK.
    /// </remarks>
    [TestClass]
    public class DaleSdkVersionsShould
    {
        private readonly List<string> _directories = new();

        private string _pluginDirectory = null!;

        public static IEnumerable<object?[]> MetOrUnknownMinimums
        {
            get
            {
                // Met: equal, below, and equal but for the fourth part, which is never compared.
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(0, 15, 2), null), new Version(0, 15, 2, 0) };
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(0, 14, 0), null), new Version(0, 15, 2, 0) };
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(0, 15, 2, 7), null), new Version(0, 15, 2, 0) };

                // Met by a declared minimum below a target the host does not reach: the declaration wins.
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(0, 16, 0), new Version(0, 14, 3)), new Version(0, 15, 2, 0) };

                // Unknown on either side.
                yield return new object?[] { new LibraryDaleSdkVersions(null, null), new Version(0, 15, 2, 0) };
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(0, 16, 0), null), null };

                // A differing major is the major gate's refusal, not an unmet minimum.
                yield return new object?[] { new LibraryDaleSdkVersions(new Version(1, 2, 0), null), new Version(0, 15, 2, 0) };
            }
        }

        [TestInitialize]
        public void Initialize()
        {
            _pluginDirectory = CreateDirectory();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Best-effort, as in PluginLoadContextShould: these tests load nothing, but a directory the
            // runtime still holds open is left rather than failing the test.
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
        [TestProperty("spec", "AC-PLUG-009.1")]
        [DataRow("FixtureA", "FixtureB", DisplayName = "higher version in the file listed last")]
        [DataRow("FixtureB", "FixtureA", DisplayName = "higher version in the file listed first")]
        public void ReportHighestSdkReferenceAsTarget(string lowerName, string higherName)
        {
            // Arrange
            EmitBuiltAgainstSdk(lowerName, new Version(0, 14, 3, 0));
            EmitBuiltAgainstSdk(higherName, new Version(0, 15, 1, 7));
            var main = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Main"));

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.AreEqual(new Version(0, 15, 1), library.Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.1")]
        public void ReportNoTargetWhenNoAssemblyReferencesSdk()
        {
            // Arrange
            FixtureAssembly.EmitUnreadable(_pluginDirectory, FixtureAssembly.UniqueName("Corrupt"));
            var main = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Main"));

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.1")]
        public void ReportNoTargetWhenPluginDirectoryMissing()
        {
            // Arrange
            var missingDirectory = Path.Combine(Path.GetTempPath(), $"dale-missing-{Guid.NewGuid():N}");

            // Act
            var library = DaleSdkVersions.ReadLibrary(missingDirectory, Path.Combine(missingDirectory, "Main.dll"));

            // Assert
            Assert.AreEqual(new LibraryDaleSdkVersions(null, null), library);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.1")]
        [TestProperty("spec", "AC-PLUG-009.2")]
        public void ReadLibraryWithoutLoadingAnyAssembly()
        {
            // Arrange
            var dependencyName = FixtureAssembly.UniqueName("Dependency");
            EmitBuiltAgainstSdk(dependencyName, new Version(0, 15, 0, 0));
            var mainName = FixtureAssembly.UniqueName("Main");
            var main = EmitDeclaring(mainName, "0.14.3");

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.AreEqual(new LibraryDaleSdkVersions(new Version(0, 15, 0), new Version(0, 14, 3)), library, "The fixture has to carry both versions, or nothing was read.");
            Assert.IsFalse(LoadedAnywhere(dependencyName), "Reading the target must not put an assembly into any load context.");
            Assert.IsFalse(LoadedAnywhere(mainName), "Reading the declared minimum must not put the assembly into any load context.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.2")]
        [DataRow("0.14.3")]
        [DataRow("1.2.0")]
        public void ReportDeclaredMinimumOfMainAssembly(string declared)
        {
            // Arrange
            var main = EmitDeclaring(FixtureAssembly.UniqueName("Main"), declared);

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.AreEqual(Version.Parse(declared), library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.2")]
        [DataRow("0.14", DisplayName = "two parts")]
        [DataRow("0.14.3.0", DisplayName = "four parts")]
        [DataRow("0.14.3-rc.1", DisplayName = "prerelease")]
        [DataRow("v0.14.3", DisplayName = "prefixed")]
        [DataRow("", DisplayName = "empty")]
        public void ReportNoDeclaredMinimumWhenValueMalformed(string declared)
        {
            // Arrange
            var main = EmitDeclaring(FixtureAssembly.UniqueName("Main"), declared);

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.2")]
        [DataRow(null, DisplayName = "no assembly metadata")]
        [DataRow("[assembly: System.Reflection.AssemblyMetadata(\"RepositoryUrl\", \"0.14.3\")]", DisplayName = "another metadata key")]
        public void ReportNoDeclaredMinimumWhenAbsent(string? attribute)
        {
            // Arrange
            var main = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Main"), attributes: attribute == null ? null : new[] { attribute });

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.2")]
        public void ReportNoDeclaredMinimumFromUnreadableAssembly()
        {
            // Arrange
            var main = FixtureAssembly.EmitUnreadable(_pluginDirectory, FixtureAssembly.UniqueName("Main"));

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.2")]
        public void ReadDeclaredMinimumOnlyFromMainAssembly()
        {
            // Arrange
            EmitDeclaring(FixtureAssembly.UniqueName("Dependency"), "0.14.3");
            var main = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Main"));

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.3")]
        public void ReportZeroTargetAsUnknown()
        {
            // Arrange
            EmitBuiltAgainstSdk(FixtureAssembly.UniqueName("Fixture"), new Version(0, 0, 0, 0));
            var main = FixtureAssembly.Emit(_pluginDirectory, FixtureAssembly.UniqueName("Main"));

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.3")]
        public void ReportZeroDeclaredMinimumAsUnknown()
        {
            // Arrange
            var main = EmitDeclaring(FixtureAssembly.UniqueName("Main"), "0.0.0");

            // Act
            var library = DaleSdkVersions.ReadLibrary(_pluginDirectory, main);

            // Assert
            Assert.IsNull(library.DeclaredMinimum);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.3")]
        [DataRow(0, DisplayName = "0.0.0.0")]
        [DataRow(9, DisplayName = "0.0.0.9")]
        public void TreatZeroHostAsUnknown(int hostRevision)
        {
            // Arrange
            var library = new LibraryDaleSdkVersions(null, new Version(0, 14, 3));

            // Act
            var unmet = DaleSdkVersions.Compare(library, new Version(0, 0, 0, hostRevision));

            // Assert
            Assert.IsNull(unmet);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.4")]
        public void ReportUnmetDeclaredMinimum()
        {
            // Arrange — the target alone is met, so only the declared minimum can be unmet
            var library = new LibraryDaleSdkVersions(new Version(0, 14, 0), new Version(0, 16, 0));

            // Act
            var unmet = DaleSdkVersions.Compare(library, new Version(0, 15, 2, 0));

            // Assert
            Assert.AreEqual(new UnmetDaleSdkMinimum(new Version(0, 16, 0), true, new Version(0, 15, 2)), unmet);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.4")]
        [DataRow(0,
                 16,
                 0,
                 0,
                 15,
                 2)]
        [DataRow(1,
                 4,
                 3,
                 1,
                 4,
                 2)]
        public void ReportUnmetTargetWhenNoneDeclared(int targetMajor,
                                                      int targetMinor,
                                                      int targetPatch,
                                                      int hostMajor,
                                                      int hostMinor,
                                                      int hostPatch)
        {
            // Arrange
            var library = new LibraryDaleSdkVersions(new Version(targetMajor, targetMinor, targetPatch), null);

            // Act
            var unmet = DaleSdkVersions.Compare(library, new Version(hostMajor, hostMinor, hostPatch, 0));

            // Assert
            Assert.AreEqual(new UnmetDaleSdkMinimum(new Version(targetMajor, targetMinor, targetPatch), false, new Version(hostMajor, hostMinor, hostPatch)), unmet);
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-009.5")]
        [DynamicData(nameof(MetOrUnknownMinimums))]
        public void ReportNoUnmetMinimum(LibraryDaleSdkVersions library, Version? host)
        {
            // Arrange
            // Act
            var unmet = DaleSdkVersions.Compare(library, host);

            // Assert
            Assert.IsNull(unmet);
        }

        private void EmitBuiltAgainstSdk(string simpleName, Version sdkVersion)
        {
            var standInSdk = FixtureAssembly.EmitStandInSdk(CreateDirectory(), sdkVersion);
            FixtureAssembly.Emit(_pluginDirectory, simpleName, new[] { standInSdk }, body: $"public class FixtureMarker : {FixtureAssembly.StandInSdkAnchorType} {{ }}");
        }

        private string EmitDeclaring(string simpleName, string declared)
        {
            return FixtureAssembly.Emit(_pluginDirectory,
                                        simpleName,
                                        attributes: new[]
                                                    {
                                                        $"[assembly: System.Reflection.AssemblyMetadata(\"{DaleSdkCompatibilityConventions.MinimumDaleSdkVersionAnnotationKey}\", \"{declared}\")]",
                                                    });
        }

        private static bool LoadedAnywhere(string simpleName)
        {
            return AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Any(assembly => assembly.GetName().Name == simpleName);
        }

        private string CreateDirectory()
        {
            var path = FixtureAssembly.CreateDirectory();
            _directories.Add(path);
            return path;
        }
    }
}