using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MeasuringPointAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task StaySilentOnPrivateSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task StaySilentOnGetOnlyMeasuringPoint()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task ReportPublicSetterOnMeasuringPoint()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double {|#0:Value|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE004_MeasuringPointPublicSetter).WithLocation(0).WithArguments("Value");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task StaySilentOnDualAnnotatedPublicSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceProperty]
    [ServiceMeasuringPoint]
    public int Counter { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task StaySilentOnInternalSetter()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public class MyBlock
{
    [ServiceMeasuringPoint] public double Value { get; internal set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-008.1")]
        public async Task StaySilentOnInterfaceDeclaration()
        {
            // Arrange / Act / Assert
            // On an interface the suggested { get; private set; } remedy is a compile error, and the
            // private-setter-for-INPC-weaving rationale is an implementation concern. The check belongs
            // on the concrete implementation, so no diagnostic on the interface declaration.
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IMyService
{
    [ServiceMeasuringPoint] double Value { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MeasuringPointAnalyzer>(source);
        }
    }
}