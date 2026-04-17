using Vion.Dale.DevHost.Mocking;
using Vion.Dale.DevHost.Web.Api.Dtos;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Messages;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Utils;
using Vion.Contracts.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.DevHost.Web.Services
{
    public class DevHostStateProvider : IDevHostStateProvider
    {
        private readonly IActorSystem _actorSystem;

        private readonly DevConfiguration _configuration;

        private readonly Dictionary<string, LogicBlockIntrospectionResult> _logicBlockIntrospectionResults = new();

        private readonly IServiceProvider _serviceProvider;

        public DevHostStateProvider(DevConfiguration configuration, IActorSystem actorSystem, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _actorSystem = actorSystem;
            _serviceProvider = serviceProvider;

            IntrospectAllLogicBlocks();
        }

        public Task<ConfigurationOutput> GetConfigurationAsync()
        {
            return Task.FromResult(new ConfigurationOutput
                                   {
                                       LogicBlocks = _configuration.LogicBlocks
                                                                   .Select(lb =>
                                                                           {
                                                                               var meta = _logicBlockIntrospectionResults[lb.Id];

                                                                               return new ConfigurationOutput.LogicBlock
                                                                                      {
                                                                                          Id = lb.Id,
                                                                                          Name = lb.Name,
                                                                                          Services = lb.Services
                                                                                                       .Select(s =>
                                                                                                               {
                                                                                                                   var serviceInfo =
                                                                                                                       meta.Services
                                                                                                                           .Single(si => si.Identifier == s
                                                                                                                               .Identifier);

                                                                                                                   return new ConfigurationOutput.Service
                                                                                                                       {
                                                                                                                           Id = s.Id,
                                                                                                                           Identifier = s.Identifier,
                                                                                                                           ServiceProperties =
                                                                                                                               serviceInfo.Properties
                                                                                                                                   .Select(sp =>
                                                                                                                                       new
                                                                                                                                       ConfigurationOutput
                                                                                                                                       .ServiceProperty
                                                                                                                                       {
                                                                                                                                           Identifier =
                                                                                                                                               sp
                                                                                                                                                   .Identifier,
                                                                                                                                           Writable =
                                                                                                                                               sp
                                                                                                                                                   .Writable,
                                                                                                                                           ServiceElementType =
                                                                                                                                               sp
                                                                                                                                                   .ServiceElementType,
                                                                                                                                           Annotations =
                                                                                                                                               sp
                                                                                                                                                   .Annotations,
                                                                                                                                       })
                                                                                                                                   .ToList(),
                                                                                                                           ServiceMeasuringPoints =
                                                                                                                               serviceInfo
                                                                                                                                   .MeasuringPoints
                                                                                                                                   .Select(smp =>
                                                                                                                                       new
                                                                                                                                       ConfigurationOutput
                                                                                                                                       .ServiceMeasuringPoint
                                                                                                                                       {
                                                                                                                                           Identifier =
                                                                                                                                               smp
                                                                                                                                                   .Identifier,
                                                                                                                                           ServiceElementType =
                                                                                                                                               smp
                                                                                                                                                   .ServiceElementType,
                                                                                                                                           Annotations =
                                                                                                                                               smp
                                                                                                                                                   .Annotations,
                                                                                                                                       })
                                                                                                                                   .ToList(),
                                                                                                                       };
                                                                                                               })
                                                                                                       .ToList(),
                                                                                          Contracts = meta.Contracts
                                                                                                          .Select(c => new ConfigurationOutput.LogicBlockContract
                                                                                                                       {
                                                                                                                           Identifier = c.Identifier,
                                                                                                                           MatchingContractType =
                                                                                                                               c.MatchingContractType,
                                                                                                                           Annotations = c.Annotations,
                                                                                                                       })
                                                                                                          .ToList(),
                                                                                          ContractMappings = lb.ContractMappings
                                                                                                               .Select(cm => new ConfigurationOutput.ContractMapping
                                                                                                                           {
                                                                                                                               ContractIdentifier =
                                                                                                                                   cm.ContractIdentifier,
                                                                                                                               MappedServiceProviderIdentifier =
                                                                                                                                   cm.ServiceProviderIdentifier,
                                                                                                                               MappedServiceIdentifier =
                                                                                                                                   cm.ServiceIdentifier,
                                                                                                                               MappedContractIdentifier =
                                                                                                                                   cm.ContractEndpointIdentifier,
                                                                                                                           })
                                                                                                               .ToList(),
                                                                                      };
                                                                           })
                                                                   .ToList(),

                                       InterfaceMappings = _configuration.InterfaceMappings
                                                                         .Select(im => new ConfigurationOutput.InterfaceMapping
                                                                                       {
                                                                                           SourceLogicBlockId = im.SourceLogicBlockId,
                                                                                           SourceLogicBlockName = im.SourceLogicBlockName,
                                                                                           SourceInterfaceIdentifier = im.SourceInterfaceIdentifier,
                                                                                           TargetLogicBlockId = im.TargetLogicBlockId,
                                                                                           TargetLogicBlockName = im.TargetLogicBlockName,
                                                                                           TargetInterfaceIdentifier = im.TargetInterfaceIdentifier,
                                                                                       })
                                                                         .ToList(),

                                       ServiceProviders = _configuration.ServiceProviders
                                                                        .Select(sp => new ConfigurationOutput.ServiceProvider
                                                                                      {
                                                                                          Id = sp.Id,
                                                                                          Services = sp.Services
                                                                                                       .Select(svc =>
                                                                                                                   new ConfigurationOutput.
                                                                                                                   ServiceProviderService
                                                                                                                   {
                                                                                                                       Identifier = svc.Identifier,
                                                                                                                       Contracts = svc.Contracts
                                                                                                                           .Select(c =>
                                                                                                                               new
                                                                                                                               ConfigurationOutput
                                                                                                                               .ServiceProviderContract
                                                                                                                               {
                                                                                                                                   Identifier =
                                                                                                                                       c.Identifier,
                                                                                                                                   ContractType =
                                                                                                                                       c.ContractType,
                                                                                                                               })
                                                                                                                           .ToList(),
                                                                                                                   })
                                                                                                       .ToList(),
                                                                                      })
                                                                        .ToList(),
                                   });
        }

        public Task SetDigitalInputValueAsync(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value)
        {
            var halHandlerRef = _actorSystem.LookupByName(nameof(DigitalInputHandler));
            _actorSystem.SendTo(halHandlerRef, new MockSetDigitalInputMessage(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));

            return Task.CompletedTask;
        }

        public Task SetAnalogInputValueAsync(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value)
        {
            var halHandlerRef = _actorSystem.LookupByName(nameof(AnalogInputHandler));
            _actorSystem.SendTo(halHandlerRef, new MockSetAnalogInputMessage(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));

            return Task.CompletedTask;
        }

        public Task SetServicePropertyValueAsync(string serviceIdentifier, string propertyIdentifier, object value)
        {
            var logicBlockConfig = _configuration.LogicBlocks.First(lb => lb.Services.Any(s => s.Id == serviceIdentifier));
            var service = logicBlockConfig.Services.First(s => s.Id == serviceIdentifier);

            var serviceInfo = _logicBlockIntrospectionResults[logicBlockConfig.Id].Services.First(s => s.Identifier == service.Identifier);
            var propertyInfo = serviceInfo.Properties.First(p => p.Identifier == propertyIdentifier);

            var typedValue = ConvertJsonValueToTypedValue(value, propertyInfo.ServiceElementType, propertyInfo.TypeFullName);

            var logicBlockActorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id));
            var handlerRef = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));
            _actorSystem.SendTo(handlerRef,
                                new MockSetServicePropertyValue(logicBlockActorRef,
                                                                new SetServicePropertyValueRequest(new ServiceIdentifier(serviceIdentifier), propertyIdentifier, typedValue)));

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PublishAllStatesAsync()
        {
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalInputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalOutputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogInputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogOutputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(MockServicePropertyHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(MockServiceMeasuringPointHandler)), new MockPublishAllStatesMessage());

            return Task.CompletedTask;
        }

        private void IntrospectAllLogicBlocks()
        {
            foreach (var logicBlockConfig in _configuration.LogicBlocks)
            {
                // Get an instance of the logic block from DI
                var logicBlock = _serviceProvider.GetService(logicBlockConfig.LogicBlockType) as LogicBlockBase;

                if (logicBlock != null)
                {
                    var introspectionResult = LogicBlockIntrospection.IntrospectLogicBlock(logicBlock, _serviceProvider);

                    // generate serviceIds
                    foreach (var service in introspectionResult.Services)
                    {
                        logicBlockConfig.Services.Add(new DevServiceConfig
                                                      {
                                                          Id = Guid.NewGuid().ToString(),
                                                          Identifier = service.Identifier,
                                                      });
                    }

                    _logicBlockIntrospectionResults[logicBlockConfig.Id] = introspectionResult;
                }
            }
        }

        private static object ConvertJsonValueToTypedValue(object value, string serviceElementType, string typeFullName)
        {
            var jsonElement = (JsonElement)value;
            return serviceElementType switch
            {
                ServiceElementTypes.String => jsonElement.GetString() ?? "",
                ServiceElementTypes.Integer => ConvertToIntegerOrEnum(jsonElement, typeFullName),
                ServiceElementTypes.Number => jsonElement.GetDouble(),
                ServiceElementTypes.Bool => jsonElement.GetBoolean(),
                ServiceElementTypes.DateTime => jsonElement.GetDateTime(),
                ServiceElementTypes.Duration => TimeSpan.Parse(jsonElement.GetString() ?? throw new InvalidOperationException($"Invalid duration format {jsonElement}")),
                _ => throw new InvalidOperationException($"Unsupported type: {serviceElementType}"),
            };
        }

        private static object ConvertToIntegerOrEnum(JsonElement jsonElement, string typeFullName)
        {
            var intValue = jsonElement.GetInt32();

            // Try to resolve the type and check if it's an enum
            var type = Type.GetType(typeFullName);
            if (type is { IsEnum: true })
            {
                // Convert integer to enum
                return Enum.ToObject(type, intValue);
            }

            // Otherwise return as int32
            return intValue;
        }
    }
}