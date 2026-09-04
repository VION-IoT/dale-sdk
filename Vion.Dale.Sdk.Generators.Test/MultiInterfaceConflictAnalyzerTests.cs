using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class MultiInterfaceConflictAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-006.2")]
        public async Task StaySilentOnSingleInterface()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-006.2")]
        public async Task StaySilentWhenInterfacesAgreeOnUnit()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-006.2")]
        public async Task ReportConflictingUnitsAcrossInterfaces()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""W"")] double Power { get; set; } }

public class MyBlock : IOne, ITwo
{
    public double {|#0:Power|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE020_MultiInterfaceConflict).WithLocation(0).WithArguments("MyBlock", "Power", "\"W\", \"kW\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-006.3")]
        public async Task StaySilentWhenClassDeclaresResolvingAttribute()
        {
            // Arrange / Act / Assert
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

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-006.3")]
        public async Task StaySilentWhenResolvingAttributeSitsOnBaseClass()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""W"")] double Power { get; set; } }

public class PowerBase
{
    [ServiceProperty(Unit = ""kW"")]
    public double Power { get; set; }
}

public class MyBlock : PowerBase, IOne, ITwo { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-006.3")]
        public async Task ResolveShadowedPropertyToMostDerivedDeclaration()
        {
            // Arrange / Act / Assert
            // The base declares the name without a resolving attribute; the derived `new` declaration
            // carries it, and the most-derived declaration is the one the cascade rule reads.
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""W"")] double Power { get; set; } }

public class PowerBase
{
    public double Power { get; set; }
}

public class MyBlock : PowerBase, IOne, ITwo
{
    [ServiceProperty(Unit = ""kW"")]
    public new double Power { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-006.4")]
        public async Task ReportConflictWhenDifferingUnitSitsOnMeasuringPointOfDualAnnotatedDeclaration()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

public interface IOne { [ServiceProperty][ServiceMeasuringPoint(Unit = ""W"")] double Power { get; set; } }
public interface ITwo { [ServiceProperty(Unit = ""kW"")] double Power { get; set; } }

public class MyBlock : IOne, ITwo
{
    public double {|#0:Power|} { get; set; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE020_MultiInterfaceConflict).WithLocation(0).WithArguments("MyBlock", "Power", "\"W\", \"kW\"");
            await AnalyzerTestBase.VerifyAnalyzerAsync<MultiInterfaceConflictAnalyzer>(source, expected);
        }
    }
}