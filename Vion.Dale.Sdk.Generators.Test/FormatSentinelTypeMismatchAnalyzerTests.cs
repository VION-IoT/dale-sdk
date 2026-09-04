using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class FormatSentinelTypeMismatchAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnRelativeSentinelOverDateTime()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = Formats.Relative)] public DateTime LastSampleAt { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatSentinelTypeMismatchAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnHumanizeSentinelOverTimeSpan()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = Formats.Humanize)] public TimeSpan Uptime { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatSentinelTypeMismatchAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task StaySilentOnExplicitFormatToken()
        {
            // Arrange / Act / Assert
            // ""LLLL"" / ""HH:mm:ss"" are not sentinels — DALE028 ignores them.
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = ""LLLL"")] public DateTime A { get; set; }
    [Presentation(Format = ""HH:mm:ss"")] public TimeSpan B { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatSentinelTypeMismatchAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task ReportRelativeSentinelOnTimeSpan()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = Formats.Relative)] public TimeSpan {|#0:Uptime|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE028_FormatSentinelTypeMismatch)
                                           .WithLocation(0)
                                           .WithArguments("Uptime", "relative", "DateTime", "System.TimeSpan");
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatSentinelTypeMismatchAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-009.5")]
        public async Task ReportHumanizeSentinelOnDateTime()
        {
            // Arrange / Act / Assert
            var source = @"
using System;
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [Presentation(Format = Formats.Humanize)] public DateTime {|#0:LastSampleAt|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE028_FormatSentinelTypeMismatch)
                                           .WithLocation(0)
                                           .WithArguments("LastSampleAt", "humanize", "TimeSpan", "System.DateTime");
            await AnalyzerTestBase.VerifyAnalyzerAsync<FormatSentinelTypeMismatchAnalyzer>(source, expected);
        }
    }
}