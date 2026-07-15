using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Conventions;
using Vion.Contracts.Introspection;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>Whether a member addressed for a write exists and, if so, whether it can be written.</summary>
    public enum ServicePropertyWriteState
    {
        /// <summary>No service property or measuring point of that name on the service.</summary>
        Unknown,

        /// <summary>The member exists but cannot be written (a measuring point, or a property with no public setter).</summary>
        ReadOnly,

        /// <summary>A writable service property.</summary>
        Writable,
    }

    /// <summary>
    ///     Core-side logic-block introspection for the headless control surface (RFC 0003). Owns the
    ///     introspection results: assigns service identifiers, records the property → service-id map for
    ///     get/set, builds the full <see cref="ConfigurationOutput" /> the UI/agents read, and resolves a
    ///     property's schema + CLR type for decoding JSON set-values. Runs once, before the logic system
    ///     initializes.
    ///     <para>
    ///         <b>Additive / non-breaking:</b> service-id assignment only happens when
    ///         <c>DevConfiguration.LogicBlocks[].Services</c> is empty (it always is now that the web state
    ///         provider was removed — this is the single source of truth).
    ///     </para>
    /// </summary>
    public sealed class DevHostIntrospection
    {
        private readonly DevConfiguration _configuration;

        // Guards introspection: it now runs once in DevHost.StartAsync, but the accessors below also call
        // EnsureIntrospected defensively, and the web server can serve concurrent requests, so the one-time
        // population must be thread-safe.
        private readonly object _gate = new();

        private readonly ILogger<DevHostIntrospection> _logger;

        // blockId → (propertyOrMeasuringPointName → serviceConfigId). Flat per-block namespace: a
        // duplicate member name across two services collapses last-service-wins — the service-qualified
        // map below exists so callers can reach the shadowed service (RFC 0006 revision 5).
        private readonly Dictionary<string, Dictionary<string, string>> _propertyToServiceId = new();

        private readonly Dictionary<string, LogicBlockIntrospectionResult> _results = new();

        // blockId → (serviceIdentifier → (memberName → serviceConfigId)) — the non-collapsing map.
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _serviceMemberToServiceId = new();

        private readonly IServiceProvider _serviceProvider;

        private volatile bool _done;

        public DevHostIntrospection(DevConfiguration configuration, IServiceProvider serviceProvider, ILogger<DevHostIntrospection> logger)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>Introspect once (idempotent, thread-safe). Call before the logic system is initialized.</summary>
        public void EnsureIntrospected()
        {
            if (_done)
            {
                return;
            }

            lock (_gate)
            {
                if (_done)
                {
                    return;
                }

                Introspect();
                _done = true;
            }
        }

        /// <summary>Resolve a block's property/measuring-point name to the service-config id carrying it.</summary>
        public bool TryGetServiceId(string blockId, string propertyName, out string serviceId)
        {
            EnsureIntrospected();
            serviceId = string.Empty;
            return _propertyToServiceId.TryGetValue(blockId, out var map) && map.TryGetValue(propertyName, out serviceId!);
        }

        /// <summary>
        ///     Resolve a block's property/measuring-point name to the service-config id, qualified by the
        ///     service identifier — reaches members the flat per-block name map shadows when two services of
        ///     one block declare the same member name (RFC 0006 revision 5 name paths).
        /// </summary>
        public bool TryGetServiceId(string blockId, string serviceIdentifier, string propertyName, out string serviceId)
        {
            EnsureIntrospected();
            serviceId = string.Empty;
            return _serviceMemberToServiceId.TryGetValue(blockId, out var services) && services.TryGetValue(serviceIdentifier, out var members) &&
                   members.TryGetValue(propertyName, out serviceId!);
        }

        /// <summary>All property/measuring-point names known for a block.</summary>
        public IReadOnlyCollection<string> PropertyNames(string blockId)
        {
            EnsureIntrospected();
            return _propertyToServiceId.TryGetValue(blockId, out var map) ? map.Keys.ToList() : Array.Empty<string>();
        }

        /// <summary>
        ///     Resolve the JSON Schema and CLR type for a property addressed by service-config id — used to
        ///     decode a JSON set-value (HTTP path) into the precise typed value the block expects.
        ///     <para>
        ///         The property may live on the block type itself (the block's own service) or on a
        ///         service-bound member object (e.g. an interface-bound "charging point" of a multi-point
        ///         block) whose member name equals the service identifier. Resolving only against the block
        ///         type would leave nested-service values as undecoded <c>JsonElement</c>s, which the service
        ///         binder then fails to cast — the "writes to multi-point properties silently do nothing" bug.
        ///     </para>
        /// </summary>
        public bool TryGetPropertyConversion(string serviceId, string propertyName, out JsonNode? schema, out Type? clrType)
        {
            EnsureIntrospected();
            schema = null;
            clrType = null;

            var block = _configuration.LogicBlocks.FirstOrDefault(lb => lb.Services.Any(s => s.Id == serviceId));
            if (block is null || !_results.TryGetValue(block.Id, out var result))
            {
                return false;
            }

            var serviceConfig = block.Services.First(s => s.Id == serviceId);
            var serviceInfo = result.Services.FirstOrDefault(si => si.Identifier == serviceConfig.Identifier);
            var propertyInfo = serviceInfo?.Properties.FirstOrDefault(p => p.Identifier == propertyName);
            if (propertyInfo is null)
            {
                return false;
            }

            schema = propertyInfo.Schema;

            var hostType = block.LogicBlockType;
            var serviceMember = block.LogicBlockType.GetProperty(serviceConfig.Identifier);
            if (serviceMember is not null && serviceMember.PropertyType.GetProperty(propertyName) is not null)
            {
                hostType = serviceMember.PropertyType;
            }

            clrType = hostType.GetProperty(propertyName)?.PropertyType;
            return clrType is not null;
        }

        /// <summary>
        ///     Whether a member addressed by service-config id can be written: <see cref="ServicePropertyWriteState.Unknown" />
        ///     when no such member exists, <see cref="ServicePropertyWriteState.ReadOnly" /> for a measuring point or a
        ///     property whose schema carries <c>readOnly: true</c> (no public setter), else
        ///     <see cref="ServicePropertyWriteState.Writable" />. The set path consults this to reject a write the block
        ///     cannot apply LOUDLY, rather than letting the binder exception be swallowed into a silent no-op.
        /// </summary>
        public ServicePropertyWriteState GetServicePropertyWriteState(string serviceId, string propertyName)
        {
            EnsureIntrospected();

            var block = _configuration.LogicBlocks.FirstOrDefault(lb => lb.Services.Any(s => s.Id == serviceId));
            if (block is null || !_results.TryGetValue(block.Id, out var result))
            {
                return ServicePropertyWriteState.Unknown;
            }

            var serviceConfig = block.Services.First(s => s.Id == serviceId);
            var serviceInfo = result.Services.FirstOrDefault(si => si.Identifier == serviceConfig.Identifier);
            if (serviceInfo is null)
            {
                return ServicePropertyWriteState.Unknown;
            }

            var property = serviceInfo.Properties.FirstOrDefault(p => p.Identifier == propertyName);
            if (property is not null)
            {
                return property.Schema?["readOnly"]?.GetValue<bool>() == true ? ServicePropertyWriteState.ReadOnly : ServicePropertyWriteState.Writable;
            }

            // Measuring points are read-only computed metrics — known members, never writable.
            return serviceInfo.MeasuringPoints.Any(mp => mp.Identifier == propertyName) ? ServicePropertyWriteState.ReadOnly : ServicePropertyWriteState.Unknown;
        }

        /// <summary>Build the full introspection output for the wired network (the heavyweight view).</summary>
        public ConfigurationOutput BuildConfiguration()
        {
            // Self-initialize: a caller may reach this before DevHost.StartAsync has run EnsureIntrospected —
            // e.g. an /api/configuration request racing host startup, or an agent calling
            // Control.GetConfiguration directly. Idempotent, so it's a no-op once introspection has happened.
            EnsureIntrospected();

            return new ConfigurationOutput
                   {
                       TopologyName = _configuration.TopologyName,
                       LogicBlocks = _configuration.LogicBlocks.Select(BuildLogicBlock).ToList(),
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
                                                                                       .Select(svc => new ConfigurationOutput.ServiceProviderService
                                                                                                      {
                                                                                                          Identifier = svc.Identifier,
                                                                                                          Contracts = svc.Contracts
                                                                                                              .Select(c =>
                                                                                                                          new ConfigurationOutput.
                                                                                                                          ServiceProviderContract
                                                                                                                          {
                                                                                                                              Identifier = c.Identifier,
                                                                                                                              ContractType = c.ContractType,
                                                                                                                          })
                                                                                                              .ToList(),
                                                                                                      })
                                                                                       .ToList(),
                                                                      })
                                                        .ToList(),
                   };
        }

        private void Introspect()
        {
            foreach (var block in _configuration.LogicBlocks)
            {
                if (_serviceProvider.GetService(block.LogicBlockType) is not LogicBlockBase instance)
                {
                    _logger.LogWarning("Could not instantiate {Type} for introspection; skipping its control metadata.", block.LogicBlockType.Name);
                    continue;
                }

                var result = LogicBlockIntrospection.IntrospectLogicBlock(instance, _serviceProvider);

                // RFC 0016: the DevHost is the local stand-in for cloud-api's LiveViewResolver — resolve the
                // definition view down to the live view for this instance's topology-set parameters, so the UI
                // shows exactly the included members (no dead Point3 slot) and the minted service ids match the
                // set the running block actually binds.
                ApplyLiveView(result, block.InstantiationParameters);

                _results[block.Id] = result;

                if (block.Services.Count == 0)
                {
                    foreach (var service in result.Services)
                    {
                        block.Services.Add(new DevServiceConfig { Id = Guid.NewGuid().ToString(), Identifier = service.Identifier });
                    }
                }

                var map = new Dictionary<string, string>();
                var serviceMap = new Dictionary<string, Dictionary<string, string>>();
                foreach (var service in result.Services)
                {
                    var serviceConfig = block.Services.FirstOrDefault(s => s.Identifier == service.Identifier);
                    if (serviceConfig is null)
                    {
                        continue;
                    }

                    // A nested interface-bound component surfaces as its own service whose identifier equals
                    // the block-type property that holds it; the block's OWN (root) service has no such
                    // property. When a member name is carried by BOTH the root service and a nested component
                    // (DF-47 — standard telemetry names such as ActivePowerTotalKw shared across a buffer
                    // surface and its charge points), the flat per-block name map must resolve the bare name
                    // to the ROOT service rather than collapse it last-service-wins onto a component. A
                    // root-service entry always wins; a nested-component entry only fills a name the root does
                    // not carry. The service-qualified map below still reaches every component (and the
                    // "service.member" read path is the correct way to address a shadowed nested member).
                    var isNestedComponentService = block.LogicBlockType.GetProperty(service.Identifier) is not null;

                    var members = new Dictionary<string, string>();
                    foreach (var property in service.Properties)
                    {
                        if (!isNestedComponentService || !map.ContainsKey(property.Identifier))
                        {
                            map[property.Identifier] = serviceConfig.Id;
                        }

                        members[property.Identifier] = serviceConfig.Id;
                    }

                    foreach (var measuringPoint in service.MeasuringPoints)
                    {
                        if (!isNestedComponentService || !map.ContainsKey(measuringPoint.Identifier))
                        {
                            map[measuringPoint.Identifier] = serviceConfig.Id;
                        }

                        members[measuringPoint.Identifier] = serviceConfig.Id;
                    }

                    serviceMap[service.Identifier] = members;
                }

                _propertyToServiceId[block.Id] = map;
                _serviceMemberToServiceId[block.Id] = serviceMap;
            }
        }

        /// <summary>
        ///     RFC 0016: filters a definition-view introspection down to the live view for a set of
        ///     instantiation-parameter values — drops any service / interface / contract whose
        ///     <c>[IncludedWhen]</c> predicate resolves false. Mirrors cloud-api's <c>LiveViewResolver</c>;
        ///     the parameter context is the topology-set values overlaid on each parameter's introspected
        ///     <c>runtime.default</c>. A predicate that fails to parse/evaluate is left included and logged
        ///     (the running block is the strict fail-closed gate; the DevHost UI stays fail-open).
        /// </summary>
        private void ApplyLiveView(LogicBlockIntrospectionResult result, IReadOnlyDictionary<string, JsonNode>? parameterValues)
        {
            var context = BuildParameterContext(result, parameterValues);

            result.Services.RemoveAll(service => !IsIncluded(service.IncludedWhen, context));
            result.Interfaces.RemoveAll(iface => !IsIncluded(AnnotationPredicate(iface.Annotations), context));
            result.Contracts.RemoveAll(contract => !IsIncluded(AnnotationPredicate(contract.Annotations), context));
        }

        private static Dictionary<string, JsonNode?> BuildParameterContext(LogicBlockIntrospectionResult result, IReadOnlyDictionary<string, JsonNode>? parameterValues)
        {
            var context = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

            // Defaults: each [InstantiationParameter] property carries runtime.instantiationParameter + runtime.default.
            foreach (var service in result.Services)
            {
                foreach (var property in service.Properties)
                {
                    if (property.Runtime is JsonObject runtime && runtime["instantiationParameter"]?.GetValue<bool>() == true)
                    {
                        context[property.Identifier] = runtime["default"]?.DeepClone();
                    }
                }
            }

            // Overlay the operator-chosen topology values.
            if (parameterValues is not null)
            {
                foreach (var (identifier, value) in parameterValues)
                {
                    context[identifier] = value?.DeepClone();
                }
            }

            return context;
        }

        private bool IsIncluded(string? predicate, IReadOnlyDictionary<string, JsonNode?> context)
        {
            if (string.IsNullOrEmpty(predicate))
            {
                return true;
            }

            try
            {
                return Predicate.Parse(predicate!).Evaluate(context);
            }
            catch (PredicateException exception)
            {
                _logger.LogWarning(exception,
                                   "Could not resolve [IncludedWhen] predicate \"{Predicate}\" for the live view; leaving the member visible (the running block is the strict gate).",
                                   predicate);
                return true;
            }
        }

        private static string? AnnotationPredicate(IReadOnlyDictionary<string, object>? annotations)
        {
            return annotations is not null && annotations.TryGetValue(LogicBlockWiringConventions.IncludedWhenAnnotationKey, out var value) ? value as string : null;
        }

        private ConfigurationOutput.LogicBlock BuildLogicBlock(DevLogicBlockConfig lb)
        {
            var meta = _results[lb.Id];

            return new ConfigurationOutput.LogicBlock
                   {
                       Id = lb.Id,
                       Name = lb.Name,
                       TypeFullName = lb.LogicBlockType.FullName,
                       Annotations = meta.Annotations,
                       Services = lb.Services.Select(s => BuildService(meta, s)).ToList(),
                       Interfaces = meta.Interfaces
                                        .Select(i => new ConfigurationOutput.LogicBlockInterface
                                                     {
                                                         Identifier = i.Identifier,
                                                         Annotations = i.Annotations,
                                                         InterfaceTypeFullNames = i.InterfaceTypeFullNames,
                                                         MatchingInterfaceTypeFullNames =
                                                             i.MatchingInterfaceTypeFullNames,
                                                     })
                                        .ToList(),
                       Contracts = meta.Contracts
                                       .Select(c => new ConfigurationOutput.LogicBlockContract
                                                    {
                                                        Identifier = c.Identifier,
                                                        MatchingContractType = c.MatchingContractType,
                                                        Annotations = c.Annotations,
                                                    })
                                       .ToList(),
                       ContractMappings = lb.ContractMappings
                                            .Select(cm => new ConfigurationOutput.ContractMapping
                                                          {
                                                              ContractIdentifier = cm.ContractIdentifier,
                                                              MappedServiceProviderIdentifier = cm.ServiceProviderIdentifier,
                                                              MappedServiceIdentifier = cm.ServiceIdentifier,
                                                              MappedContractIdentifier = cm.ContractEndpointIdentifier,
                                                          })
                                            .ToList(),
                       InstantiationParameters = lb.InstantiationParameters,
                   };
        }

        private static ConfigurationOutput.Service BuildService(LogicBlockIntrospectionResult meta, DevServiceConfig s)
        {
            var serviceInfo = meta.Services.Single(si => si.Identifier == s.Identifier);

            return new ConfigurationOutput.Service
                   {
                       Id = s.Id,
                       Identifier = s.Identifier,
                       ServiceProperties = serviceInfo.Properties
                                                      .Select(sp => new ConfigurationOutput.ServiceProperty
                                                                    {
                                                                        Identifier = sp.Identifier,
                                                                        Schema = sp.Schema,
                                                                        Presentation = sp.Presentation,
                                                                        Runtime = sp.Runtime,
                                                                    })
                                                      .ToList(),
                       ServiceMeasuringPoints = serviceInfo.MeasuringPoints
                                                           .Select(smp => new ConfigurationOutput.ServiceMeasuringPoint
                                                                          {
                                                                              Identifier = smp.Identifier,
                                                                              Schema = smp.Schema,
                                                                              Presentation = smp.Presentation,
                                                                              Runtime = smp.Runtime,
                                                                          })
                                                           .ToList(),
                   };
        }
    }
}