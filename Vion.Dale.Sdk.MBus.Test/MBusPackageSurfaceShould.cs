using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     What the package ships, read off the assembly rather than off a list: every public type is either
    ///     published surface or plumbing that is public only because another assembly of this build reaches it, and
    ///     the assembly is shared across plugins because its types travel in actor messages between them.
    ///     <para>
    ///         The analyzer that enforces the classification at compile time is proven in
    ///         <c>Vion.Dale.Sdk.Generators.Test.AnalyzerWiringShould</c>, which builds this project with a
    ///         deliberately unmarked type linked in and requires the diagnostic.
    ///     </para>
    /// </summary>
    [TestClass]
    public class MBusPackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(IMBus).Assembly;

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-009.1")]
        public void ClassifyEveryPublicTypeAsSurfaceOrPlumbing()
        {
            // Arrange
            // Nested types are included rather than filtered out: DALE014 judges a public type nested in a
            // public one exactly as it judges a top-level one.
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
        [TestProperty("spec", "AC-MBUS-005.7")]
        public void MarkAssemblyAsSharedAcrossPlugins()
        {
            // Arrange
            // The plugin loader gives every plugin its own copy of an unmarked assembly. The M-Bus actor messages
            // cross the plugin boundary between a block and the one handler of the runtime, so a copy per plugin
            // gives their types one identity per plugin and the handler matches none of them.

            // Act
            var isMarked = Package.GetCustomAttributes(typeof(DaleSharedAssemblyAttribute), false).Any();

            // Assert
            Assert.IsTrue(isMarked, $"{Package.GetName().Name} declares the types its actor messages carry and must be shared across plugins.");
        }
    }
}