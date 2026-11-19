using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE049 coverage. The host runtime reports a shared assembly whose handler actors come with no
    ///     <c>IConfigureServices</c> when it loads it; this rule says so at the assembly's own build. The SDK is
    ///     a referenced assembly here, as in every real build, because the mark only counts when another
    ///     assembly defines it.
    /// </summary>
    [TestClass]
    public class SharedHandlerRegistrationAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        [DataRow("public class MeterHandler : ServiceProviderHandlerBase { }", "Acme.MeterHandler", DisplayName = "deriving from the SDK's base")]
        [DataRow("public class MeterHandler : IServiceProviderHandlerActor { }", "Acme.MeterHandler", DisplayName = "implementing the interface")]
        [DataRow("[DevelopmentOnlyHandler] public class MeterProviderHandler : ServiceProviderHandlerBase { }", "Acme.MeterProviderHandler", DisplayName = "development-only")]
        [DataRow("internal class Outer { private sealed class MeterHandler : ServiceProviderHandlerBase { } }", "Acme.Outer+MeterHandler", DisplayName = "nested and private")]
        public async Task ReportHandlerActorOfSharedAssemblyWithoutRegistration(string handlerDeclaration, string handlerName)
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

[assembly: DaleSharedAssembly]

namespace Acme
{
    " + handlerDeclaration + @"
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.HasCount(1, diagnostics);
            Assert.AreEqual("DALE049", diagnostics[0].Id);
            Assert.AreEqual($"Assembly 'Consumer' is marked [DaleSharedAssembly] and declares handler actors ({handlerName}) but no IConfigureServices implementation. " +
                            "The host runtime reports such an assembly when it loads it. Add a class implementing IConfigureServices to this assembly.",
                            diagnostics[0].GetMessage(CultureInfo.InvariantCulture));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        public async Task ReportOnceAtMarkNamingEveryHandlerActor()
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

[assembly: DaleSharedAssembly]

namespace Acme
{
    public class WaterMeterHandler : ServiceProviderHandlerBase { }

    public class HeatMeterHandler : ServiceProviderHandlerBase { }
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.HasCount(1, diagnostics);
            StringAssert.Contains(diagnostics[0].GetMessage(CultureInfo.InvariantCulture), "(Acme.HeatMeterHandler, Acme.WaterMeterHandler)");
            Assert.AreEqual("DaleSharedAssembly", source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        public async Task StaySilentOnUnmarkedAssembly()
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;

namespace Acme
{
    public class MeterHandler : ServiceProviderHandlerBase { }
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics,
                           "an unmarked assembly is loaded once per plugin, and the runtime asks nothing of it: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        public async Task StaySilentOnAssemblyMarkedByItsOwnAttribute()
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;

[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]

namespace Vion.Dale.Sdk.Core
{
    [System.AttributeUsage(System.AttributeTargets.Assembly)]
    public sealed class DaleSharedAssemblyAttribute : System.Attribute { }
}

namespace Acme
{
    public class MeterHandler : ServiceProviderHandlerBase { }
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics,
                           "the plugin loader treats an assembly that declares the mark itself as unmarked, so it is never shared: " +
                           string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        [DataRow("public", DisplayName = "public registration")]
        [DataRow("internal", DisplayName = "internal registration")]
        public async Task StaySilentOnHandlerActorBesideRegistration(string registrationAccessibility)
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

[assembly: DaleSharedAssembly]

namespace Acme
{
    public class MeterHandler : ServiceProviderHandlerBase { }

    " + registrationAccessibility + @" class DependencyInjection : IConfigureServices { }
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics, "the runtime invokes a registration of any accessibility: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-PLUG-010.1")]
        [DataRow("public readonly record struct MeterReading(double Value);", DisplayName = "message types only")]
        [DataRow("public abstract class MeterHandlerBase : ServiceProviderHandlerBase { }", DisplayName = "abstract handler only")]
        public async Task StaySilentOnSharedAssemblyWithoutConcreteHandlerActor(string declaration)
        {
            // Arrange
            var sdkReference = await MetadataCompilation.CompileSdkStubAsync();
            var source = @"
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;

[assembly: DaleSharedAssembly]

namespace Acme
{
    " + declaration + @"
}";

            // Act
            var diagnostics = await MetadataCompilation.RunAnalyzerAsync(source, new SharedHandlerRegistrationAnalyzer(), sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics, "a shared assembly with no concrete handler actor has nothing to register: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }
    }
}