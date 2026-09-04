using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class FormatOnNonTemporalAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnFormatOverDateTime()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = ""LLLL"")] public DateTime LastSampleAt { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatOnNonTemporalAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnFormatOverTimeSpan()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = ""HH:mm:ss"")] public TimeSpan Uptime { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatOnNonTemporalAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnFormatOverNullableDateTime()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = Formats.Relative)] public DateTime? ScheduledAt { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatOnNonTemporalAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentWhenFormatUnset()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Decimals = 2)] public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatOnNonTemporalAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task ReportFormatOnNonTemporalProperty()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = ""LLLL"")] public string {|#0:Notes|} { get; set; } = """";
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE027_FormatOnNonTemporal).WithLocation(0).WithArguments("Notes", "string");
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatOnNonTemporalAnalyzer>(source, expected);
        }
    }
}