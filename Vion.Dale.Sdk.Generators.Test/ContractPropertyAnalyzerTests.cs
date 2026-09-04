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

[ServiceProviderContractType(""TestContractType"")]
public interface ITestContractType { }

public class ConcreteContract : ITestContractType { }
";

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.3")]
        public async Task StaySilentOnPrivateSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType Input { get; private set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.3")]
        public async Task StaySilentOnPublicSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType Input { get; set; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.1")]
        public async Task ReportContractPropertyWithoutSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ITestContractType {|#0:Input|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter).WithLocation(0).WithArguments("Input");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.1")]
        public async Task ReportConcreteContractTypeWithoutSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public class MyBlock
{
    public ConcreteContract {|#0:Input|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter).WithLocation(0).WithArguments("Input");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.1")]
        public async Task StaySilentOnNonContractProperty()
        {
            // Arrange / Act / Assert
            var source = @"
public interface INotAContract { }

public class MyBlock
{
    public INotAContract Input { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.2")]
        public async Task StaySilentOnInterfaceContractPropertyWithoutSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public interface IHolder
{
    ITestContractType Input { get; }
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-004.2")]
        public async Task ReportAbstractContractPropertyWithoutSetter()
        {
            // Arrange / Act / Assert
            var source = ContractTypeSetup + @"
public abstract class HolderBase
{
    public abstract ITestContractType {|#0:Input|} { get; }
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE001_ContractPropertyMustHaveSetter).WithLocation(0).WithArguments("Input");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractPropertyAnalyzer>(source, expected);
        }
    }
}