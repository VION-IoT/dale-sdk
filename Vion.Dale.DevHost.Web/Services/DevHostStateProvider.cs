using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Contracts.Introspection;
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
        ///     Best-effort JSON → CLR conversion for the DevHost set-property path.
        ///     Decodes based on the property's JSON Schema <c>type</c> + <c>format</c> + <c>enum</c> keywords,
        ///     using <paramref name="targetClrType" /> (when available) to widen/narrow correctly and to parse
        ///     enum member names back to typed values.
        ///     TODO(rich-types): Replace with <c>PropertyValueCodec</c> once that decode path is wired up here.
        /// </summary>
        private static object? ConvertJsonValueToTypedValue(object? value, JsonNode schema, Type? targetClrType)
        {
            // schema["type"] can be a JsonValue (single primitive type) or a JsonArray (e.g. ["integer", "null"] for nullable).
            // Pull the non-null variant when it's an array.
            var typeStr = ExtractEffectiveType(schema) ?? "string";
            var formatStr = schema["format"]?.GetValue<string>();
            var isNullable = IsNullableSchema(schema);
            var hasEnum = schema["enum"] is JsonArray;

            // Two distinct null shapes to handle:
            //   1. value is C# null (the JSON body had a literal `null`; STJ binds it directly to null when the
            //      controller's input type is `object`). The cast `(JsonElement)null` would throw NRE.
            //   2. value is a JsonElement whose ValueKind is JsonValueKind.Null (the JSON body had a JsonElement
            //      wrapping null). The cast succeeds; ValueKind check catches it.
            // Both cases mean "set the property to null"; both require the schema to be nullable.
            if (value is null || (value is JsonElement je && je.ValueKind == JsonValueKind.Null))
            {
                if (!isNullable)
                {
                    throw new InvalidOperationException($"Property schema is not nullable but JSON value is null. Schema: {schema.ToJsonString()}");
                }

                return null;
            }

            var jsonElement = (JsonElement)value;

            // For nullable value-types, set targetClrType to the non-Nullable underlying so the conversion picks the right kind.
            var underlyingTarget = targetClrType is null ? null : Nullable.GetUnderlyingType(targetClrType) ?? targetClrType;

            // Enum: members are NAME STRINGS on the wire. Use the CLR target enum type to parse back to a typed enum value.
            // This is required because LT9 dropped the binder-side ad-hoc int→enum conversion; the codec / decoder is responsible for producing the typed value now.
            if (hasEnum && underlyingTarget is { IsEnum: true })
            {
                var name = jsonElement.GetString() ?? throw new InvalidOperationException($"Enum value must be a JSON string member name; got {jsonElement.ValueKind}");
                return Enum.Parse(underlyingTarget, name, false);
            }

            return typeStr switch
            {
                "string" when formatStr == "date-time" => jsonElement.GetDateTime(),
                "string" when formatStr == "duration" => TimeSpan.Parse(jsonElement.GetString() ?? throw new InvalidOperationException($"Invalid duration format {jsonElement}")),
                "string" => jsonElement.GetString() ?? string.Empty,
                "boolean" => jsonElement.GetBoolean(),

                // Integer narrowing: widen via GetInt64 then narrow to the target CLR type. The format keyword is
                // a hint for range checking; the actual setter coercion is driven by underlyingTarget.
                "integer" => NarrowInteger(jsonElement, underlyingTarget, formatStr),

                // Number narrowing: same idea for float/double.
                "number" => formatStr == "float" || underlyingTarget == typeof(float) ? jsonElement.GetSingle() : jsonElement.GetDouble(),

                // Object → readonly record struct via reflection on the primary positional ctor.
                "object" when underlyingTarget is not null => DecodeStruct(jsonElement, schema, underlyingTarget),

                // Array → ImmutableArray<T> via reflection.
                "array" when underlyingTarget is not null => DecodeArray(jsonElement, schema, underlyingTarget),

                // Fallback: pass the JSON string through verbatim.
                _ => jsonElement.GetString() ?? string.Empty,
            };
        }

        /// <summary>
        ///     Decodes a JSON object into a <c>readonly record struct</c> instance by walking the struct's primary
        ///     positional constructor and binding each parameter to the matching JSON property (camelCase by name).
        ///     Recursively converts nested values via <see cref="ConvertJsonValueToTypedValue" />.
        /// </summary>
        private static object DecodeStruct(JsonElement element, JsonNode schema, Type structType)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Expected JSON object for struct '{structType.Name}', got {element.ValueKind}");
            }

            var ctor = structType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault(c => c.GetParameters().Length > 0) ??
                       throw new InvalidOperationException($"Struct '{structType.FullName}' has no positional constructor");

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var fieldName = ToCamelCase(param.Name!);
                var fieldSchema = schema["properties"]?[fieldName] ??
                                  throw new InvalidOperationException($"Struct '{structType.Name}' field '{fieldName}' not present in schema.properties");

                if (!element.TryGetProperty(fieldName, out var fieldElement))
                {
                    throw new InvalidOperationException($"JSON for struct '{structType.Name}' is missing required field '{fieldName}'");
                }

                args[i] = ConvertJsonValueToTypedValue(fieldElement, fieldSchema, param.ParameterType);
            }

            return ctor.Invoke(args)!;
        }

        /// <summary>
        ///     Decodes a JSON array into an <see cref="System.Collections.Immutable.ImmutableArray{T}" />
        ///     where T is the element type of <paramref name="arrayType" />. Recursively converts each element
        ///     via <see cref="ConvertJsonValueToTypedValue" /> using the schema's <c>items</c> child schema.
        /// </summary>
        private static object DecodeArray(JsonElement element, JsonNode schema, Type arrayType)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Expected JSON array, got {element.ValueKind}");
            }

            if (!arrayType.IsGenericType || arrayType.GetGenericTypeDefinition() != typeof(ImmutableArray<>))
            {
                throw new InvalidOperationException($"Array target CLR type must be ImmutableArray<T>, got '{arrayType.FullName}'");
            }

            var elementType = arrayType.GetGenericArguments()[0];
            var itemsSchema = schema["items"] ?? throw new InvalidOperationException("Array schema missing 'items'");

            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var item in element.EnumerateArray())
            {
                var itemValue = ConvertJsonValueToTypedValue(item, itemsSchema, elementType);
                list.Add(itemValue);
            }

            // Convert List<T> → ImmutableArray<T> via a generic helper invoked by reflection.
            // (Avoids the awkward CreateRange overload-resolution dance.)
            var helper = typeof(DevHostStateProvider).GetMethod(nameof(ListToImmutableArray), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(elementType);
            return helper.Invoke(null, new object[] { list })!;
        }

        private static ImmutableArray<T> ListToImmutableArray<T>(IEnumerable<T> items)
        {
            return ImmutableArray.CreateRange(items);
        }

        private static string ToCamelCase(string s)
        {
            if (string.IsNullOrEmpty(s) || char.IsLower(s[0]))
            {
                return s;
            }

            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        private static string? ExtractEffectiveType(JsonNode schema)
        {
            var typeNode = schema["type"];
            if (typeNode is JsonValue v)
            {
                return v.GetValue<string>();
            }

            if (typeNode is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var s = item?.GetValue<string>();
                    if (s is not null && s != "null")
                    {
                        return s;
                    }
                }
            }

            return null;
        }

        private static bool IsNullableSchema(JsonNode schema)
        {
            if (schema["type"] is not JsonArray arr)
            {
                return false;
            }

            foreach (var item in arr)
            {
                if (item?.GetValue<string>() == "null")
                {
                    return true;
                }
            }

            return false;
        }

        private static object NarrowInteger(JsonElement jsonElement, Type? underlyingTarget, string? formatStr)
        {
            // Read at full long width, then narrow to the precise CLR target. Range failures throw OverflowException at cast.
            var asLong = jsonElement.GetInt64();

            if (underlyingTarget is null)
            {
                // No target hint — best-effort: int32 unless format says otherwise.
                return formatStr switch
                {
                    "uint8" => (byte)asLong,
                    "int16" => (short)asLong,
                    "uint16" => (ushort)asLong,
                    "int32" => (int)asLong,
                    "uint32" => (uint)asLong,
                    "int64" => asLong,
                    _ => (int)asLong,
                };
            }

            return Type.GetTypeCode(underlyingTarget) switch
            {
                TypeCode.Byte => (byte)asLong,
                TypeCode.SByte => (sbyte)asLong,
                TypeCode.Int16 => (short)asLong,
                TypeCode.UInt16 => (ushort)asLong,
                TypeCode.Int32 => (int)asLong,
                TypeCode.UInt32 => (uint)asLong,
                TypeCode.Int64 => asLong,
                TypeCode.UInt64 => (ulong)asLong,
                _ => asLong,
            };
        }
    }
}
