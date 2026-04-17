using System;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Introspection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.Introspection;

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
        [ServiceProperty("Leistung", "kW")]
        [Importance(Importance.Primary)]
        [Display(group: "Energy")]
        public double ActivePower { get; set; }

        [ServiceProperty(unit: "kWh")]
        [ServiceMeasuringPoint(unit: "kWh")]
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

        [ServiceMeasuringPoint("Temperatur", "°C")]
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

            Assert.AreEqual("Primary", activePower.Annotations["Importance"]);
        }

        [TestMethod]
        public void ReadCategoryAnnotation()
        {
            var maxPower = GetProperty("MaxPower");

            Assert.AreEqual("Configuration", maxPower.Annotations["Category"]);
        }

        [TestMethod]
        public void ReadDisplayGroupAnnotation()
        {
            var activePower = GetProperty("ActivePower");

            Assert.AreEqual("Energy", activePower.Annotations["Group"]);
        }

        [TestMethod]
        public void ReadDisplayNameOverridingDefaultName()
        {
            var brightness = GetProperty("Brightness");

            Assert.AreEqual("Helligkeit", brightness.Annotations["DefaultName"]);
        }

        [TestMethod]
        public void ReadDisplayOrderAnnotation()
        {
            var brightness = GetProperty("Brightness");

            Assert.AreEqual(5, brightness.Annotations["Order"]);
        }

        [TestMethod]
        public void ReadUIHintAnnotation()
        {
            var brightness = GetProperty("Brightness");

            Assert.AreEqual("slider", brightness.Annotations["UIHint"]);
        }

        [TestMethod]
        public void ReadStatusIndicatorAnnotation()
        {
            var connectionState = GetProperty("ConnectionState");

            Assert.IsTrue((bool)connectionState.Annotations["StatusIndicator"]);
        }

        [TestMethod]
        public void ReadStatusMappingsFromStatusIndicatorProperty()
        {
            var connectionState = GetProperty("ConnectionState");
            var mappings = (List<Dictionary<string, object>>)connectionState.Annotations["StatusMappings"];

            Assert.HasCount(3, mappings);

            var unknown = mappings.First(m => (string)m["Name"] == "Unknown");
            Assert.AreEqual("Neutral", unknown["Severity"]);
            Assert.AreEqual("Unbekannt", unknown["DefaultName"]);

            var connected = mappings.First(m => (string)m["Name"] == "Connected");
            Assert.AreEqual("Success", connected["Severity"]);
            Assert.AreEqual("Verbunden", connected["DefaultName"]);

            var disconnected = mappings.First(m => (string)m["Name"] == "Disconnected");
            Assert.AreEqual("Error", disconnected["Severity"]);
            Assert.AreEqual("Getrennt", disconnected["DefaultName"]);
        }

        [TestMethod]
        public void ReadEnumValuesWithDefaultNames()
        {
            var mode = GetProperty("Mode");
            var enumValues = (List<Dictionary<string, object>>)mode.Annotations["EnumValues"];

            Assert.HasCount(2, enumValues);

            var auto = enumValues.First(e => (string)e["Name"] == "Auto");
            Assert.AreEqual(0, auto["Value"]);
            Assert.AreEqual("Automatik", auto["DefaultName"]);

            var manual = enumValues.First(e => (string)e["Name"] == "Manual");
            Assert.AreEqual(1, manual["Value"]);
            Assert.AreEqual("Manuell", manual["DefaultName"]);
        }

        [TestMethod]
        public void ReadServicePropertyAnnotations()
        {
            var activePower = GetProperty("ActivePower");

            Assert.AreEqual("Leistung", activePower.Annotations["DefaultName"]);
            Assert.AreEqual("kW", activePower.Annotations["Unit"]);
        }

        [TestMethod]
        public void ReadMeasuringPointAnnotations()
        {
            var service = _result.Services.First();
            var temperature = service.MeasuringPoints.First(m => m.Identifier == "Temperature");

            Assert.AreEqual("Temperatur", temperature.Annotations["DefaultName"]);
            Assert.AreEqual("°C", temperature.Annotations["Unit"]);
        }

        [TestMethod]
        public void ReadUiAnnotationsOnMeasuringPoints()
        {
            var service = _result.Services.First();
            var energyTotal = service.MeasuringPoints.First(m => m.Identifier == "EnergyTotal");

            Assert.AreEqual("Secondary", energyTotal.Annotations["Importance"]);
            Assert.AreEqual("Energy", energyTotal.Annotations["Group"]);
        }

        [TestMethod]
        public void NotIncludeAbsentAnnotationKeys()
        {
            // MaxPower has [Category] but no [Importance], [Display], [UIHint]
            var maxPower = GetProperty("MaxPower");

            Assert.IsFalse(maxPower.Annotations.ContainsKey("Importance"));
            Assert.IsFalse(maxPower.Annotations.ContainsKey("Group"));
            Assert.IsFalse(maxPower.Annotations.ContainsKey("UIHint"));
            Assert.IsFalse(maxPower.Annotations.ContainsKey("Order"));
            Assert.IsFalse(maxPower.Annotations.ContainsKey("StatusIndicator"));
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