using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     What every analyzer can and cannot see. The walk exists to agree with the declarative binders,
    ///     so that a diagnostic and a bind-time refusal are two doors onto one rule; these tests pin the
    ///     three places where an author can feel the difference.
    /// </summary>
    [TestClass]
    public class SharedWalkTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-002.2")]
        public async Task StaySilentOnDeclarationCarryingOnlyPresetAttribute()
        {
            // Arrange / Act / Assert
            // A preset attribute — one deriving from a Dale attribute so it can carry a unit — is matched by
            // nothing but DALE019. The runtime honours it (AttributeInheritanceShould), so this pins a live
            // limitation rather than a rule: the by-name match is what makes every other analyzer blind to
            // it, and widening the match re-aims all of them at once. Recorded in docs/specs/_findings.md.
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class KilowattsAttribute : ServicePropertyAttribute { }

public class MyBlock
{
    [Kilowatts] public List<double> Bands { get; set; } = new();
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-002.2")]
        public async Task ReportDeclarationCarryingPlatformAttributeDirectly()
        {
            // Arrange / Act / Assert
            // The control for the test above: the identical declaration, written with the base attribute.
            var source = @"
using System.Collections.Generic;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public List<double> {|#0:Bands|} { get; set; } = new();
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE003_UnsupportedServicePropertyType)
                                           .WithLocation(0)
                                           .WithArguments("Bands", "ServiceProperty", "List<double>");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ServiceElementTypeAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-002.6")]
        [DataRow("public", DisplayName = "public")]
        [DataRow("internal", DisplayName = "internal")]
        [DataRow("public static", DisplayName = "public static")]
        public async Task ReportKnobOnDeclarationOfAnyAccessibility(string modifiers)
        {
            // Arrange / Act / Assert
            // A per-property rule reads the declaration's own attributes, whatever the binders would do with
            // the member. Warning about a knob that will never be read costs nothing; staying silent about a
            // malformed one would teach the author that it parsed.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [{|#0:ServiceProperty(MinInterval = ""nonsense"")|}] " + modifiers + @" int Reading { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE036_MinIntervalInvalid).WithLocation(0).WithArguments("Reading", "nonsense");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MinIntervalInvalidAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.1")]
        public async Task ReportPresentationHintOnMemberWithoutServiceAttribute()
        {
            // Arrange / Act / Assert
            // The presentation rules key off [Presentation] alone. A hint on a member nothing publishes is
            // inert either way, and the advice is the same one the author will need when they mark it.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Decimals = 2)] public string {|#0:Label|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE021_DecimalsOnNonNumeric).WithLocation(0).WithArguments("Label", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<DecimalsOnNonNumericAnalyzer>(source, expected);
        }
    }
}