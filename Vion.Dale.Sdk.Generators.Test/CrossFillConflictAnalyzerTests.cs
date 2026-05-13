using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class CrossFillConflictAnalyzerTests
    {
        [TestMethod]
        public async Task NoConflict_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Title = ""Blinks"")]
    [ServiceMeasuringPoint]
    public int Blinks { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<CrossFillConflictAnalyzer>(source);
        }

        [TestMethod]
        public async Task SingleAttributeOnly_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Unit = ""kW"")] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<CrossFillConflictAnalyzer>(source);
        }

        [TestMethod]
        public async Task ConflictingTitle_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Title = ""Property Title"")]
    [ServiceMeasuringPoint(Title = ""Measuring Point Title"")]
    public int {|#0:Blinks|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE025_CrossFillConflict)
                                           .WithLocation(0)
                                           .WithArguments("Blinks", "Title", "\"Property Title\"", "\"Measuring Point Title\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<CrossFillConflictAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ConflictingUnit_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Unit = ""kW"")]
    [ServiceMeasuringPoint(Unit = ""W"")]
    public double {|#0:Power|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE025_CrossFillConflict)
                                           .WithLocation(0)
                                           .WithArguments("Power", "Unit", "\"kW\"", "\"W\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<CrossFillConflictAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task MatchingTitle_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Title = ""Blinks"")]
    [ServiceMeasuringPoint(Title = ""Blinks"")]
    public int Blinks { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<CrossFillConflictAnalyzer>(source);
        }
    }
}
