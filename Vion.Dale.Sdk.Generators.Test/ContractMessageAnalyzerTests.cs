using System.Threading.Tasks;
using Vion.Dale.Sdk.Generators.Analyzers;
using Vion.Dale.Sdk.Generators.Test.Helpers;

namespace Vion.Dale.Sdk.Generators.Test
{
    [TestClass]
    public class ContractMessageAnalyzerTests
    {
        // --- DALE009: Contract interface names must start with 'I' ---

        [TestMethod]
        public async Task ValidInterfaceNames_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract { }
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        public async Task BetweenInterfaceMissingPrefix_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""Producer"", AndInterface = ""IConsumer"")]
public static class {|#0:EnergyContract|} { }
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix)
                                           .WithLocation(0)
                                           .WithArguments("EnergyContract", "BetweenInterface", "Producer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task AndInterfaceMissingPrefix_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""Consumer"")]
public static class {|#0:EnergyContract|} { }
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE009_ContractInterfaceNamePrefix)
                                           .WithLocation(0)
                                           .WithArguments("EnergyContract", "AndInterface", "Consumer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        // --- DALE010: Command/StateUpdate From/To must match ---

        [TestMethod]
        public async Task ValidCommandFromTo_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [Command(From = ""IProducer"", To = ""IConsumer"")]
    public readonly record struct Allocate;
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        public async Task CommandFromMismatch_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [Command(From = ""IWrong"", To = ""IConsumer"")]
    public readonly record struct {|#0:Allocate|};
}
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE010_MessageFromToMismatch)
                                           .WithLocation(0)
                                           .WithArguments("Allocate", "From", "IWrong", "IProducer", "IConsumer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task CommandToMismatch_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [Command(From = ""IProducer"", To = ""IWrong"")]
    public readonly record struct {|#0:Allocate|};
}
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE010_MessageFromToMismatch)
                                           .WithLocation(0)
                                           .WithArguments("Allocate", "To", "IWrong", "IProducer", "IConsumer");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task StateUpdateValidFromTo_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [StateUpdate(From = ""IConsumer"", To = ""IProducer"")]
    public readonly record struct Status;
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        // --- DALE011: ResponseType must be nested struct ---

        [TestMethod]
        public async Task ValidRequestResponse_NoDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [RequestResponse(From = ""IProducer"", To = ""IConsumer"", ResponseType = typeof(AllocateResponse))]
    public readonly record struct AllocateRequest;

    public readonly record struct AllocateResponse;
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        public async Task ResponseTypeNotInSameContract_ReportsDiagnostic()
        {
            var source = @"
using Vion.Dale.Sdk.Core;

public readonly record struct ExternalResponse;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract
{
    [RequestResponse(From = ""IProducer"", To = ""IConsumer"", ResponseType = typeof(ExternalResponse))]
    public readonly record struct {|#0:AllocateRequest|};
}
";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE011_ResponseTypeMustBeNestedStruct)
                                           .WithLocation(0)
                                           .WithArguments("AllocateRequest", "ExternalResponse", "EnergyContract");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        [TestMethod]
        public async Task NoLogicBlockContractAttribute_NoDiagnostic()
        {
            var source = @"
public static class NotAContract
{
    public readonly record struct SomeStruct;
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }
    }
}