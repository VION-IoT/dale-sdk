using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     DALE048 coverage. The token is the stable cloud-facing identifier of a contract type; the
    ///     attribute validates nothing, so this is the only door before the introspection document.
    /// </summary>
    [TestClass]
    public class ContractTypeTokenAnalyzerTests
    {
        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.4")]
        public async Task StaySilentOnNamedContractTypeToken()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Configuration.Contract;

[ServiceProviderContractType(""GridMeasurement"")]
public interface IGridMeasurement { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractTypeTokenAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.4")]
        [DataRow(@"""""", DisplayName = "empty")]
        [DataRow(@""" """, DisplayName = "one space")]
        [DataRow(@"""\t""", DisplayName = "a tab")]
        public async Task ReportBlankContractTypeToken(string token)
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Configuration.Contract;

[{|#0:ServiceProviderContractType(" + token + @")|}]
public interface IGridMeasurement { }";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE048_ContractTypeTokenMustNotBeBlank).WithLocation(0).WithArguments("IGridMeasurement");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractTypeTokenAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.4")]
        public async Task StaySilentOnInterfaceWithoutContractTypeAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
public interface IGridMeasurement { }";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractTypeTokenAnalyzer>(source);
        }
    }
}