using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MultiInterfaceConflictAnalyzerTests
    {
        [TestMethod]
        public async Task SingleInterface_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }

public class MyBlock : IOne
{
    public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source);
        }

        [TestMethod]
        public async Task TwoInterfacesAgreeOnUnit_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }

public class MyBlock : IOne, ITwo
{
    public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source);
        }

        [TestMethod]
        public async Task TwoInterfacesConflictOnUnit_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""W"")] double Power { get; set; } }

public class MyBlock : IOne, ITwo
{
    public double {|#0:Power|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE020_MultiInterfaceConflict)
                                           .WithLocation(0)
                                           .WithArguments("MyBlock", "Power", "\"W\", \"kW\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ConflictSuppressedByExplicitOverride_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""W"")] double Power { get; set; } }

public class MyBlock : IOne, ITwo
{
    [ServiceProperty(Unit = ""kW"")]
    public double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source);
        }
    }
}
