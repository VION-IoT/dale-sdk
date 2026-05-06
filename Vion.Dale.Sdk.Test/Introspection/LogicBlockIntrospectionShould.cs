using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Test.Introspection
{
    [Contract(BetweenInterface = "ITestProvider",
              AndInterface = "ITestConsumer",
              BetweenDefaultName = "Provider",
              AndDefaultName = "Consumer",
              Direction = ContractDirection.BetweenToAnd)]
    public static class TestDirectionalContract
    {
        [Command(From = "ITestProvider", To = "ITestConsumer")]
        public readonly record struct TestCommand(string Data);
    }

    [Service("BetweenDevice")]
    [InterfaceDependency(typeof(ITestProvider),
                         "Quelle",
                         CardinalityType.Optional,
                         SharingType.Exclusive,
                         DependencyCreationType.AllowCreateNew,
                         "provider-tag")]
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

    [Service("AndDevice")]
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
        [EnumValueInfo("Unbekannt")]
        [StatusSeverity(StatusSeverity.Neutral)]
        Unknown,

        [EnumValueInfo("Verbunden")]
        [StatusSeverity(StatusSeverity.Success)]
        Connected,

        [EnumValueInfo("Getrennt")]
        [StatusSeverity(StatusSeverity.Error)]
        Disconnected,
    }

    public enum OperatingMode
    {
        [EnumValueInfo("Automatik")]
        Auto,

        [EnumValueInfo("Manuell")]
        Manual,
    }

    [Service("TestDevice")]
    [LogicBlockInfo("Testgerät", "device-line")]
    public class TestLogicBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Leistung", Unit = "kW")]
        [Importance(Importance.Primary)]
        [Display(group: "Energy")]
        public double ActivePower { get; set; }

        [ServiceProperty(Unit = "kWh")]
        [ServiceMeasuringPoint(Unit = "kWh")]
        [Importance(Importance.Secondary)]
        [Display(group: "Energy")]
        public double EnergyTotal { get; private set; }

        [ServiceProperty]
        [Category(PropertyCategory.Configuration)]
        public double MaxPower { get; set; } = 10;

        [ServiceProperty]
        [StatusIndicator]
        public DeviceConnectionState ConnectionState { get; private set; }

        [ServiceProperty]
        public OperatingMode Mode { get; set; }

        [ServiceProperty]
        [UIHint("slider")]
        [Display("Helligkeit", "Visuals", 5)]
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

    [Service("ContractDevice")]
    public class ContractTestLogicBlock : LogicBlockBase
    {
        [ServiceProviderContract("Button",
                                 "Taster",
                                 CardinalityType.Optional,
                                 SharingType.Exclusive,
                                 "input",
                                 "sensor")]
        public IDigitalInput Button { get; set; } = null!;

        [ServiceProviderContract("LED")]
        public IDigitalOutput Led { get; set; } = null!;

        [ServiceProviderContract]
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

    [Service("PlainDevice")]
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
        private readonly LogicBlockIntrospectionResult _result;

        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        public LogicBlockIntrospectionShould()
        {
            var logicBlock = new TestLogicBlock();
            _result = LogicBlockIntrospection.IntrospectLogicBlock(logicBlock, _serviceProvider);
        }

        [TestMethod]
        public void ReadBlockLevelAnnotations()
        {
            Assert.AreEqual("Testgerät", _result.Annotations["DefaultName"]);
            Assert.AreEqual("device-line", _result.Annotations["Icon"]);
        }

        [TestMethod]
        public void ReturnEmptyAnnotationsWhenNoLogicBlockInfoAttribute()
        {
            var block = new PlainLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            Assert.IsEmpty(result.Annotations);
        }

        [TestMethod]
        public void ReadImportanceAnnotation()
        {
            var activePower = GetProperty("ActivePower");

            // Importance maps to presentation.importance
            Assert.AreEqual("Primary", activePower.Presentation?["importance"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadCategoryAnnotation()
        {
            var maxPower = GetProperty("MaxPower");

            // Category maps to presentation.category
            Assert.AreEqual("Configuration", maxPower.Presentation?["category"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadDisplayGroupAnnotation()
        {
            var activePower = GetProperty("ActivePower");

            // Group maps to presentation.group
            Assert.AreEqual("Energy", activePower.Presentation?["group"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadDisplayNameOverridingDefaultName()
        {
            var brightness = GetProperty("Brightness");

            // DisplayName maps to presentation.displayName
            Assert.AreEqual("Helligkeit", brightness.Presentation?["displayName"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadDisplayOrderAnnotation()
        {
            var brightness = GetProperty("Brightness");

            // Order maps to presentation.order
            Assert.AreEqual(5, brightness.Presentation?["order"]?.GetValue<int>());
        }

        [TestMethod]
        public void ReadUIHintAnnotation()
        {
            var brightness = GetProperty("Brightness");

            // UIHint maps to presentation.uiHint
            Assert.AreEqual("slider", brightness.Presentation?["uiHint"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadStatusIndicatorAnnotation()
        {
            var connectionState = GetProperty("ConnectionState");

            // StatusIndicator presence is indicated by statusMappings being populated.
            Assert.IsNotNull(connectionState.Presentation?["statusMappings"]);
        }

        [TestMethod]
        public void ReadStatusMappingsFromStatusIndicatorProperty()
        {
            var connectionState = GetProperty("ConnectionState");
            var mappings = connectionState.Presentation?["statusMappings"] as JsonObject;

            Assert.IsNotNull(mappings);

            // New shape: statusMappings is a flat object mapping member name → severity string.
            Assert.AreEqual("neutral", mappings["Unknown"]?.GetValue<string>());
            Assert.AreEqual("success", mappings["Connected"]?.GetValue<string>());
            Assert.AreEqual("error", mappings["Disconnected"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadEnumMembersInSchema()
        {
            var mode = GetProperty("Mode");

            // New shape: enum members are inline in schema.enum as an array of name strings.
            // Integer values are NOT on the wire per spec §5.1.
            var enumArray = mode.Schema["enum"] as JsonArray;
            Assert.IsNotNull(enumArray);
            Assert.HasCount(2, enumArray);
            Assert.IsTrue(enumArray.Any(e => e?.GetValue<string>() == "Auto"));
            Assert.IsTrue(enumArray.Any(e => e?.GetValue<string>() == "Manual"));
        }

        [TestMethod]
        public void ReadServicePropertySchemaAnnotations()
        {
            var activePower = GetProperty("ActivePower");

            // Title maps to schema.title; Unit maps to schema["x-unit"].
            Assert.AreEqual("Leistung", activePower.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("kW", activePower.Schema["x-unit"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadMeasuringPointSchemaAnnotations()
        {
            var service = _result.Services.First();
            var temperature = service.MeasuringPoints.First(m => m.Identifier == "Temperature");

            // Title and Unit map to schema fields.
            Assert.AreEqual("Temperatur", temperature.Schema["title"]?.GetValue<string>());
            Assert.AreEqual("°C", temperature.Schema["x-unit"]?.GetValue<string>());
        }

        [TestMethod]
        public void ReadUiAnnotationsOnMeasuringPoints()
        {
            var service = _result.Services.First();
            var energyTotal = service.MeasuringPoints.First(m => m.Identifier == "EnergyTotal");

            // EnergyTotal has [Importance(Secondary)] and [Display(group: "Energy")] from the
            // logic-block property, which maps to presentation.
            Assert.AreEqual("Secondary", energyTotal.Presentation?["importance"]?.GetValue<string>());
            Assert.AreEqual("Energy", energyTotal.Presentation?["group"]?.GetValue<string>());
        }

        [TestMethod]
        public void NotIncludeAbsentPresentationKeys()
        {
            // MaxPower has [Category] but no [Importance], [Display], [UIHint].
            var maxPower = GetProperty("MaxPower");

            Assert.IsNull(maxPower.Presentation?["importance"]);
            Assert.IsNull(maxPower.Presentation?["group"]);
            Assert.IsNull(maxPower.Presentation?["uiHint"]);
            Assert.IsNull(maxPower.Presentation?["order"]);

            // statusMappings should be absent (no [StatusIndicator]).
            Assert.IsNull(maxPower.Presentation?["statusMappings"]);
        }

        [TestMethod]
        public void ReadContractNameOnBetweenSideInterface()
        {
            var block = new BetweenSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("TestDirectionalContract", iface.Annotations["ContractName"]);
        }

        [TestMethod]
        public void ResolveOutboundDirectionOnBetweenSide()
        {
            var block = new BetweenSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("Outbound", iface.Annotations["ArrowDirection"]);
        }

        [TestMethod]
        public void ResolveInboundDirectionOnAndSide()
        {
            var block = new AndSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("Inbound", iface.Annotations["ArrowDirection"]);
        }

        [TestMethod]
        public void ReadRoleDefaultNamesOnBetweenSide()
        {
            var block = new BetweenSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("Provider", iface.Annotations["RoleDefaultName"]);
            Assert.AreEqual("Consumer", iface.Annotations["MatchingRoleDefaultName"]);
        }

        [TestMethod]
        public void ReadRoleDefaultNamesOnAndSide()
        {
            var block = new AndSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("Consumer", iface.Annotations["RoleDefaultName"]);
            Assert.AreEqual("Provider", iface.Annotations["MatchingRoleDefaultName"]);
        }

        [TestMethod]
        public void ReadInterfaceDependencyAnnotations()
        {
            var block = new BetweenSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            Assert.AreEqual("Quelle", iface.Annotations["DefaultName"]);
            Assert.AreEqual(CardinalityType.Optional, iface.Annotations["Cardinality"]);
            Assert.AreEqual(SharingType.Exclusive, iface.Annotations["Sharing"]);
            Assert.AreEqual(DependencyCreationType.AllowCreateNew, iface.Annotations["CreationType"]);
        }

        [TestMethod]
        public void ReadInterfaceDependencyTagsAnnotation()
        {
            var block = new BetweenSideTestBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var iface = result.Interfaces.First();
            var tags = (List<string>)iface.Annotations["Tags"];
            Assert.HasCount(1, tags);
            Assert.Contains("provider-tag", tags);
        }

        [TestMethod]
        public void IntrospectContractsWithIdentifiers()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            Assert.HasCount(3, result.Contracts);
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Button"));
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "LED"));
            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Temperature"));
        }

        [TestMethod]
        public void IntrospectContractMatchingContractType()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");
            Assert.AreEqual("DigitalInput", button.MatchingContractType);

            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.AreEqual("DigitalOutput", led.MatchingContractType);

            var temperature = result.Contracts.First(c => c.Identifier == "Temperature");
            Assert.AreEqual("AnalogInput", temperature.MatchingContractType);
        }

        [TestMethod]
        public void IntrospectContractDefaultNameAnnotation()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");
            Assert.AreEqual("Taster", button.Annotations["DefaultName"]);

            var led = result.Contracts.First(c => c.Identifier == "LED");
            Assert.IsFalse(led.Annotations.ContainsKey("DefaultName"));
        }

        [TestMethod]
        public void IntrospectContractCardinalityAndSharingAnnotations()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");
            Assert.AreEqual(CardinalityType.Optional, button.Annotations["Cardinality"]);
            Assert.AreEqual(SharingType.Exclusive, button.Annotations["Sharing"]);
        }

        [TestMethod]
        public void IntrospectContractTagsAnnotation()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var button = result.Contracts.First(c => c.Identifier == "Button");
            var tags = (List<string>)button.Annotations["Tags"];
            Assert.HasCount(2, tags);
            Assert.Contains("input", tags);
            Assert.Contains("sensor", tags);
        }

        [TestMethod]
        public void UsePropertyNameAsContractIdentifierWhenNotSpecified()
        {
            var block = new ContractTestLogicBlock();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            Assert.IsTrue(result.Contracts.Any(c => c.Identifier == "Temperature"));
        }

        private LogicBlockIntrospectionResult.ServicePropertyInfo GetProperty(string identifier)
        {
            var service = _result.Services.First();
            return service.Properties.First(p => p.Identifier == identifier);
        }
    }
}
