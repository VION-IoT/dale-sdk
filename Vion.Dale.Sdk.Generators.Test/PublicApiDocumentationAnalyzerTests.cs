using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class PublicApiDocumentationAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.1")]
        public async Task ReportPublicApiTypeWithoutSummary()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

namespace TestNs
{
    [PublicApi]
    public class {|#0:MyBlock|} { }
}
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE013_PublicApiMissingDocs).WithLocation(0).WithArguments("MyBlock");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.1")]
        public async Task StaySilentOnDocumentedPublicApiType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

namespace TestNs
{
    /// <summary>Documented type.</summary>
    [PublicApi]
    public class MyBlock { }
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.2")]
        public async Task ReportUnmarkedPublicTypeInApiNamespace()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""TestNs"")]

namespace TestNs
{
    public class {|#0:UnmarkedType|} { }
}
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE014_UnmarkedPublicType).WithLocation(0).WithArguments("UnmarkedType", "TestNs");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.2")]
        public async Task StaySilentOnInternalApiMarkedType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""TestNs"")]

namespace TestNs
{
    [InternalApi]
    public class SomeInternalType { }
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.2")]
        public async Task StaySilentOnPublicApiMarkedType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""TestNs"")]

namespace TestNs
{
    /// <summary>Documented.</summary>
    [PublicApi]
    public class SomePublicType { }
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.2")]
        public async Task StaySilentOnTypeOutsideConfiguredNamespaces()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""ConfiguredNs"")]

namespace ConfiguredNs
{
    /// <summary>Documented.</summary>
    [PublicApi]
    public class RequiredType { }
}

namespace SomeOther.Namespace
{
    public class UnmarkedType { }
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.3")]
        public async Task ReportNamespaceMatchingNoPublicType()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: {|#0:PublicApiNamespace(""Foo.Bar"")|}]
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE015_StalePublicApiNamespace).WithLocation(0).WithArguments("Foo.Bar");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.3")]
        public async Task StaySilentOnNamespaceMatchingPublicTypes()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""MyNs"")]

namespace MyNs
{
    /// <summary>Documented.</summary>
    [PublicApi]
    public class MyType { }
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.4")]
        public async Task ReportNestedPublicApiTypeWithoutSummary()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

namespace Api
{
    /// <summary>Documented outer.</summary>
    [PublicApi]
    public class Outer
    {
        [PublicApi]
        public class {|#0:Inner|} { }
    }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE013_PublicApiMissingDocs).WithLocation(0).WithArguments("Inner");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.4")]
        public async Task ReportUnmarkedNestedPublicTypeInApiNamespace()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""Api"")]

namespace Api
{
    /// <summary>Documented outer.</summary>
    [PublicApi]
    public class Outer
    {
        public class {|#0:Inner|} { }
    }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE014_UnmarkedPublicType).WithLocation(0).WithArguments("Inner", "Api");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.5")]
        public async Task StaySilentOnPublicTypeNestedInNonPublicOuter()
        {
            // Arrange / Act / Assert
            // Nothing outside the assembly can name it, so demanding a mark would be a warning with
            // no action behind it.
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""Api"")]

namespace Api
{
    /// <summary>Documented, and the reason the namespace is not stale.</summary>
    [PublicApi]
    public class Marked { }

    internal class Outer
    {
        public class Inner { }
    }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-012.6")]
        public async Task CreditEveryMatchingNamespacePrefix()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: PublicApiNamespace(""Api"")]
[assembly: PublicApiNamespace(""Api.Sub"")]

namespace Api.Sub
{
    /// <summary>Documented.</summary>
    [PublicApi]
    public class OnlyType { }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source);
        }
    }
}