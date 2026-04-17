using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class PublicApiDocumentationAnalyzerTests
    {
        [TestMethod]
        public async Task DALE013_PublicApiWithoutSummary_ReportsWarning()
        {
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
        public async Task DALE013_PublicApiWithSummary_NoDiagnostic()
        {
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
        public async Task DALE014_PublicTypeInApiNamespace_WithoutAttribute_ReportsWarning()
        {
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
        public async Task DALE014_TypeWithInternalApi_NoDiagnostic()
        {
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
        public async Task DALE014_TypeWithPublicApi_NoDiagnostic()
        {
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
        public async Task DALE014_TypeInNonConfiguredNamespace_NoDiagnostic()
        {
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
        public async Task DALE015_StaleNamespace_ReportsWarning()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[assembly: {|#0:PublicApiNamespace(""Foo.Bar"")|}]
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE015_StalePublicApiNamespace).WithLocation(0).WithArguments("Foo.Bar");
            await AnalyzerTestBase.VerifyAnalyzerAsync<PublicApiDocumentationAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task DALE015_NamespaceWithTypes_NoDiagnostic()
        {
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
    }
}