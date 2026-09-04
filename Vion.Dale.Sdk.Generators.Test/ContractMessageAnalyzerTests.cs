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
        [TestProperty("spec", "AC-ANLZ-005.1")]
        public async Task StaySilentOnConformingRoleNames()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""IProducer"", AndInterface = ""IConsumer"")]
public static class EnergyContract { }
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.1")]
        public async Task ReportBetweenInterfaceWithoutPrefix()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.1")]
        public async Task ReportAndInterfaceWithoutPrefix()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.2")]
        public async Task StaySilentOnCommandNamingBothRoles()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.2")]
        public async Task ReportCommandFromNamingNeitherRole()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.2")]
        public async Task ReportCommandToNamingNeitherRole()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.2")]
        public async Task StaySilentOnStateUpdateNamingBothRoles()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.3")]
        public async Task StaySilentOnNestedResponseStruct()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.3")]
        public async Task ReportResponseTypeOutsideContract()
        {
            // Arrange / Act / Assert
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
        [TestProperty("spec", "AC-ANLZ-005.1")]
        public async Task StaySilentOnClassWithoutContractAttribute()
        {
            // Arrange / Act / Assert
            var source = @"
public static class NotAContract
{
    public readonly record struct SomeStruct;
}
";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.5")]
        [DataRow("Command", "", DisplayName = "a command")]
        [DataRow("StateUpdate", "", DisplayName = "a state update")]
        [DataRow("RequestResponse", ", ResponseType = typeof(StrayAnswer)", DisplayName = "a request-response")]
        public async Task ReportMessageStructDeclaredBesideItsContract(string attribute, string responseType)
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""ISource"", AndInterface = ""ISink"")]
public class Link { }

[" + attribute + @"(From = ""ISource"", To = ""ISink""" + responseType + @")]
public readonly record struct {|#0:StrayNudge|}(int Amount);

public readonly record struct StrayAnswer(int Value);";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE047_MessageStructNotNestedInContract).WithLocation(0).WithArguments("StrayNudge", attribute);
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.5")]
        public async Task StaySilentOnMessageStructNestedInItsContract()
        {
            // Arrange / Act / Assert
            var source = @"
using Vion.Dale.Sdk.Core;

[LogicBlockContract(BetweenInterface = ""ISource"", AndInterface = ""ISink"")]
public class Link
{
    [Command(From = ""ISource"", To = ""ISink"")]
    public readonly record struct Nudge(int Amount);
}";
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-005.5")]
        public async Task ReportMessageStructNestedOutsideContractClass()
        {
            // Arrange / Act / Assert
            // Nesting alone is not the rule: the generator reads the structs of a [LogicBlockContract]
            // class and of nothing else.
            var source = @"
using Vion.Dale.Sdk.Core;

public class NotAContract
{
    [Command(From = ""ISource"", To = ""ISink"")]
    public readonly record struct {|#0:StrayNudge|}(int Amount);
}";
            var expected = AnalyzerTestBase.Diagnostic(DaleDiagnostics.DALE047_MessageStructNotNestedInContract).WithLocation(0).WithArguments("StrayNudge", "Command");
            await AnalyzerTestBase.VerifyAnalyzerAsync<ContractMessageAnalyzer>(source, expected);
        }
    }
}