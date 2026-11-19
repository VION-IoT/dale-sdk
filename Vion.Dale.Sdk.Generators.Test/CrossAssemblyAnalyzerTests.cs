using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Vion.Dale.Sdk.Generators.Analyzers;
using static Vion.Dale.Sdk.Generators.Test.Helpers.MetadataCompilation;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     The rules whose subject is the boundary between the compilation and its references. The
    ///     ordinary harness injects the stub attribute file as a source document, so nothing in it can
    ///     reproduce a consumer's compilation — where the SDK, and anything else, is metadata. These tests
    ///     emit a real referenced assembly instead, through <see cref="Helpers.MetadataCompilation" />.
    ///     <para>
    ///         <c>DALE026</c> is why the file exists: <c>Vion.Dale.Sdk.Core.PropertyGroup</c> is a source
    ///         declaration inside the SDK and metadata for every consumer, and a source-only lookup warned
    ///         about the group keys the platform itself ships.
    ///     </para>
    /// </summary>
    [TestClass]
    public class CrossAssemblyAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task StaySilentOnLiteralMatchingConstantInReferencedAssembly()
        {
            // Arrange
            var sdkReference = await CompileSdkStubAsync();

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""status"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference);

            // Assert
            Assert.IsEmpty(diagnostics,
                           "DALE026 must not fire on a key the platform ships: every consumer reads PropertyGroup as metadata. " +
                           string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task StaySilentOnLiteralMatchingIntegratorConstantInReferencedAssembly()
        {
            // Arrange
            var sdkReference = await CompileSdkStubAsync();
            var conventions = await CompileLibraryAsync("Acme.Conventions",
                                                        @"
namespace Acme
{
    public static class PropertyGroup
    {
        public const string Powertrain = ""acme.powertrain"";
    }
}",
                                                        sdkReference);

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference, conventions);

            // Assert
            Assert.IsEmpty(diagnostics, "an integrator's own PropertyGroup class is a reference like any other: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.4")]
        public async Task ReportLiteralMatchingNoConstantInAnyAssembly()
        {
            // Arrange — the negative control: the same layout, no matching constant anywhere.
            var sdkReference = await CompileSdkStubAsync();

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference);

            // Assert
            Assert.HasCount(1, diagnostics);
            Assert.Contains("acme.powertrain", diagnostics[0].GetMessage());
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-010.5")]
        public async Task ReportLiteralMatchingConstantInClassNotNamedPropertyGroup()
        {
            // Arrange — only a class named exactly PropertyGroup is read; a suffixed name is not.
            var sdkReference = await CompileSdkStubAsync();
            var conventions = await CompileLibraryAsync("Acme.Conventions",
                                                        @"
namespace Acme
{
    public static class EcoPropertyGroup
    {
        public const string Powertrain = ""acme.powertrain"";
    }
}",
                                                        sdkReference);

            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Group = ""acme.powertrain"")] public int Counter { get; set; }
}";

            // Act
            var diagnostics = await RunAsync(source, sdkReference, conventions);

            // Assert
            Assert.HasCount(1, diagnostics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-002.3")]
        public async Task StaySilentOnPublicApiTypeDeclaredInReferencedAssembly()
        {
            // Arrange — an undocumented [PublicApi] type in a REFERENCED assembly, in a declared namespace.
            var sdkReference = await CompileSdkStubAsync();
            var library = await CompileLibraryAsync("Library",
                                                    @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""Api"")]

namespace Api
{
    [PublicApi] public class Undocumented { }
}",
                                                    sdkReference);

            var source = @"
public class Consumer { }";

            // Act
            var diagnostics = await RunAnalyzerAsync(source, new PublicApiDocumentationAnalyzer(), sdkReference, library);

            // Assert
            Assert.IsEmpty(diagnostics, "a referenced assembly's declarations are its own build's business: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-013.4")]
        public async Task StaySilentOnVisibleWhenPredicateDeclaredInReferencedAssembly()
        {
            // Arrange — a library base block carrying a predicate that names nothing. Its own build is
            // where DALE041 is reported (AC-ANLZ-013.2's abstract-block rule); the consumer's build reads
            // the same member out of metadata, so a report here would be the second squiggle for one
            // declaration — in a file the consumer cannot edit.
            var sdkReference = await CompileSdkStubAsync();
            var library = await CompileLibraryAsync("BaseBlocks",
                                                    @"
using Vion.Dale.Sdk.Core;

public abstract class BaseBlock : LogicBlockBase
{
    [ServiceProperty]
    [Presentation(VisibleWhen = ""NoSuchProperty"")]
    public bool Visible { get; set; }
}",
                                                    sdkReference);

            var source = @"
public class MyBlock : BaseBlock { }";

            // Act
            var diagnostics = await RunAnalyzerAsync(source, new VisibleWhenPredicateAnalyzer(), sdkReference, library);

            // Assert
            Assert.IsEmpty(diagnostics,
                           "a predicate declared in metadata was judged when its own assembly was built: " + string.Join("; ", diagnostics.Select(d => d.GetMessage())));
        }

        private static Task<Diagnostic[]> RunAsync(string consumerSource, params MetadataReference[] references)
        {
            return RunAnalyzerAsync(consumerSource, new LiteralGroupKeyAnalyzer(), references);
        }
    }
}