using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ContractPropertyAnalyzerTests
    {
        private const string ContractTypeSetup = @"
using Vion.Dale.Sdk.Configuration.Contract;

[ServiceProviderContractType]
public interface ITestContractType { }

public class ConcreteContract : ITestContractType { }
";

        [TestMethod]
        public async Task ContractPropertyWithPrivateSetter_NoDiagnostic()
        {
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType Input { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        public async Task ContractPropertyWithPublicSetter_NoDiagnostic()
        {
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType Input { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        public async Task ContractPropertyWithoutSetter_ReportsDiagnostic()
        {
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType {|#0:Input|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter).WithLocation(0).WithArguments("Input");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task ConcreteContractTypeWithoutSetter_ReportsDiagnostic()
        {
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ConcreteContract {|#0:Input|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter).WithLocation(0).WithArguments("Input");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NonContractProperty_NoDiagnostic()
        {
            var source = @"
public interface INotAContract { }

public class MyBlock
{
    public INotAContract Input { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }
    }
}