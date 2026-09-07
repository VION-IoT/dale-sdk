using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Rtu.Test
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
    ///         declaration in this package before that reference landed.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ModbusRtuPackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(IModbusRtu).Assembly;

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
            // Four types, and only four: the contract interface a block declares, the two exceptions its error
            // callback receives by name, and the IConfigureServices implementation the SDK's own DevHost example
            // constructs by hand. The RTU actor messages, the handler and the concrete contract are the runtime's
            // to send, register and bind - a block names none of them, and their cross-plugin identity is
            // [DaleSharedAssembly]'s subject rather than the manifest's.

            // Act
            var published = Package.GetExportedTypes()
                                   .Where(type => type.GetCustomAttribute<PublicApiAttribute>() != null)
                                   .Select(type => type.Name)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.AreEqual("DependencyInjection,IModbusRtu,PendingRequestsLimitReachedException,ServiceProviderContractMappingNotFoundException", string.Join(",", published));
        }
    }
}