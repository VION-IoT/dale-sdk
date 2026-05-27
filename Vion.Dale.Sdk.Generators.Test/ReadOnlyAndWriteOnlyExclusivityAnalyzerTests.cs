using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ReadOnlyAndWriteOnlyExclusivityAnalyzerTests
    {
        [TestMethod]
        public async Task ReadOnlyAlone_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(ReadOnly = true)] public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ReadOnlyAndWriteOnlyExclusivityAnalyzer>(source);
        }

        [TestMethod]
        public async Task WriteOnlyAlone_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(WriteOnly = true)] public string ApiKey { get; set; } = """";
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ReadOnlyAndWriteOnlyExclusivityAnalyzer>(source);
        }

        [TestMethod]
        public async Task NeitherFlag_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ReadOnlyAndWriteOnlyExclusivityAnalyzer>(source);
        }

        [TestMethod]
        public async Task BothFlagsTrue_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(ReadOnly = true, WriteOnly = true)] public string {|#0:Token|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE030_ReadOnlyAndWriteOnlyMutuallyExclusive).WithLocation(0).WithArguments("Token");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ReadOnlyAndWriteOnlyExclusivityAnalyzer>(source, expected);
        }
    }
}
