using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Contracts.Codec;
using Vion.Contracts.Introspection;
using Vion.Contracts.TypeRef;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.DevHost.Web.Api.Dtos;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

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
                                                                                          Annotations = meta.Annotations,
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
                                                                                                                                           Schema =
                                                                                                                                               sp
                                                                                                                                                   .Schema,
                                                                                                                                           Presentation =
                                                                                                                                               sp
                                                                                                                                                   .Presentation,
                                                                                                                                           Runtime =
                                                                                                                                               sp
                                                                                                                                                   .Runtime,
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
                                                                                                                                           Schema =
                                                                                                                                               smp
                                                                                                                                                   .Schema,
                                                                                                                                           Presentation =
                                                                                                                                               smp
                                                                                                                                                   .Presentation,
                                                                                                                                       })
                                                                                                                                   .ToList(),
                                                                                                                       };
                                                                                                               })
                                                                                                       .ToList(),
                                                                                          Interfaces = meta.Interfaces
                                                                                                           .Select(i => new ConfigurationOutput.LogicBlockInterface
                                                                                                                        {
                                                                                                                            Identifier = i.Identifier,
                                                                                                                            Annotations = i.Annotations,
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

            // TODO(rich-types): Use PropertyValueCodec + Schema to decode value once the codec lands.
            // For now, fall back to a best-effort conversion based on the JSON Schema "type" keyword
            // plus reflection on the logic block to recover the precise CLR target type (needed for enum parse).
            var targetClrType = logicBlockConfig.LogicBlockType.GetProperty(propertyIdentifier)?.PropertyType;
            var typedValue = ConvertJsonValueToTypedValue(value, propertyInfo.Schema, targetClrType);

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

        /// <summary>
        ///     JSON → typed CLR conversion for the DevHost set-property path.
        ///     Re-parses the per-property JSON Schema into a <see cref="TypeRef" /> and delegates to
        ///     <see cref="PropertyValueCodec.JsonToClr" /> — the same code path the runtime uses for
        ///     decoded wire payloads. Replaces a previously hand-rolled converter that duplicated
        ///     the codec's logic (numeric narrowing per format, enum-name parsing, struct
        ///     decomposition, ImmutableArray construction).
        /// </summary>
        private static object? ConvertJsonValueToTypedValue(object? value, JsonNode schema, Type? targetClrType)
        {
            if (targetClrType is null)
            {
                throw new InvalidOperationException(
                    "DevHostStateProvider: targetClrType is required to decode a JSON value into a typed CLR value. " +
                    "The LogicBlock property is missing or the reflection lookup failed upstream.");
            }

            // The codec wants a TypeRef (the typed shape), not the JSON Schema document. Re-parse on
            // each call — DevHost is a dev tool with low-traffic SetPropertyValue calls; the parse
            // overhead is negligible compared to the round-trip through the actor system.
            var typeRef = TypeSchemaSerialization.FromJsonSchema(schema).Type;

            // Normalize the value into a JsonNode. STJ's `object` model binding produces a
            // JsonElement for typed JSON values and a literal C# null when the body was `null`.
            JsonNode? json = value switch
            {
                null => null,
                JsonElement je when je.ValueKind == JsonValueKind.Null => null,
                JsonElement je => JsonNode.Parse(je.GetRawText()),
                _ => JsonValue.Create(value),
            };

            return PropertyValueCodec.JsonToClr(json, typeRef, targetClrType);
        }

        // (The previous ~200 lines of hand-rolled converter helpers — DecodeStruct, DecodeArray,
        // ListToImmutableArray, ToCamelCase, ExtractEffectiveType, IsNullableSchema, NarrowInteger —
        // are gone. PropertyValueCodec.JsonToClr (public from Vion.Contracts 0.7.1) carries the
        // equivalent logic.)
    }
}
