using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Core-side logic-block introspection for the headless control surface (RFC 0003).
    ///     <para>
    ///         Assigns each block's services their identifiers and records the property → service-id map the
    ///         control facade needs for get/set. Runs once, before the logic system is initialized.
    ///     </para>
    ///     <para>
    ///         <b>Additive / non-breaking:</b> service-id assignment only happens when
    ///         <c>DevConfiguration.LogicBlocks[].Services</c> is empty. In a <c>.WithWebUi()</c> boot the web
    ///         state provider has already populated it (its ctor runs while the web hosted-service is
    ///         resolved, before this), so this maps onto the <em>existing</em> ids and changes nothing on the
    ///         web path.
    ///     </para>
    /// </summary>
    public sealed class DevHostIntrospection
    {
        private readonly DevConfiguration _configuration;

        private readonly IServiceProvider _serviceProvider;

        private readonly ILogger<DevHostIntrospection> _logger;

        // blockId → (propertyOrMeasuringPointName → serviceConfigId)
        private readonly Dictionary<string, Dictionary<string, string>> _propertyToServiceId = new();

        private bool _done;

        public DevHostIntrospection(DevConfiguration configuration, IServiceProvider serviceProvider, ILogger<DevHostIntrospection> logger)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>Introspect once (idempotent). Call before the logic system is initialized.</summary>
        public void EnsureIntrospected()
        {
            if (_done)
            {
                return;
            }

            _done = true;

            foreach (var block in _configuration.LogicBlocks)
            {
                if (_serviceProvider.GetService(block.LogicBlockType) is not LogicBlockBase instance)
                {
                    _logger.LogWarning("Could not instantiate {Type} for introspection; skipping its control metadata.", block.LogicBlockType.Name);
                    continue;
                }

                var result = LogicBlockIntrospection.IntrospectLogicBlock(instance, _serviceProvider);

                // Headless: assign service ids if the web path hasn't already.
                if (block.Services.Count == 0)
                {
                    foreach (var service in result.Services)
                    {
                        block.Services.Add(new DevServiceConfig { Id = Guid.NewGuid().ToString(), Identifier = service.Identifier });
                    }
                }

                // Map every property / measuring point to the service-config id the block actually uses.
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

        /// <summary>Resolve a block's property/measuring-point name to the service-config id carrying it.</summary>
        public bool TryGetServiceId(string blockId, string propertyName, out string serviceId)
        {
            serviceId = string.Empty;
            return _propertyToServiceId.TryGetValue(blockId, out var map) && map.TryGetValue(propertyName, out serviceId!);
        }

        /// <summary>All property/measuring-point names known for a block.</summary>
        public IReadOnlyCollection<string> PropertyNames(string blockId)
        {
            return _propertyToServiceId.TryGetValue(blockId, out var map) ? map.Keys.ToList() : Array.Empty<string>();
        }
    }
}
