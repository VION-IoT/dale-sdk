using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ImmediateIgnoresThrottleKnobsAnalyzerTests
    {
        [TestMethod]
        public async Task ImmediateAlone_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true)] public bool Fault { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source);
        }

        [TestMethod]
        public async Task ThrottleKnobsWithoutImmediate_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(MinInterval = ""1s"", MinChange = ""0.1"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmediateWithDefaultMinIntervalEcho_NoDiagnostic()
        {
            // Echoing the default "250ms" alongside Immediate is harmless redundancy, not a misconfig.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true, MinInterval = ""250ms"")] public bool Fault { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmediateWithNonDefaultMinInterval_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true, MinInterval = ""1s"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs).WithLocation(0).WithArguments("Voltage", "MinInterval = \"1s\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ImmediateWithMinChange_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true, MinChange = ""0.1"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs).WithLocation(0).WithArguments("Voltage", "MinChange = \"0.1\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ImmediateWithBothKnobs_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true, MinInterval = ""1s"", MinChange = ""0.1"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs)
                                           .WithLocation(0)
                                           .WithArguments("Voltage", "MinInterval = \"1s\" and MinChange = \"0.1\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ImmediateFalseWithKnobs_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = false, MinInterval = ""1s"")] public double Voltage { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source);
        }

        [TestMethod]
        public async Task ImmediateWithZeroSentinelMinInterval_ReportsDiagnostic()
        {
            // "0" is a non-default MinInterval — explicitly disabling a throttle that Immediate already
            // bypasses is contradictory, so it's surfaced.
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty(Immediate = true, MinInterval = ""0"")] public double {|#0:Voltage|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE038_ImmediateIgnoresThrottleKnobs).WithLocation(0).WithArguments("Voltage", "MinInterval = \"0\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ImmediateIgnoresThrottleKnobsAnalyzer>(source, expected);
        }
    }
}
