using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.DevHost.Control
{
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

        // blockId → (propertyOrMeasuringPointName → serviceConfigId)
        private readonly Dictionary<string, Dictionary<string, string>> _propertyToServiceId = new();

        private readonly Dictionary<string, LogicBlockIntrospectionResult> _results = new();

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
                _results[block.Id] = result;

                if (block.Services.Count == 0)
                {
                    foreach (var service in result.Services)
                    {
                        block.Services.Add(new DevServiceConfig { Id = Guid.NewGuid().ToString(), Identifier = service.Identifier });
                    }
                }

                var map = new Dictionary<string, string>();
                foreach (var service in result.Services)
                {
                    var serviceConfig = block.Services.FirstOrDefault(s => s.Identifier == service.Identifier);
                    if (serviceConfig is null)
                    {
                        continue;
                    }

                    foreach (var property in service.Properties)
                    {
                        map[property.Identifier] = serviceConfig.Id;
                    }

                    foreach (var measuringPoint in service.MeasuringPoints)
                    {
                        map[measuringPoint.Identifier] = serviceConfig.Id;
                    }
                }

                _propertyToServiceId[block.Id] = map;
            }
        }

        private ConfigurationOutput.LogicBlock BuildLogicBlock(DevLogicBlockConfig lb)
        {
            var meta = _results[lb.Id];

            return new ConfigurationOutput.LogicBlock
                   {
                       Id = lb.Id,
                       Name = lb.Name,
                       Annotations = meta.Annotations,
                       Services = lb.Services.Select(s => BuildService(meta, s)).ToList(),
                       Interfaces = meta.Interfaces
                                        .Select(i => new ConfigurationOutput.LogicBlockInterface
                                                     { Identifier = i.Identifier, Annotations = i.Annotations })
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
                                                                          })
                                                           .ToList(),
                   };
        }
    }
}