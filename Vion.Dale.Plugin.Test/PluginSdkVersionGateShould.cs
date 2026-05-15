using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Plugin;

namespace Vion.Dale.Plugin.Test
{
    /// <summary>
    ///     Tests for item E (spec §E / decision 0022): plugin/SDK major-version fail-fast.
    ///     Exercises the pure <see cref="PluginLoadContext.EnsureSdkMajorCompatible" /> seam plus
    ///     the PE-metadata extraction of a plugin's referenced <c>Vion.Dale.Sdk</c> version.
    /// </summary>
    [TestClass]
    public class PluginSdkVersionGateShould
    {
        private const string PackageId = "Acme.Sample.Plugin";
        private const string SdkAssemblyName = "Vion.Dale.Sdk";

        [TestMethod]
        public void Throw_WithActionableMessage_WhenPluginReferencesDifferingMajor()
        {
            var host = new Version(1, 4, 0);
            var plugin = new Version(2, 0, 0);
            var logger = new RecordingLogger();

            var ex = Assert.ThrowsExactly<PluginSdkVersionMismatchException>(() =>
                PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName, host, plugin, logger));

            // Message must name the package id, BOTH versions, and the rebuild guidance.
            StringAssert.Contains(ex.Message, PackageId);
            StringAssert.Contains(ex.Message, "1.4.0");
            StringAssert.Contains(ex.Message, "2.0.0");
            StringAssert.Contains(ex.Message, SdkAssemblyName);
            StringAssert.Contains(ex.Message, "Rebuild the plugin");
            StringAssert.Contains(ex.Message, "compatible");

            // And it logged the failure at Error level before throwing.
            Assert.IsTrue(logger.LoggedAtLeastOneError, "Expected an Error log entry before the throw.");
        }

        [TestMethod]
        public void NotThrow_WhenSameMajorButDifferingMinorOrPatch()
        {
            // Same major, differing minor/patch must stay warn-and-continue: the seam itself
            // does NOT throw (the existing LogDefaultContextLoad path owns the warning).
            var logger = new RecordingLogger();

            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName,
                new Version(1, 4, 0), new Version(1, 2, 0), logger);

            // host older than plugin within same major — also no throw.
            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName,
                new Version(1, 2, 0), new Version(1, 9, 7), logger);

            Assert.IsFalse(logger.LoggedAtLeastOneError,
                "Same-major minor/patch skew must NOT raise an error in the seam (warn-and-continue is preserved).");
        }

        /// <summary>
        ///     ACCEPTED, DELIBERATE CONSEQUENCE — DO NOT "fix" this test by making it throw.
        ///     During 0.x the major is always 0, so the major-version gate is DORMANT pre-1.0.
        ///     The literal motivating skew 0.4.3 → 0.5.0 (spec §E "Consequence (accepted)" /
        ///     decision 0022) MUST remain a warning, not a hard fail, because major 0 == major 0.
        ///     This test pins that decision so it cannot silently regress.
        /// </summary>
        [TestMethod]
        public void NotThrow_ForThe0xMotivatingSkew_BecauseTheGateIsDormantPreV1()
        {
            var host = new Version(0, 5, 0);
            var plugin = new Version(0, 4, 3);
            var logger = new RecordingLogger();

            // Must NOT throw: 0.5.0 vs 0.4.3 share major 0.
            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName, host, plugin, logger);

            Assert.IsFalse(logger.LoggedAtLeastOneError,
                "0.x is pre-1.0 and the major gate is intentionally dormant; 0.4.3→0.5.0 stays a warning.");
        }

        [TestMethod]
        public void NotThrow_WhenEitherVersionIsNull()
        {
            var logger = new RecordingLogger();

            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName, null, new Version(2, 0, 0), logger);
            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName, new Version(1, 0, 0), null, logger);
            PluginLoadContext.EnsureSdkMajorCompatible(PackageId, SdkAssemblyName, null, null, logger);

            Assert.IsFalse(logger.LoggedAtLeastOneError);
        }

        [TestMethod]
        public void ExtractReferencedSdkVersion_FromARealAssemblyThatReferencesTheSdk()
        {
            // Vion.Dale.Plugin.dll has a ProjectReference to Vion.Dale.Sdk, so its compiled
            // assembly carries an AssemblyReference to "Vion.Dale.Sdk". Use it as a real fixture.
            var pluginAssemblyPath = typeof(PluginLoadContext).Assembly.Location;
            Assert.IsTrue(File.Exists(pluginAssemblyPath), $"Fixture assembly not found: {pluginAssemblyPath}");

            var version = PluginLoadContext.TryReadReferencedSdkVersion(pluginAssemblyPath, SdkAssemblyName);

            Assert.IsNotNull(version, "Expected to read a referenced Vion.Dale.Sdk version from the plugin assembly.");

            // Sanity-check it matches the SDK actually loaded in this test process.
            var loadedSdkVersion = typeof(Vion.Dale.Sdk.Core.LogicBlockBase).Assembly.GetName().Version;
            Assert.IsNotNull(loadedSdkVersion);
            Assert.AreEqual(loadedSdkVersion!.Major, version!.Major,
                "Referenced SDK major should match the SDK loaded in this process.");
        }

        [TestMethod]
        public void ReturnNull_WhenAssemblyDoesNotReferenceTheSdk()
        {
            // A BCL assembly that demonstrably does not reference Vion.Dale.Sdk.
            var bclPath = typeof(object).Assembly.Location;

            var version = PluginLoadContext.TryReadReferencedSdkVersion(bclPath, SdkAssemblyName);

            Assert.IsNull(version);
        }

        [TestMethod]
        public void ReturnNull_WhenFileIsNotAManagedAssembly()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"not-an-assembly-{Guid.NewGuid():N}.dll");
            File.WriteAllText(tempPath, "this is not a PE file");
            try
            {
                var version = PluginLoadContext.TryReadReferencedSdkVersion(tempPath, SdkAssemblyName);
                Assert.IsNull(version);
            }
            finally
            {
                File.Delete(tempPath);
            }
        }

        /// <summary>
        ///     Integration coverage for the <c>EnforceSdkMajorCompatibility</c> ORCHESTRATOR (run from
        ///     the constructor chokepoint), as opposed to the pure seam / extraction tested above. It
        ///     exercises the no-throw guard and continue paths only — the differing-major THROW is
        ///     already pinned by the seam test, and synthesizing a differing-major assembly is
        ///     intentionally out of scope.
        /// </summary>
        [TestMethod]
        public void NotThrow_WhenPluginPathMissingOrAllReferencedMajorsMatch()
        {
            var logger = new RecordingLogger();

            // 1) Non-existent plugin path: the !Directory.Exists guard returns early, no throw.
            var missingPath = Path.Combine(Path.GetTempPath(), $"dale-missing-{Guid.NewGuid():N}");
            Assert.IsFalse(Directory.Exists(missingPath));
            _ = new PluginLoadContext(missingPath, PackageId, logger);

            // 2) Temp dir containing a copy of the real Vion.Dale.Plugin.dll (references
            //    Vion.Dale.Sdk at the host's matching major) plus an unrelated non-SDK dll
            //    (Google.FlatBuffers.dll → TryReadReferencedSdkVersion returns null → continue).
            var binDir = Path.GetDirectoryName(typeof(PluginLoadContext).Assembly.Location)!;
            var realPluginDll = Path.Combine(binDir, "Vion.Dale.Plugin.dll");
            var unrelatedDll = Path.Combine(binDir, "Google.FlatBuffers.dll");
            Assert.IsTrue(File.Exists(realPluginDll), $"Fixture not found: {realPluginDll}");
            Assert.IsTrue(File.Exists(unrelatedDll), $"Fixture not found: {unrelatedDll}");

            var tempDir = Path.Combine(Path.GetTempPath(), $"dale-plugin-gate-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                File.Copy(realPluginDll, Path.Combine(tempDir, "Vion.Dale.Plugin.dll"));
                File.Copy(unrelatedDll, Path.Combine(tempDir, "Google.FlatBuffers.dll"));

                // Matching-major + null-continue paths: constructor must complete without throwing.
                _ = new PluginLoadContext(tempDir, PackageId, logger);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }

            Assert.IsFalse(logger.LoggedAtLeastOneError,
                "Missing-path guard and matching-major/null-continue paths must not raise an error.");
        }

        /// <summary>
        ///     Minimal recording logger test-double — captures whether any Error was logged so the
        ///     warn-vs-fail boundary can be asserted without a mocking framework.
        /// </summary>
        private sealed class RecordingLogger : ILogger
        {
            private readonly List<LogLevel> _levels = new();

            public bool LoggedAtLeastOneError => _levels.Contains(LogLevel.Error) || _levels.Contains(LogLevel.Critical);

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel,
                                     EventId eventId,
                                     TState state,
                                     Exception? exception,
                                     Func<TState, Exception?, string> formatter)
            {
                _levels.Add(logLevel);
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
