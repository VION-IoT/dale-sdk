using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    public static class DeclarativeContractBinder
    {
        public static void BindContractsFromAttributes(object logicBlock,
                                                       IContractFactory contractFactory,
                                                       BindingMode mode,
                                                       IReadOnlyDictionary<string, JsonNode?>? parameterContext,
                                                       Dictionary<string, string> mintedBy)
        {
            var type = logicBlock.GetType();

            // A binding attribute on a property the marker rule does not reach is authored intent nothing
            // reads: the walks below key on [ServiceProviderContractType], so such a property is in neither
            // of them and the block runs with a null where its author expected a contract.
            foreach (var property in GetUnreadableContractBindings(type))
            {
                throw new InvalidOperationException($"Property '{property.Name}' in '{type.Name}' has [ServiceProviderContractBinding] but its type " +
                                                    $"'{property.PropertyType.Name}' does not carry [ServiceProviderContractType], so nothing binds it and the property " +
                                                    "stays null. Type the property on a service-provider contract interface, or remove the attribute.");
            }

            var contractProperties = GetContractProperties(type);

            foreach (var property in GetInvalidContractProperties(type))
            {
                throw new InvalidOperationException($"Property '{property.Name}' in '{type.Name}' has [ServiceProviderContractBinding] attribute but no setter. " +
                                                    $"Contract properties must have at least a private setter to enable binding. " +
                                                    $"Example: public {property.PropertyType.Name} {property.Name} {{ get; private set; }}");
            }

            foreach (var property in contractProperties)
            {
                // Skip a gated-out contract binding entirely in Live mode. The property is left
                // at its default — for a contract that means NULL (the binder is what constructs it), the
                // documented authoring hazard (declare gated contract properties nullable, gate the fan-out).
                var includedWhen = InclusionGate.ReadPredicate(property);
                if (includedWhen is not null && mode == BindingMode.Definition)
                {
                    InclusionGate.EnsureResolvable(includedWhen, logicBlock, property.Name);
                }

                if (!InclusionGate.IsIncluded(includedWhen, mode, parameterContext))
                {
                    continue;
                }

                var contractAttribute = property.GetCustomAttribute<ServiceProviderContractBindingAttribute>();
                var identifier = contractAttribute?.Identifier ?? property.Name;
                BindingIdentifiers.Claim(mintedBy, identifier, property.Name, "Contract binding", type);
                var contractInstance = contractFactory.Create(property.PropertyType, identifier);
                WritableAccessor(property)!.SetValue(logicBlock, contractInstance);
                ApplyMetadata(contractInstance, contractAttribute, includedWhen);
            }
        }

        private static List<PropertyInfo> GetContractProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true).Where(p => IsContractType(p.PropertyType) && WritableAccessor(p) is not null).ToList();
        }

        private static List<PropertyInfo> GetInvalidContractProperties(Type type)
        {
            return ReflectionHelper.GetProperties(type, true).Where(p => IsContractType(p.PropertyType) && WritableAccessor(p) is null).ToList();
        }

        private static List<PropertyInfo> GetUnreadableContractBindings(Type type)
        {
            return ReflectionHelper.GetProperties(type, true)
                                   .Where(p => !IsContractType(p.PropertyType) && p.GetCustomAttribute<ServiceProviderContractBindingAttribute>() != null)
                                   .ToList();
        }

        /// <summary>
        ///     The property the binder writes the contract instance through, or <c>null</c> when the
        ///     declaration has no setter at all.
        ///     <para>
        ///         Reflection does not inherit a non-public accessor: a property declared on a base class as
        ///         <c>{ get; private set; }</c> comes back from the derived type's walk with
        ///         <see cref="PropertyInfo.CanWrite" /> false and a null set method, so the derived view alone
        ///         cannot tell "no setter" from "a setter this view may not see". Resolving the declaration on
        ///         its own type answers that, and the resolved property is what
        ///         <see cref="PropertyInfo.SetValue(object, object)" /> has to be called on — the derived
        ///         view's would throw for want of a set method.
        ///     </para>
        /// </summary>
        private static PropertyInfo? WritableAccessor(PropertyInfo property)
        {
            if (property.CanWrite)
            {
                return property;
            }

            var declared = property.DeclaringType?.GetProperty(property.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            return declared is { CanWrite: true } ? declared : null;
        }

        private static void ApplyMetadata(object contractInstance, ServiceProviderContractBindingAttribute? contractAttr, string? includedWhen)
        {
            if (contractAttr == null && includedWhen is null)
            {
                return;
            }

            if (contractInstance is not LogicBlockContractBase logicBlockContract)
            {
                throw new InvalidCastException($"Object of type {contractInstance.GetType().FullName} is not of type {nameof(LogicBlockContractBase)}");
            }

            var metadata = logicBlockContract.MetaData;
            metadata.IncludedWhen = includedWhen;

            if (contractAttr == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(contractAttr.DefaultName))
            {
                metadata.DefaultName = contractAttr.DefaultName;
            }

            if (contractAttr.Tags.Length > 0)
            {
                metadata.Tags = contractAttr.Tags.ToList();
            }

            metadata.Multiplicity = contractAttr.Multiplicity;
        }

        private static bool IsContractType(Type type)
        {
            return type.GetCustomAttribute<ServiceProviderContractTypeAttribute>() != null;
        }
    }
}