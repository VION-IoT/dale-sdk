using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What the package ships, read off the assembly rather than off a list: which of its public types
    ///     are published surface, what a plugin inherits by taking it, and the loading decision it makes by
    ///     not marking itself shared. Rosters drift; these derive both sides and compare them, so a type
    ///     added without a mark fails here rather than at a consumer.
    ///     <para>
    ///         The analyzer that enforces the same rule at compile time is proven in
    ///         <c>Vion.Dale.Sdk.Generators.Test.AnalyzerWiringShould</c>, which builds this project with a
    ///         deliberately unmarked type linked in and requires the diagnostic.
    ///     </para>
    /// </summary>
    [TestClass]
    public class HttpPackageSurfaceShould
    {
        private static readonly Assembly Package = typeof(ILogicBlockHttpClient).Assembly;

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-001.1")]
        public void ExposeNamedClientOnNoPublicMember()
        {
            // Arrange — the negative half of "under a name no public member exposes". Resolving under the
            // name is proven at the registration; this is the claim that a consumer cannot reach the name
            // from the published surface and must go through `configureClient` instead. The test reads the
            // name through InternalsVisibleTo, which is exactly the access a consumer does not have.

            // Act
            var exposing = Package.GetExportedTypes()
                                  .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                                  .Where(member => member.Name == HttpRequestExecutor.HttpClientName || ConstantValueOf(member) == HttpRequestExecutor.HttpClientName)
                                  .Select(member => $"{member.DeclaringType?.FullName}.{member.Name}")
                                  .OrderBy(name => name, StringComparer.Ordinal)
                                  .ToList();

            // Assert
            Assert.IsEmpty(exposing, $"No published member may name or hand out the client name: {string.Join(", ", exposing)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-013.1")]
        public void ClassifyEveryPublicTypeAsSurfaceOrPlumbing()
        {
            // Arrange

            // Act
            var unmarked = Package.GetExportedTypes()
                                  .Where(type => !type.IsNested)
                                  .Where(type => type.GetCustomAttribute<PublicApiAttribute>() == null && type.GetCustomAttribute<InternalApiAttribute>() == null)
                                  .Select(type => type.FullName)
                                  .OrderBy(name => name, StringComparer.Ordinal)
                                  .ToList();

            // Assert
            Assert.IsEmpty(unmarked, $"Every public type this package ships is published surface or is marked internal plumbing: {string.Join(", ", unmarked)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-013.1")]
        public void PublishOnlyWhatBlockAuthorCalls()
        {
            // Arrange — the marked set is what reaches the API manifest and the generated reference, so it
            // is the set a block author is told to use: the client they inject, the registration they call,
            // and the one exception they may want to catch by name

            // Act
            var published = Package.GetExportedTypes()
                                   .Where(type => type.GetCustomAttribute<PublicApiAttribute>() != null)
                                   .Select(type => type.Name)
                                   .OrderBy(name => name, StringComparer.Ordinal)
                                   .ToList();

            // Assert
            Assert.AreEqual("ContentNullAfterDeserializationException,ILogicBlockHttpClient,ServiceCollectionExtensions", string.Join(",", published));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-013.3")]
        public void DeclareNoSharedAssemblyMarker()
        {
            // Arrange — the attribute's own rule scopes it to contract handler actors and cross-plugin
            // message types, and this package declares neither: nothing it ships is handed across a plugin
            // boundary. Marking it would let whichever plugin bound it first fix the logging, JSON and
            // HTTP-factory versions every other plugin's client resolves against, for the process's life.

            // Act
            var isMarkedShared = Package.GetCustomAttributes(typeof(DaleSharedAssemblyAttribute), false).Length > 0;

            // Assert
            Assert.IsFalse(isMarkedShared, $"{Package.GetName().Name} is reached through DI inside one plugin and declares no cross-plugin message type.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-014.1")]
        [DataRow("Microsoft.Extensions.Logging.Abstractions")]
        [DataRow("System.Text.Json")]
        [DataRow("Microsoft.Extensions.DependencyInjection.Abstractions")]
        [DataRow("Microsoft.Extensions.Http")]
        public void AddDependencyToEveryPluginTakingIt(string dependency)
        {
            // Arrange — a consumer adopting the package inherits these; the first consumer's evaluation
            // priced adoption as "one AddDaleHttpSdk() call", and this is the rest of the bill

            // Act
            var referenced = Package.GetReferencedAssemblies().Select(assembly => assembly.Name).ToList();

            // Assert
            Assert.Contains(dependency, referenced!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-014.1")]
        public void TargetFrameworkEveryPluginCanLoad()
        {
            // Arrange

            // Act
            var targetFramework = Package.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;

            // Assert — netstandard, so a plugin built against any supported runtime can load it
            Assert.AreEqual(".NETStandard,Version=v2.1", targetFramework);
        }

        /// <summary>The literal a <c>const</c> field hands out, or <c>null</c> for every other member.</summary>
        private static string? ConstantValueOf(MemberInfo member)
        {
            return member is FieldInfo { IsLiteral: true } field ? field.GetRawConstantValue() as string : null;
        }
    }
}