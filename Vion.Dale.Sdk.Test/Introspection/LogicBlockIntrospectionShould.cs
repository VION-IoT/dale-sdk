using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Conventions;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Examples.FunctionInterfaces;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Introspection
{
    [LogicBlockContract(BetweenInterface = "ITestProvider",
                        AndInterface = "ITestConsumer",
                        BetweenDefaultName = "Provider",
                        AndDefaultName = "Consumer",
                        Direction = ContractDirection.BetweenToAnd)]
    public static class TestDirectionalContract
    {
        [Command(From = "ITestProvider", To = "ITestConsumer")]
        public readonly record struct TestCommand(string Data);
    }

    [LogicBlockInterfaceBinding(typeof(ITestProvider), DefaultName = "Quelle", Multiplicity = LinkMultiplicity.ZeroOrOne, Tags = new[] { "provider-tag" })]
    public class BetweenSideTestBlock : LogicBlockBase, ITestProvider
    {
        public BetweenSideTestBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class AndSideTestBlock : LogicBlockBase, ITestConsumer
    {
        public AndSideTestBlock() : base(new Mock<ILogger>().Object)
        {
        }

        public void HandleCommand(TestDirectionalContract.TestCommand command)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public enum DeviceConnectionState
    {
        [EnumLabel("Unbekannt")]
        [Severity(StatusSeverity.Neutral)]
        Unknown,

        [EnumLabel("Verbunden")]
        [Severity(StatusSeverity.Success)]
        Connected,

        [EnumLabel("Getrennt")]
        [Severity(StatusSeverity.Error)]
        Disconnected,

        // Declared out of alphabetical order on purpose: the sorted-key-order test (VION-77) needs
        // declaration order, hash order and sorted order to be three different sequences, and five members
        // make an accidentally-sorted hash order unlikely enough for the test to discriminate.
        [EnumLabel("Blockiert")]
        [Severity(StatusSeverity.Warning)]
        Blocked,

        [EnumLabel("Aushandeln")]
        [Severity(StatusSeverity.Neutral)]
        Authenticating,
    }

    public enum OperatingMode
    {
        [EnumLabel("Automatik")]
        Auto,

        [EnumLabel("Manuell")]
        Manual,
    }

    // Literal "Energy" / "Visuals" group keys below are intentional: they exercise the
    // custom-key path (no constant required). The DALE026 analyzer would normally suggest
    // using a constant, but here the literals are the unit-under-test.
#pragma warning disable DALE026

    [LogicBlock(Name = "Testgerät", Icon = "device-line", Groups = new[] { "Energy", "Devices" })]
    public class TestLogicBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Leistung", Unit = "kW")]
        [Presentation(Importance = Importance.Primary, Group = "Energy")]
        public double ActivePower { get; set; }

        [ServiceProperty(Unit = "kWh")]
        [ServiceMeasuringPoint(Unit = "kWh")]
        [Presentation(Importance = Importance.Secondary, Group = "Energy")]
        public double EnergyTotal { get; private set; }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Configuration)]
        public double MaxPower { get; set; } = 10;

        [ServiceProperty]
        [Presentation(StatusIndicator = true)]
        public DeviceConnectionState ConnectionState { get; private set; }

        [ServiceProperty]
        public OperatingMode Mode { get; set; }

        [ServiceProperty]
        [Presentation(DisplayName = "Helligkeit", Group = "Visuals", Order = 5, UiHint = "slider")]
        public int Brightness { get; set; }

        [ServiceMeasuringPoint(Title = "Temperatur", Unit = "°C")]
        public double Temperature { get; private set; }

        public TestLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

#pragma warning restore DALE026

    public class ContractTestLogicBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(Identifier = "Button", DefaultName = "Taster", Multiplicity = LinkMultiplicity.ZeroOrOne, Tags = new[] { "input", "sensor" })]
        public IDigitalInput Button { get; set; } = null!;

        [ServiceProviderContractBinding(Identifier = "LED")]
        public IDigitalOutput Led { get; set; } = null!;

        [ServiceProviderContractBinding]
        public IAnalogInput Temperature { get; set; } = null!;

        public ContractTestLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    /// <summary>
    ///     A simulator: it binds the four provider faces, which are the development-only inverses of the HAL
    ///     contracts <see cref="ContractTestLogicBlock" /> binds.
    /// </summary>
    public class ProviderContractTestLogicBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(Identifier = "ButtonProvider")]
        public IDigitalInputProvider Button { get; set; } = null!;

        [ServiceProviderContractBinding(Identifier = "LedProvider")]
        public IDigitalOutputProvider Led { get; set; } = null!;

        [ServiceProviderContractBinding(Identifier = "TemperatureProvider")]
        public IAnalogInputProvider Temperature { get; set; } = null!;

        [ServiceProviderContractBinding(Identifier = "DimmerProvider")]
        public IAnalogOutputProvider Dimmer { get; set; } = null!;

        public ProviderContractTestLogicBlock() : base(NullLogger.Instance)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    /// <summary>
    ///     A block that binds one ordinary HAL contract and one provider face, the provider face gated by an
    ///     <c>[IncludedWhen]</c> predicate — the shape the pack gate must still judge development-only.
    /// </summary>
    public class GatedProviderContractTestLogicBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Kanäle", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int ChannelCount { get; init; } = 1;

        [ServiceProviderContractBinding(Identifier = "LED")]
        public IDigitalOutput Led { get; set; } = null!;

        [IncludedWhen("ChannelCount >= 2")]
        [ServiceProviderContractBinding(Identifier = "LedProvider")]
        public IDigitalOutputProvider LedProvider { get; set; } = null!;

        public GatedProviderContractTestLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    [LogicBlock(Name = "KindBlock", Icon = "gauge-line")]
    public class MeasuringPointKindLogicBlock : LogicBlockBase
    {
        [ServiceMeasuringPoint(Unit = "kWh", Kind = MeasuringPointKind.TotalIncreasing)]
        public double LifetimeEnergy { get; private set; }

        [ServiceMeasuringPoint(Unit = "kWh", Kind = MeasuringPointKind.Total)]
        public double DailyEnergy { get; private set; }

        [ServiceMeasuringPoint(Unit = "kW")]
        public double InstantPower { get; private set; }

        public MeasuringPointKindLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class PlainLogicBlock : LogicBlockBase
    {
        public PlainLogicBlock() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    [TestClass]
    public class LogicBlockIntrospectionShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        public static IEnumerable<object[]> BlankIdentifierBlocks
        {
            get
            {
                yield return [new BlankInterfaceIdentifierBlock(), nameof(BlankInterfaceIdentifierBlock.Blank)];
                yield return [new BlankContractIdentifierBlock(), nameof(BlankContractIdentifierBlock.Blank)];
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.3")]
        public void ReadBlockLevelAnnotations()
        {
            // Arrange

            // Act
            var annotations = GetAnnotations();

            // Assert
            Assert.AreEqual("Testgerät", annotations["DefaultName"]);
            Assert.AreEqual("device-line", annotations["Icon"]);
            CollectionAssert.AreEqual(new[] { "Energy", "Devices" }, (string[])annotations["Groups"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.5")]
        public void ReturnEmptyAnnotationsWhenNoLogicBlockAttribute()
        {
            // Arrange
            var block = new PlainLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.IsEmpty(result.Annotations);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void ReadImportanceAnnotation()
        {
            // Arrange

            // Act
            var activePower = GetProperty("ActivePower");

            // Importance maps to presentation.importance

            // Assert
            Assert.AreEqual("Primary", activePower.Presentation?["importance"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        [DataRow("MaxPower", "configuration", DisplayName = "a well-known group key")]
        [DataRow("ActivePower", "Energy", DisplayName = "a custom group key")]
        public void ReadGroupAnnotation(string identifier, string expectedGroup)
        {
            // Arrange

            // Act
            var property = GetProperty(identifier);

            // Assert
            Assert.AreEqual(expectedGroup, property.Presentation?["group"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void ReadDisplayNameOverridingDefaultName()
        {
            // Arrange

            // Act
            var brightness = GetProperty("Brightness");

            // DisplayName maps to presentation.displayName

            // Assert
            Assert.AreEqual("Helligkeit", brightness.Presentation?["displayName"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void ReadDisplayOrderAnnotation()
        {
            // Arrange

            // Act
            var brightness = GetProperty("Brightness");

            // Order maps to presentation.order

            // Assert
            Assert.AreEqual(5, brightness.Presentation?["order"]?.GetValue<int>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void ReadUIHintAnnotation()
        {
            // Arrange

            // Act
            var brightness = GetProperty("Brightness");

            // UIHint maps to presentation.uiHint

            // Assert
            Assert.AreEqual("slider", brightness.Presentation?["uiHint"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.5")]
        public void ReadStatusIndicatorAnnotation()
        {
            // Arrange

            // Act
            var connectionState = GetProperty("ConnectionState");

            // StatusIndicator presence is indicated by statusMappings being populated.

            // Assert
            Assert.IsNotNull(connectionState.Presentation?["statusMappings"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.1")]
        public void ReadStatusMappingsFromStatusIndicatorProperty()
        {
            // Arrange

            // Act
            var connectionState = GetProperty("ConnectionState");
            var mappings = connectionState.Presentation?["statusMappings"] as JsonObject;

            // Assert
            Assert.IsNotNull(mappings);

            // New shape: statusMappings is a flat object mapping member name → severity string.
            Assert.AreEqual("neutral", mappings["Unknown"]?.GetValue<string>());
            Assert.AreEqual("success", mappings["Connected"]?.GetValue<string>());
            Assert.AreEqual("error", mappings["Disconnected"]?.GetValue<string>());
        }

        [TestMethod]
        [DataRow("statusMappings")]
        [DataRow("enumLabels")]
        [TestProperty("spec", "AC-INTRO-003.2")]
        public void SerializeStatusAndLabelMapsInSortedKeyOrder(string mapName)
        {
            // Arrange
            // VION-77: both maps are built as immutable dictionaries, and .NET randomizes string hashing per
            // process — so the same assembly serialized them in a different order on every run, and
            // `dale dev --export-config` wrote a different file each time.
            // The assertion is sorted order, not "two serializations agree": two serializations inside one
            // test process share a hash seed and agree with or without the canonicalization.
            // DeviceConnectionState is declared Unknown, Connected, Disconnected, Blocked, Authenticating — so
            // declaration order and sorted order are genuinely different documents, and only the fix produces
            // the sorted one.

            // Act
            var map = GetProperty("ConnectionState").Presentation?[mapName] as JsonObject;

            // Assert
            Assert.IsNotNull(map, $"ConnectionState must carry {mapName} for this test to mean anything.");

            var keys = map.Select(entry => entry.Key).ToList();
            CollectionAssert.AreEqual(new[] { "Authenticating", "Blocked", "Connected", "Disconnected", "Unknown" }, keys, $"{mapName} key order: {string.Join(", ", keys)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.1")]
        public void ReadEnumMembersInSchema()
        {
            // Arrange

            // Act
            var mode = GetProperty("Mode");

            // New shape: enum members are inline in schema.enum as an array of name strings.
            // Integer values are NOT on the wire per spec §5.1.
            var enumArray = mode.Schema["enum"] as JsonArray;

            // Assert
            Assert.IsNotNull(enumArray);
            Assert.HasCount(2, enumArray);
            Assert.IsTrue(enumArray.Any(e => e?.GetValue<string>() == "Auto"));
            Assert.IsTrue(enumArray.Any(e => e?.GetValue<string>() == "Manual"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.2")]
        public void ReadServicePropertySchemaAnnotations()
        {
            // Arrange

            // Act
            var activePower = GetProperty("ActivePower");

            // Title maps to schema.title; Unit maps to schema["x-unit"].

            // Assert
            Assert.AreEqual("Leistung", activePower.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("kW", activePower.Schema["x-unit"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.2")]
        public void ReadMeasuringPointSchemaAnnotations()
        {
            // Arrange

            // Act
            var temperature = GetMeasuringPoint("Temperature");

            // Assert
            // Title and Unit map to schema fields.
            Assert.AreEqual("Temperatur", temperature.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("°C", temperature.Schema["x-unit"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void ReadUiAnnotationsOnMeasuringPoints()
        {
            // Arrange

            // Act
            var energyTotal = GetMeasuringPoint("EnergyTotal");

            // Assert
            // EnergyTotal carries its importance and group from the logic-block property, which maps to
            // presentation.
            Assert.AreEqual("Secondary", energyTotal.Presentation?["importance"]?.GetValue<string>());
            Assert.AreEqual("Energy", energyTotal.Presentation?["group"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.2")]
        [TestProperty("spec", "AC-INTRO-010.1")]
        public void NotIncludeAbsentPresentationKeys()
        {
            // Arrange
            // MaxPower has only [Presentation(Group = ...)] — no Importance / Order / UiHint.

            // Act
            var maxPower = GetProperty("MaxPower");

            // Assert
            Assert.IsNull(maxPower.Presentation?["importance"]);
            Assert.IsNull(maxPower.Presentation?["uiHint"]);
            Assert.IsNull(maxPower.Presentation?["order"]);

            // statusMappings should be absent (no StatusIndicator = true).
            Assert.IsNull(maxPower.Presentation?["statusMappings"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.2")]
        public void ReadContractNameOnBetweenSideInterface()
        {
            // Arrange
            var block = new BetweenSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("TestDirectionalContract", iface.Annotations["ContractName"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.4")]
        public void ResolveOutboundDirectionOnBetweenSide()
        {
            // Arrange
            var block = new BetweenSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("Outbound", iface.Annotations["ArrowDirection"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.4")]
        public void ResolveInboundDirectionOnAndSide()
        {
            // Arrange
            var block = new AndSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("Inbound", iface.Annotations["ArrowDirection"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.3")]
        public void ReadRoleDefaultNamesOnBetweenSide()
        {
            // Arrange
            var block = new BetweenSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("Provider", iface.Annotations["RoleDefaultName"]);
            Assert.AreEqual("Consumer", iface.Annotations["MatchingRoleDefaultName"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.3")]
        public void ReadRoleDefaultNamesOnAndSide()
        {
            // Arrange
            var block = new AndSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("Consumer", iface.Annotations["RoleDefaultName"]);
            Assert.AreEqual("Provider", iface.Annotations["MatchingRoleDefaultName"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.2")]
        public void ReadInterfaceMultiplicityAnnotation()
        {
            // Arrange
            var block = new BetweenSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();

            // Assert
            Assert.AreEqual("Quelle", iface.Annotations["DefaultName"]);

            // Frozen forward-contract shape: the consumer-side link multiplicity rides
            // the loose Annotations dict, keyed by LogicBlockWiringConventions and
            // valued with the shared token string (not a boxed enum, not x-).
            Assert.AreEqual(LogicBlockWiringConventions.ZeroOrOne, iface.Annotations[LogicBlockWiringConventions.MultiplicityAnnotationKey]);

            // The deleted Cardinality/Sharing/CreationType keys must no longer be emitted.
            Assert.IsFalse(iface.Annotations.ContainsKey("Cardinality"));
            Assert.IsFalse(iface.Annotations.ContainsKey("Sharing"));
            Assert.IsFalse(iface.Annotations.ContainsKey("CreationType"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.2")]
        public void ReadInterfaceDependencyTagsAnnotation()
        {
            // Arrange
            var block = new BetweenSideTestBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            var tags = (List<string>)iface.Annotations["Tags"];

            // Assert
            Assert.HasCount(1, tags);
            Assert.Contains("provider-tag", tags);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.2")]
        public void IntrospectContractsWithIdentifiers()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.HasCount(3, result.Contracts);
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Button"));
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "LED"));
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Temperature"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.5")]
        public void IntrospectContractMatchingContractType()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");

            // Assert
            Assert.AreEqual("DigitalInput", button.MatchingContractType);

            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.AreEqual("DigitalOutput", led.MatchingContractType);

            var temperature = result.Contracts.First(c => c.Identifier == "Temperature");
            Assert.AreEqual("AnalogInput", temperature.MatchingContractType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void IntrospectContractDefaultNameAnnotation()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");

            // Assert
            Assert.AreEqual("Taster", button.Annotations["DefaultName"]);

            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.IsFalse(led.Annotations.ContainsKey("DefaultName"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void IntrospectContractMultiplicityAndConsumersAnnotations()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Button: IDigitalInput bound with consumer-side Multiplicity = ZeroOrOne.
            // Inputs default provider-side Consumers to ZeroOrMore → no Consumers key.
            var button = result.Contracts.First(c => c.Identifier == "Button");

            // Assert
            Assert.AreEqual(LogicBlockWiringConventions.ZeroOrOne, button.Annotations[LogicBlockWiringConventions.MultiplicityAnnotationKey]);
            Assert.IsFalse(button.Annotations.ContainsKey(LogicBlockWiringConventions.ConsumersAnnotationKey));
            Assert.IsFalse(button.Annotations.ContainsKey("Cardinality"));
            Assert.IsFalse(button.Annotations.ContainsKey("Sharing"));

            // LED: IDigitalOutput bound with no explicit Multiplicity (default
            // ZeroOrMore → no Multiplicity key); provider-side Consumers = ZeroOrOne
            // (single-writer) is injected from [ServiceProviderContractType].
            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.IsFalse(led.Annotations.ContainsKey(LogicBlockWiringConventions.MultiplicityAnnotationKey));
            Assert.AreEqual(LogicBlockWiringConventions.ZeroOrOne, led.Annotations[LogicBlockWiringConventions.ConsumersAnnotationKey]);

            // Temperature: IAnalogInput, all-default (consumer + provider ZeroOrMore)
            // → neither key present; the unconstrained default is omitted.
            var temperature = result.Contracts.First(c => c.Identifier == "Temperature");
            Assert.IsFalse(temperature.Annotations.ContainsKey(LogicBlockWiringConventions.MultiplicityAnnotationKey));
            Assert.IsFalse(temperature.Annotations.ContainsKey(LogicBlockWiringConventions.ConsumersAnnotationKey));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void IntrospectContractHandlerActorNameAnnotation()
        {
            // Arrange
            // The contract's ContractHandlerActorName is surfaced so the DevHost can address the generic
            // stand-in registered under it when a scenario drives the contract (RFC 0010).
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");

            // Assert
            Assert.AreEqual("DigitalInputHandler", button.Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName]);

            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.AreEqual("DigitalOutputHandler", led.Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName]);

            var temperature = result.Contracts.First(c => c.Identifier == "Temperature");
            Assert.AreEqual("AnalogInputHandler", temperature.Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void IntrospectDevelopmentOnlyAnnotationOnEveryProviderFace()
        {
            // Arrange
            // Every provider face is development surface, so each carries the flag — and its contract-type
            // name is the consumer face's name with the Provider suffix (a stable introspection identifier).
            var block = new ProviderContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var expected = new Dictionary<string, string>
                           {
                               ["ButtonProvider"] = "DigitalInputProvider",
                               ["LedProvider"] = "DigitalOutputProvider",
                               ["TemperatureProvider"] = "AnalogInputProvider",
                               ["DimmerProvider"] = "AnalogOutputProvider",
                           };

            foreach (var (identifier, contractType) in expected)
            {
                var contract = result.Contracts.First(c => c.Identifier == identifier);

                // Assert
                Assert.AreEqual(contractType, contract.MatchingContractType);
                Assert.IsTrue(contract.Annotations.TryGetValue(ServiceProviderContractAnnotations.DevelopmentOnly, out var flag) && flag is true,
                              $"{identifier} must be flagged development-only.");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void OmitDevelopmentOnlyAnnotationOnOrdinaryContracts()
        {
            // Arrange
            // The flag is emitted only when set — an ordinary HAL contract's annotation bag is unchanged.
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            foreach (var contract in result.Contracts)
            {
                // Assert
                Assert.IsFalse(contract.Annotations.ContainsKey(ServiceProviderContractAnnotations.DevelopmentOnly),
                               $"{contract.Identifier} must not carry the development-only flag.");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.6")]
        public void ReportEveryDevelopmentOnlyContractOfSimulatorBlock()
        {
            // Arrange
            // The pack gate's predicate: a block that binds any provider face is development surface, and the
            // report names each binding so a pack log can say what the production artifact does not carry.
            var block = new ProviderContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var developmentOnly = LogicBlockIntrospection.GetDevelopmentOnlyContracts(result);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "ButtonProvider", "LedProvider", "TemperatureProvider", "DimmerProvider" },
                                           developmentOnly.Select(contract => contract.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.6")]
        public void ReportNoDevelopmentOnlyContractForProductionBlock()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.IsEmpty(LogicBlockIntrospection.GetDevelopmentOnlyContracts(result));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.6")]
        public void ReportDevelopmentOnlyContractEvenForGatedBinding()
        {
            // Arrange
            // Strict by design, and consistent with the production runtime's refusal: the flag is judged on the
            // declaration, so an [IncludedWhen] gate cannot argue a block back into the production artifact.
            var block = new GatedProviderContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var developmentOnly = LogicBlockIntrospection.GetDevelopmentOnlyContracts(result);

            // Assert
            Assert.HasCount(1, developmentOnly);
            Assert.AreEqual("LedProvider", developmentOnly[0].Identifier);
            Assert.AreEqual("ChannelCount >= 2", result.Contracts.First(c => c.Identifier == "LedProvider").Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.6")]
        public void IntrospectContractTagsAnnotation()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");
            var tags = (List<string>)button.Annotations["Tags"];

            // Assert
            Assert.HasCount(2, tags);
            Assert.Contains("input", tags);
            Assert.Contains("sensor", tags);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.2")]
        public void UsePropertyNameAsContractIdentifierWhenNotSpecified()
        {
            // Arrange
            var block = new ContractTestLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Temperature"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-006.3")]
        public void EmitByteIdenticalXKindWireTokenForEachMeasuringPointKind()
        {
            // Arrange
            // The Kind attribute now carries the SDK-Core mirror enum; PropertyMetadataBuilder
            // casts it back to the canonical wire enum. The emitted x-kind token must stay
            // byte-identical to the pre-mirror output (measurement / total / totalIncreasing).
            var block = new MeasuringPointKindLogicBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);
            var service = result.Services.First();

            var lifetime = service.MeasuringPoints.First(m => m.Identifier == "LifetimeEnergy");
            var daily = service.MeasuringPoints.First(m => m.Identifier == "DailyEnergy");
            var instant = service.MeasuringPoints.First(m => m.Identifier == "InstantPower");

            // Assert
            Assert.AreEqual("totalIncreasing", lifetime.Schema["x-kind"]?.GetValue<string>());
            Assert.AreEqual("total", daily.Schema["x-kind"]?.GetValue<string>());
            Assert.AreEqual("measurement", instant.Schema["x-kind"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.3")]
        public void OmitNonFiniteBound()
        {
            // Arrange
            var block = new NonFiniteBoundBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties;
            var nan = properties.Single(property => property.Identifier == nameof(NonFiniteBoundBlock.NanBounds));
            var swapped = properties.Single(property => property.Identifier == nameof(NonFiniteBoundBlock.SwappedInfinities));
            var bounded = properties.Single(property => property.Identifier == nameof(NonFiniteBoundBlock.Bounded));

            Assert.IsNull(nan.Schema["minimum"]);
            Assert.IsNull(nan.Schema["maximum"]);
            Assert.IsNull(swapped.Schema["minimum"]);
            Assert.IsNull(swapped.Schema["maximum"]);
            Assert.AreEqual(1d, bounded.Schema["minimum"]?.GetValue<double>());
            Assert.AreEqual(9d, bounded.Schema["maximum"]?.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.3")]
        public void OmitNonFiniteStructFieldBound()
        {
            // Arrange
            var block = new NonFiniteBoundBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var fields = result.Services.Single().Properties.Single(property => property.Identifier == nameof(NonFiniteBoundBlock.Fields)).Schema["properties"];
            Assert.IsNull(fields?["nan"]?["minimum"]);
            Assert.IsNull(fields?["nan"]?["maximum"]);
            Assert.IsNull(fields?["swapped"]?["minimum"]);
            Assert.IsNull(fields?["swapped"]?["maximum"]);
            Assert.AreEqual(1d, fields?["bounded"]?["minimum"]?.GetValue<double>());
            Assert.AreEqual(9d, fields?["bounded"]?["maximum"]?.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.3")]
        public void SerializeDocumentDeclaringNonFiniteBound()
        {
            // Arrange
            var block = new NonFiniteBoundBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The harm the omission prevents: System.Text.Json cannot write a NaN or an infinity, so one such
            // bound aborted the whole document with an exception naming neither the member nor the block. The
            // assertion is the serialized text, because that is the thing that used to throw.
            var serialized = string.Join(string.Empty, result.Services.Single().Properties.Select(property => property.Schema.ToJsonString()));

            Assert.Contains("\"minimum\":1", serialized);
            Assert.DoesNotContain("NaN", serialized);
            Assert.DoesNotContain("Infinity", serialized);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-006.2")]
        public void ReportMeasuringPointKindOnMeasuringPointStreamOnly()
        {
            // Arrange
            var block = new DualStreamKindBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var service = result.Services.Single();
            Assert.IsNull(service.Properties.Single().Schema["x-kind"]);
            Assert.AreEqual("totalIncreasing", service.MeasuringPoints.Single().Schema["x-kind"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-006.1")]
        public void ReportOneTitleAndDescriptionForMemberOnBothStreams()
        {
            // Arrange
            var block = new DualStreamKindBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var service = result.Services.Single();
            var property = service.Properties.Single();
            var measuringPoint = service.MeasuringPoints.Single();

            Assert.AreEqual(nameof(DualStreamKindBlock.Power), property.Identifier);
            Assert.AreEqual(nameof(DualStreamKindBlock.Power), measuringPoint.Identifier);
            Assert.AreEqual("Grid power", property.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("Grid power", measuringPoint.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("Live state and a chart", property.Schema["description"]?.GetValue<string>());
            Assert.AreEqual("Live state and a chart", measuringPoint.Schema["description"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.3")]
        [DynamicData(nameof(BlankIdentifierBlocks))]
        public void RefuseBindingWhoseIdentifierBlank(LogicBlockBase block, string memberName)
        {
            // Arrange / Act / Assert
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));

            Assert.Contains(memberName, exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.4")]
        public void RefuseTwoInterfaceBindingsResolvingToOneIdentifier()
        {
            // Arrange
            var block = new CollidingInterfaceIdentifierBlock();

            // Act / Assert
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));

            Assert.Contains("Shared", exception.Message);
            Assert.Contains(nameof(CollidingInterfaceIdentifierBlock.Peer), exception.Message);
            Assert.Contains(nameof(IToggleable), exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.4")]
        public void RefuseTwoContractBindingsResolvingToOneIdentifier()
        {
            // Arrange
            var block = new CollidingContractIdentifierBlock();

            // Act / Assert
            var exception = Assert.ThrowsExactly<InvalidOperationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));

            Assert.Contains("Shared", exception.Message);
            Assert.Contains(nameof(CollidingContractIdentifierBlock.OutputA), exception.Message);
            Assert.Contains(nameof(CollidingContractIdentifierBlock.OutputB), exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.1")]
        public void DeriveDistinctIdentifiersForTwoBindingsOfOneInterface()
        {
            // Arrange
            var block = new DistinctInterfaceIdentifierBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "Left_IToggleable", "Right_IToggleable" }, result.Interfaces.Select(logicInterface => logicInterface.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-005.3")]
        public void DistinguishServiceIdentifiersDifferingOnlyInCase()
        {
            // Arrange
            var block = new CaseDistinctServiceBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(CaseDistinctServiceBlock), nameof(CaseDistinctServiceBlock.Sensor), nameof(CaseDistinctServiceBlock.SENSOR) },
                                           result.Services.Select(service => service.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.6")]
        public void CarryDisplayStringsVerbatim()
        {
            // Arrange
            var block = new VerbatimStringBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var marked = result.Services.Single().Properties.Single(property => property.Identifier == nameof(VerbatimStringBlock.Marked));
            Assert.AreEqual("Tür & <b>Wärme</b> — 20 °C", result.Annotations["DefaultName"]);
            Assert.AreEqual("Tür & <i>Wärme</i> — 20 °C", marked.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("Ünïcödé — em-dash — and \"quotes\"", marked.Schema["description"]?.GetValue<string>());
            Assert.AreEqual("€/kWh", marked.Schema["x-unit"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.8")]
        public void CarryEmptyDisplayStringsRatherThanOmittingThem()
        {
            // Arrange
            var block = new VerbatimStringBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var empties = result.Services.Single().Properties.Single(property => property.Identifier == nameof(VerbatimStringBlock.Empties));
            Assert.AreEqual(string.Empty, empties.Schema["title"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, empties.Schema["description"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, empties.Schema["x-unit"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, empties.Presentation?["displayName"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, empties.Presentation?["group"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.3")]
        [TestProperty("spec", "AC-INTRO-009.4")]
        public void OmitOrderDecimalsAndImportanceDeclaredAtTheirDefaults()
        {
            // Arrange
            var block = new VerbatimStringBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var empties = result.Services.Single().Properties.Single(property => property.Identifier == nameof(VerbatimStringBlock.Empties));
            Assert.IsNull(empties.Presentation?["order"]);
            Assert.IsNull(empties.Presentation?["decimals"]);
            Assert.IsNull(empties.Presentation?["importance"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.7")]
        public void ReportBoundedRangeThatCannotBeSatisfied()
        {
            // Arrange
            var block = new VerbatimStringBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var inverted = result.Services.Single().Properties.Single(property => property.Identifier == nameof(VerbatimStringBlock.Inverted));
            Assert.AreEqual(10d, inverted.Schema["minimum"]?.GetValue<double>());
            Assert.AreEqual(1d, inverted.Schema["maximum"]?.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.4")]
        public void OmitBlockAnnotationsDeclaredEmpty()
        {
            // Arrange
            var block = new EmptyAnnotationBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.IsEmpty(result.Annotations);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.2")]
        public void ReadSeveritiesThroughNullableEnumAndNotThroughArray()
        {
            // Arrange
            var block = new SeverityReachBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties;
            var throughNullable = properties.Single(property => property.Identifier == nameof(SeverityReachBlock.Nullable));
            var throughArray = properties.Single(property => property.Identifier == nameof(SeverityReachBlock.Array));

            Assert.IsNotNull(throughNullable.Presentation?["statusMappings"]);
            Assert.IsNull(throughArray.Presentation?["statusMappings"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.3")]
        public void ReadEnumLabelsThroughNullableEnumAndThroughArray()
        {
            // Arrange
            var block = new SeverityReachBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties;
            var throughNullable = properties.Single(property => property.Identifier == nameof(SeverityReachBlock.Nullable));
            var throughArray = properties.Single(property => property.Identifier == nameof(SeverityReachBlock.Array));

            Assert.AreEqual("Fine", throughNullable.Presentation?["enumLabels"]?["Good"]?.GetValue<string>());
            Assert.AreEqual("Fine", throughArray.Presentation?["enumLabels"]?["Good"]?.GetValue<string>());
            Assert.IsNull(throughNullable.Presentation?["enumLabels"]?["Bad"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.3")]
        public void EnumerateStructFieldsFromConstructorWithMostParameters()
        {
            // Arrange
            var block = new StructShapeBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties.Single().Schema["properties"] as JsonObject;
            CollectionAssert.AreEquivalent(new[] { "left", "right" }, properties!.Select(field => field.Key).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.4")]
        public void RefuseStructWithNoPositionalConstructor()
        {
            // Arrange
            var block = new FieldlessStructBlock();

            // Act / Assert
            var exception = Assert.ThrowsExactly<NotSupportedException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));

            Assert.Contains(nameof(FieldlessStruct), exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.1")]
        public void ReportNestedBlockIdentityWithItsClrNestingSeparator()
        {
            // Arrange
            var block = new IntrospectionOuter.NestedBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.EndsWith("IntrospectionOuter+NestedBlock", result.TypeFullName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-005.1")]
        [TestProperty("spec", "AC-INTRO-005.2")]
        public void IdentifyServicesByBlockClassAndHoldingPropertyName()
        {
            // Arrange
            var block = new CaseDistinctServiceBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.Contains(nameof(CaseDistinctServiceBlock), result.Services.Select(service => service.Identifier).ToList());
            Assert.Contains(nameof(CaseDistinctServiceBlock.Sensor), result.Services.Select(service => service.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-005.5")]
        public void IdentifyMembersByTheirPropertyName()
        {
            // Arrange
            var block = new MemberFlagsBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            CollectionAssert.AreEquivalent(new[]
                                           {
                                               nameof(MemberFlagsBlock.Passphrase), nameof(MemberFlagsBlock.Retained), nameof(MemberFlagsBlock.Excluded),
                                               nameof(MemberFlagsBlock.Plain), nameof(MemberFlagsBlock.Labels),
                                           },
                                           result.Services.Single().Properties.Select(property => property.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-004.2")]
        [TestProperty("spec", "AC-INTRO-005.4")]
        public void ReportServiceInterfaceTypeNamesInTheirDisplayForm()
        {
            // Arrange
            var block = new NestedSurfaceBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The nesting separator is the block identity's alone: every other type name in the document is
            // written the way a reader spells it in source.
            var interfaceNames = result.Services.Single().InterfaceTypeFullNames;
            Assert.Contains("Vion.Dale.Sdk.Test.TestHelpers.NestedSurface.IReading", interfaceNames);
            Assert.EndsWith("NestedSurfaceBlock", result.TypeFullName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-002.8")]
        public void ReportOriginatingExceptionRatherThanReflectionWrapper()
        {
            // Arrange
            var block = new UnsupportedTypeBlock();

            // Act / Assert
            // Configure is reached by reflection, so without the rethrow every refusal above would arrive as
            // "Exception has been thrown by the target of an invocation." — which is what a pack would print.
            Assert.ThrowsExactly<NotSupportedException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.6")]
        public void RefuseMemberTypeOutsideServiceElementSet()
        {
            // Arrange
            var block = new UnsupportedTypeBlock();

            // Act / Assert
            var exception = Assert.ThrowsExactly<NotSupportedException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider));

            Assert.Contains("System.Decimal", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.5")]
        public void ReportWriteOnlyFromServicePropertyDeclarationAlone()
        {
            // Arrange
            var block = new MemberFlagsBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties;
            Assert.IsTrue(properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Passphrase)).Schema["writeOnly"]?.GetValue<bool>());
            Assert.IsNull(properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Plain)).Schema["writeOnly"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-013.1")]
        public void ReportPersistenceFromItsOptIn()
        {
            // Arrange
            var block = new MemberFlagsBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var properties = result.Services.Single().Properties;
            Assert.IsTrue(properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Retained)).Runtime?["persistent"]?.GetValue<bool>());
            Assert.IsNull(properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Excluded)).Runtime);
            Assert.IsNull(properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Plain)).Runtime);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-009.1")]
        public void ReportNoPresentationForMemberDeclaringNone()
        {
            // Arrange
            var block = new MemberFlagsBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.IsNull(result.Services.Single().Properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Plain)).Presentation);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-010.4")]
        public void CarryEveryDeclaredEnumLabelWhateverItsValue()
        {
            // Arrange
            var block = new MemberFlagsBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var labels = result.Services.Single().Properties.Single(property => property.Identifier == nameof(MemberFlagsBlock.Labels)).Presentation?["enumLabels"];
            Assert.AreEqual(string.Empty, labels?[nameof(EdgeLabelEnum.Empty)]?.GetValue<string>());
            Assert.AreEqual("Same", labels?[nameof(EdgeLabelEnum.First)]?.GetValue<string>());
            Assert.AreEqual("Same", labels?[nameof(EdgeLabelEnum.Second)]?.GetValue<string>());
            Assert.IsNull(labels?[nameof(EdgeLabelEnum.Unlabelled)]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.4")]
        public void DecideWritabilityFromImplementingProperty()
        {
            // Arrange
            var block = new WritabilityBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The interface declares the member get-only; the implementation is what a set-value request
            // writes, so its public setter is what decides.
            Assert.IsNull(result.Services.Single().Properties.Single().Schema["readOnly"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.5")]
        public void ReportStructFieldAnnotationsOneLevelDeepOnly()
        {
            // Arrange
            var block = new NestedStructBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var property = result.Services.Single().Properties.Single();
            var child = property.Schema["properties"]?["child"];

            Assert.AreEqual("A", property.Schema["properties"]?["amp"]?["x-unit"]?.GetValue<string>());
            Assert.IsNull(child?["properties"]?["volt"]?["x-unit"]);
            Assert.IsNull(child?["properties"]?["volt"]?["title"]);
            Assert.IsNull(property.Presentation?["fields"]?["child"]?["fields"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-007.1")]
        public void ReportSchemaForEveryMemberOfBothKinds()
        {
            // Arrange
            var block = new DualStreamKindBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var service = result.Services.Single();
            Assert.IsNotNull(service.Properties.Single().Schema);
            Assert.IsNotNull(service.MeasuringPoints.Single().Schema);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.5")]
        public void MintSeparateIdentifierNamespacesForContractAndInterfaceBindings()
        {
            // Arrange
            var block = new CrossKindIdentifierBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // One identifier, two endpoints, no refusal: the document keeps contracts and interfaces in
            // separate arrays, so a name addresses one endpoint of each kind.
            Assert.AreEqual("Shared", result.Contracts.Single().Identifier);
            Assert.AreEqual("Shared", result.Interfaces.Single().Identifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-014.5")]
        public void DistinguishEndpointIdentifiersDifferingOnlyInCase()
        {
            // Arrange
            var block = new CaseDistinctEndpointBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            CollectionAssert.AreEquivalent(new[] { "Relay", "relay" }, result.Contracts.Select(contract => contract.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-015.1")]
        public void ReportInterfaceBindingTypeAndItsMatchingCounterpart()
        {
            // Arrange
            var block = new DistinctInterfaceIdentifierBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var endpoint = result.Interfaces.First(logicInterface => logicInterface.Identifier == "Left_IToggleable");
            CollectionAssert.AreEqual(new[] { typeof(IToggleable).FullName }, endpoint.InterfaceTypeFullNames);
            CollectionAssert.AreEqual(new[] { typeof(IToggler).FullName }, endpoint.MatchingInterfaceTypeFullNames);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-016.8")]
        public void ReportNoRelationHalfForComponentPropertyHoldingNull()
        {
            // Arrange
            var block = new NullComponentEndpointBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The endpoint is described — its identity is type-level — but a null component contributes no
            // service, and a relation half has no service to hang on. The control beside it is the component
            // that does exist: its half is reported.
            var halves = result.Services.SelectMany(service => service.OutwardRelations).Select(relation => relation.InterfaceIdentifier).ToList();

            Assert.Contains("Absent_IToggleable", result.Interfaces.Select(logicInterface => logicInterface.Identifier).ToList());
            CollectionAssert.DoesNotContain(result.Services.Select(service => service.Identifier).ToList(), nameof(NullComponentEndpointBlock.Absent));
            CollectionAssert.DoesNotContain(halves, "Absent_IToggleable");
            Assert.Contains("Present_IToggleable", halves);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-011.5")]
        public void CarryEmptyAuthoredStructFieldTitle()
        {
            // Arrange
            var block = new EmptyStructFieldTitleBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The enum field's schema title is its type identity, so its authored title is re-routed — and an
            // authored empty title is carried there exactly as the same empty title is carried inline on the
            // scalar field beside it.
            var property = result.Services.Single().Properties.Single();
            Assert.AreEqual(string.Empty, property.Presentation?["fields"]?["state"]?["displayName"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, property.Schema["properties"]?["scalar"]?["title"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-003.3")]
        public void ReportComponentServiceMembersInBaseToDerivedDeclarationOrder()
        {
            // Arrange
            var block = new OrderedComponentBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // A component's members are ordered by the component's own declarations, not the block's — a
            // position map belongs to one type's inheritance chain and says nothing about another's.
            var component = result.Services.Single(service => service.Identifier == nameof(OrderedComponentBlock.Component));

            CollectionAssert.AreEqual(new[]
                                      {
                                          nameof(OrderedComponentBase.Alpha), nameof(OrderedComponentBase.Bravo), nameof(OrderedComponent.Charlie),
                                          nameof(OrderedComponent.Delta),
                                      },
                                      component.Properties.Select(property => property.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.7")]
        [DataRow("NullableElements", true, DisplayName = "a member whose elements may be null")]
        [DataRow("NonNullableElements", false, DisplayName = "a member whose elements may not")]
        public void ReportArrayElementNullability(string identifier, bool elementsNullable)
        {
            // Arrange
            var block = new ArrayElementNullabilityBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The element carries its own annotation: without it the outbound codec refuses a null element
            // and the whole publish is dropped, which is the failure the member-level rule already avoids.
            var items = result.Services.Single().Properties.Single(property => property.Identifier == identifier).Schema["items"];
            var expected = elementsNullable ? new[] { "string", "null" } : null;

            if (expected is null)
            {
                Assert.AreEqual("string", items?["type"]?.GetValue<string>());
            }
            else
            {
                CollectionAssert.AreEqual(expected, (items?["type"] as JsonArray)?.Select(node => node!.GetValue<string>()).ToList());
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-008.7")]
        public void ReportArrayElementNullabilityAtEveryNestingDepth()
        {
            // Arrange
            var block = new ArrayElementNullabilityBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            // The flags are one pre-order walk of the member's type, so the walk of the type follows it down.
            var nested = result.Services.Single().Properties.Single(property => property.Identifier == nameof(ArrayElementNullabilityBlock.NestedNullableElements));
            var inner = nested.Schema["items"]?["items"];

            CollectionAssert.AreEqual(new[] { "string", "null" }, (inner?["type"] as JsonArray)?.Select(node => node!.GetValue<string>()).ToList());
        }

        private LogicBlockIntrospectionResult.ServicePropertyInfo GetProperty(string identifier)
        {
            return Introspected().Services.First().Properties.First(property => property.Identifier == identifier);
        }

        private LogicBlockIntrospectionResult.ServiceMeasuringPointInfo GetMeasuringPoint(string identifier)
        {
            return Introspected().Services.First().MeasuringPoints.First(measuringPoint => measuringPoint.Identifier == identifier);
        }

        private Dictionary<string, object> GetAnnotations()
        {
            return Introspected().Annotations;
        }

        /// <summary>The standing fixture, introspected fresh: each test owns its own Act.</summary>
        private LogicBlockIntrospectionResult Introspected()
        {
            return LogicBlockIntrospection.IntrospectLogicBlock(new TestLogicBlock(), _serviceProvider);
        }
    }
}