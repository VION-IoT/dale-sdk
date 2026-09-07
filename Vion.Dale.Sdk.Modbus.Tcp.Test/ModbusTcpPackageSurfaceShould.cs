using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test
{
    /// <summary>
    ///     What the package ships, read off the assembly rather than off a list: which of its public types are
    ///     published surface, and which are plumbing that is public only because another assembly of this build
    ///     reaches it. Rosters drift; these derive both sides and compare them, so a type added without a mark
    ///     fails here rather than at a consumer.
    ///     <para>
    ///         The analyzer that enforces the same rule at compile time is proven in
    ///         <c>Vion.Dale.Sdk.Generators.Test.AnalyzerWiringShould</c>, which builds this project with a
    ///         deliberately unmarked type linked in and requires the diagnostic. No DALE diagnostic judged a
    ///         declaration in this package before that reference landed, although it had declared five
    ///         published namespaces since it shipped.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ModbusTcpPackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(ILogicBlockModbusTcpClient).Assembly;

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.1")]
        public void ClassifyEveryPublicTypeAsSurfaceOrPlumbing()
        {
            // Arrange
            // Nested types are included rather than filtered out: DALE014 judges a public type nested in a
            // public one exactly as it judges a top-level one, so a test that skipped them would pass on a
            // declaration the build still reports.
            var exported = Package.GetExportedTypes();

            // Act
            var unmarked = exported.Where(type => type.GetCustomAttribute<PublicApiAttribute>() == null && type.GetCustomAttribute<InternalApiAttribute>() == null)
                                   .Select(type => type.FullName!)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.IsNotEmpty(exported, $"{Package.GetName().Name} exports no public type at all, so an empty unmarked list proves nothing.");
            Assert.IsEmpty(unmarked,
                           $"Every public type this package ships is published surface or is marked internal plumbing; {exported.Length} classified, " +
                           $"{unmarked.Count} unmarked: {string.Join(", ", unmarked)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-019.1")]
        public void PublishOnlyWhatBlockAuthorNames()
        {
            // Arrange
            // The client and server surfaces a block injects, the registration it calls, the queue policy and
            // drop reason it configures and reads, the connection diagnostics it publishes, and the four
            // exceptions its error callback receives by name. The proxies, the wrapper, the request seam and the
            // concrete clients and factories are public only so the TestKit's fakes and the container's activator
            // can reach them, and the TestKit hands out its own concrete types rather than these interfaces.

            // Act
            var published = Package.GetExportedTypes()
                                   .Where(type => type.GetCustomAttribute<PublicApiAttribute>() != null)
                                   .Select(type => type.Name)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.AreEqual("ConnectionTimeoutException,ILogicBlockModbusTcpClient,ILogicBlockModbusTcpClientFactory,ILogicBlockModbusTcpServer," +
                            "ILogicBlockModbusTcpServerFactory,IpAddressNotSetException,LinkBackoffException,ModbusTcpConnectionState," +
                            "ModbusTcpConnectionSummary,QueueOverflowPolicy,RequestDropReason,RequestDroppedException,ServiceCollectionExtensions",
                            string.Join(",", published));
        }
    }
}