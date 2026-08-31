using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Reflection;

namespace Vion.Dale.DevHost.Mocking
{
    /// <summary>
    ///     Discovers the service-provider handlers the DevHost should stand in for (RFC 0010): the same
    ///     <see cref="IServiceProviderHandlerActor" /> convention scan the runtime uses, narrowed to the
    ///     handlers that declare a <c>[ScenarioWire]</c> — the <b>value</b> contracts in scope. Handlers without
    ///     it (Modbus RTU request/response and other out-of-scope mechanisms) yield no codec and are skipped, so
    ///     the DevHost never fabricates a stand-in it cannot drive or assert.
    /// </summary>
    internal static class ServiceProviderContractHandlerScan
    {
        public static IReadOnlyList<(Type HandlerType, ScenarioWireCodec Codec)> Discover(Assembly[] assemblies)
        {
            var discovered = new List<(Type, ScenarioWireCodec)>();
            foreach (var handlerType in assemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor)))
            {
                var codec = ScenarioWireCodec.ForHandler(handlerType);
                if (codec is not null)
                {
                    discovered.Add((handlerType, codec));
                }
            }

            return discovered;
        }

        /// <summary>
        ///     Whether a handler class of that name is loaded at all — regardless of whether it declares a
        ///     <c>[ScenarioWire]</c>. Only the refusal path asks (the RFC 0020 pairing table, when a contract's
        ///     <c>ContractHandlerActorName</c> is not among the discovered codecs): a handler that is present but
        ///     silent about its wire structs is an authoring problem in the handler, while one that is absent is a
        ///     missing project reference — two different fixes, so they get two different messages.
        /// </summary>
        public static bool IsHandlerTypeLoaded(string handlerTypeName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic).ToArray();
            return assemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor)).Any(t => string.Equals(t.Name, handlerTypeName, StringComparison.Ordinal));
        }
    }
}