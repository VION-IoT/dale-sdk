using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     What the five kits ship, read off the assemblies and the repository rather than off a list.
    ///     Rosters drift; these tests derive both sides and compare them, so a kit added or a type left
    ///     unmarked fails here rather than at a consumer.
    /// </summary>
    [TestClass]
    public class TestKitSurfaceShould
    {
        /// <summary>The five packable kits, by the assembly each ships.</summary>
        private static readonly string[] KitAssemblies =
        [
            "Vion.Dale.Sdk.TestKit",
            "Vion.Dale.Sdk.DigitalIo.TestKit",
            "Vion.Dale.Sdk.AnalogIo.TestKit",
            "Vion.Dale.Sdk.Modbus.Rtu.TestKit",
            "Vion.Dale.Sdk.Modbus.Tcp.TestKit",
        ];

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.1")]
        [DataRow("Vion.Dale.Sdk.TestKit")]
        [DataRow("Vion.Dale.Sdk.DigitalIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.AnalogIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp.TestKit")]
        public void ClassifyEveryPublicTypeAsPublishedSurface(string assemblyName)
        {
            // Arrange
            var assembly = LoadKit(assemblyName);

            // Act
            var unmarked = assembly.GetExportedTypes()
                                   .Where(type => !type.IsNested)
                                   .Where(type => type.GetCustomAttribute<PublicApiAttribute>() == null && type.GetCustomAttribute<InternalApiAttribute>() == null)
                                   .Select(type => type.FullName)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.IsEmpty(unmarked, $"Every public type a kit ships is published surface or is marked internal: {string.Join(", ", unmarked)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.1")]
        [DataRow("Vion.Dale.Sdk.TestKit")]
        [DataRow("Vion.Dale.Sdk.DigitalIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.AnalogIo.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu.TestKit")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp.TestKit")]
        public void DeclareNoAssertionOrTestFramework(string assemblyName)
        {
            // Arrange — a kit that pinned a test framework would decide its consumer's, and the nine
            // downstream suites this repository ships are xunit while every suite inside it is MSTest
            var assembly = LoadKit(assemblyName);

            // Act
            var frameworks = assembly.GetReferencedAssemblies()
                                     .Select(reference => reference.Name!)
                                     .Where(name => name.StartsWith("MSTest", StringComparison.Ordinal) || name.StartsWith("xunit", StringComparison.Ordinal) ||
                                                    name.StartsWith("NUnit", StringComparison.Ordinal))
                                     .ToList();

            // Assert
            Assert.IsEmpty(frameworks, $"{assemblyName} references a test framework: {string.Join(", ", frameworks)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.3")]
        public void CarryMockingLibraryAndControllableClockInPublishedSignature()
        {
            // Arrange — both reach a consumer's own code, so both are part of what a kit upgrade brings
            var verify = typeof(LogicBlockTestContext<>).GetMethod(nameof(LogicBlockTestContext<SampleLogicBlock>.VerifyServicePropertyChanged));

            // Act / Assert
            Assert.IsNotNull(verify);
            Assert.Contains(typeof(Times?), verify!.GetParameters().Select(parameter => parameter.ParameterType).ToList());
            Assert.AreEqual(typeof(FakeTimeProvider), typeof(LogicBlockTestContext<>).GetProperty(nameof(LogicBlockTestContext<SampleLogicBlock>.TimeProvider))!.PropertyType);
            Assert.AreEqual(typeof(Mock<Microsoft.Extensions.Logging.ILogger>), typeof(LogicBlockTestHelper).GetMethod(nameof(LogicBlockTestHelper.CreateLoggerMock))!.ReturnType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.1")]
        public void CarryEveryPublishedKitTypeInApiManifest()
        {
            // Arrange
            var manifest =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "snapshots", "publicapi-manifest.json")))!;
            var recorded = manifest["types"].EnumerateArray().Select(entry => entry.GetString()!).ToHashSet(StringComparer.Ordinal);

            // Act
            var shipped = KitAssemblies.SelectMany(name => LoadKit(name).GetExportedTypes())
                                       .Where(type => !type.IsNested && type.GetCustomAttribute<PublicApiAttribute>() != null)
                                       .Select(type => StripArity(type.FullName!))
                                       .ToList();

            // Assert
            Assert.IsNotEmpty(shipped);
            Assert.IsEmpty(shipped.Where(name => !recorded.Contains(name)).ToList(),
                           "Every published kit type takes a row in the API manifest, which is what makes a surface change visible in review.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-013.4")]
        public void NameEveryPackableProjectInReleaseCacheRoster()
        {
            // Arrange — a package the roster does not name keeps its previous version in the local cache
            // after a release, so the next local build of a consumer resolves the old one
            var root = RepositoryRoot();
            var roster = Regex.Match(File.ReadAllText(Path.Combine(root, "scripts", "set-version.ps1")), @"\$sdkPackageIds\s*=\s*@\((?<body>[^)]*)\)");
            Assert.IsTrue(roster.Success, "set-version.ps1 declares no $sdkPackageIds roster.");
            var listed = Regex.Matches(roster.Groups["body"].Value, "\"([^\"]+)\"").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

            // Act
            var packable = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                                    .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                                   !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                                    .Select(File.ReadAllText)
                                    .Where(text => Regex.IsMatch(text, @"<IsPackable>\s*true\s*</IsPackable>"))
                                    .Select(text => Regex.Match(text, "<PackageId>([^<]+)</PackageId>"))
                                    .Where(match => match.Success)
                                    .Select(match => match.Groups[1].Value)
                                    .ToList();

            // Assert
            Assert.IsNotEmpty(packable);
            Assert.IsEmpty(packable.Where(id => !listed.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.1")]
        [DataRow("Vion.Dale.Sdk.TestKit.Test")]
        [DataRow("Vion.Dale.Sdk.DigitalIo.TestKit.Test")]
        [DataRow("Vion.Dale.Sdk.AnalogIo.TestKit.Test")]
        [DataRow("Vion.Dale.Sdk.Modbus.Rtu.TestKit.Test")]
        [DataRow("Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test")]
        public void ReachNoRuntimeBrokerDeviceOrDevelopmentHostFromKitSuite(string projectName)
        {
            // Arrange — the kits are testable without any of them, and a suite that reached one would be
            // testing that thing instead
            var project = File.ReadAllText(Path.Combine(RepositoryRoot(), projectName, projectName + ".csproj"));

            // Act
            var forbidden = new[] { "Vion.Dale.DevHost", "Vion.Dale.ProtoActor", "Vion.Dale.Plugin", "MQTTnet" }
                            .Where(reference => project.Contains(reference, StringComparison.Ordinal))
                            .ToList();

            // Assert
            Assert.IsEmpty(forbidden, $"{projectName} references {string.Join(", ", forbidden)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-014.2")]
        public void LeaveSuiteProvingAnotherAreaCitedToThatArea()
        {
            // Arrange — 63 of the tests in these five projects prove emission, gating, lifecycle and
            // Modbus criteria; they stay where they are and keep citing the page that owns them
            var root = RepositoryRoot();

            // Act
            var foreignCitations = new[] { "Vion.Dale.Sdk.TestKit.Test", "Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test" }
                                   .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
                                   .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                                  !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                                   .SelectMany(path => Regex.Matches(File.ReadAllText(path), "\"(AC-(?:EMIT|GATE|LIFE|MODB)-[0-9.]+)\"").Select(match => match.Groups[1].Value))
                                   .Distinct()
                                   .ToList();

            // Assert
            Assert.IsNotEmpty(foreignCitations, "A suite in these projects proves other areas' criteria and must keep citing them.");
        }

        // A generic type's FullName carries its arity (`Name`1`); the manifest records the plain name.
        private static string StripArity(string fullName)
        {
            var backtick = fullName.IndexOf('`');
            return backtick < 0 ? fullName : fullName[..backtick];
        }

        private static Assembly LoadKit(string assemblyName)
        {
            return Assembly.Load(assemblyName);
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Vion.Dale.Sdk.sln")))
            {
                directory = directory.Parent;
            }

            Assert.IsNotNull(directory, "Could not locate the repository root.");
            return directory.FullName;
        }
    }
}