using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServiceBinder : IServiceFactory
    {
        public static readonly Type ExtraPropsKey = typeof(ExtraPropertiesSentinel);

        // serviceIdentifier → interfaceType (ExtraPropsKey for extra props) → propertyName → binding
        private readonly Dictionary<string, Dictionary<Type, Dictionary<string, ServiceBinding>>> _serviceMeasuringPoints = new();

        // serviceIdentifier → interfaceType (ExtraPropsKey for extra props) → propertyName → binding
        private readonly Dictionary<string, Dictionary<Type, Dictionary<string, ServiceBinding>>> _serviceProperties = new();

        // serviceIdentifier → list of relations
        private readonly Dictionary<string, List<ServiceRelationInfo>> _serviceRelations = [];

        /// <summary>
        ///     For the user to declare a service and start binding properties
        /// </summary>
        public ServiceBuilder CreateService(string serviceIdentifier)
        {
            if (!_serviceProperties.ContainsKey(serviceIdentifier))
            {
                _serviceProperties[serviceIdentifier] = new Dictionary<Type, Dictionary<string, ServiceBinding>>();
            }

            if (!_serviceMeasuringPoints.ContainsKey(serviceIdentifier))
            {
                _serviceMeasuringPoints[serviceIdentifier] = new Dictionary<Type, Dictionary<string, ServiceBinding>>();
            }

            return new ServiceBuilder(this, serviceIdentifier);
        }

        public event EventHandler<ServicePropertyChangedEventArgs>? ServicePropertyValueChanged;

        public event EventHandler<ServiceMeasuringPointChangedEventArgs>? ServiceMeasuringPointValueChanged;

        public event EventHandler<ServicePropertyClearedEventArgs>? ServicePropertyCleared;

        public event EventHandler<ServiceMeasuringPointClearedEventArgs>? ServiceMeasuringPointCleared;

        /// <summary>
        ///     For the logic block to set a property value on a service
        /// </summary>
        public void SetPropertyValue(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            if (_serviceProperties.TryGetValue(serviceIdentifier, out var ifaceMap))
            {
                foreach (var kv in ifaceMap.Values)
                {
                    if (kv.TryGetValue(propertyIdentifier, out var binding) && binding.Setter != null)
                    {
                        // Convert int to enum if the target property is an enum type
                        var convertedValue = value;
                        if (value is int intValue && binding.TargetPropertyType.IsEnum)
                        {
                            convertedValue = Enum.ToObject(binding.TargetPropertyType, intValue);
                        }

                        binding.Setter(binding.Source, convertedValue);
                        return;
                    }
                }
            }

            throw new InvalidOperationException($"Property {propertyIdentifier} not found or not writable in {serviceIdentifier}.");
        }

        /// <summary>
        ///     For the logic block to get a property value from a service
        /// </summary>
        public object? GetPropertyValue(string serviceIdentifier, string propertyIdentifier)
        {
            if (_serviceProperties.TryGetValue(serviceIdentifier, out var ifaceMap))
            {
                foreach (var kv in ifaceMap.Values)
                {
                    if (kv.TryGetValue(propertyIdentifier, out var binding))
                    {
                        return binding.Getter(binding.Source);
                    }
                }
            }

            throw new InvalidOperationException($"Property {propertyIdentifier} not found in {serviceIdentifier}.");
        }

        /// <summary>
        ///     For the logic block to get a measuring point value from a service
        /// </summary>
        public object? GetMeasuringPointValue(string serviceIdentifier, string measuringPointIdentifier)
        {
            if (_serviceMeasuringPoints.TryGetValue(serviceIdentifier, out var ifaceMap))
            {
                foreach (var kv in ifaceMap.Values)
                {
                    if (kv.TryGetValue(measuringPointIdentifier, out var binding))
                    {
                        return binding.Getter(binding.Source);
                    }
                }
            }

            throw new InvalidOperationException($"Property {measuringPointIdentifier} not found in {serviceIdentifier}.");
        }

        /// <summary>
        ///     For the parser to inspect all bindings
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> GetAllServicePropertyBindings()
        {
            return _serviceProperties.ToDictionary(serviceEntry => serviceEntry.Key, // serviceIdentifier
                                                   serviceEntry =>
                                                       (IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>)new
                                                           ReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>(serviceEntry.Value.ToDictionary(ifaceEntry =>
                                                                   ifaceEntry.Key,
                                                               ifaceEntry =>
                                                                   (IReadOnlyDictionary<string, ServiceBinding>)
                                                                   new ReadOnlyDictionary<string, ServiceBinding>(ifaceEntry.Value))));
        }

        /// <summary>
        ///     For the parser to inspect all bindings
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> GetAllServiceMeasuringPointBindings()
        {
            return _serviceMeasuringPoints.ToDictionary(serviceEntry => serviceEntry.Key, // serviceIdentifier
                                                        serviceEntry =>
                                                            (IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>)new
                                                                ReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>(serviceEntry.Value.ToDictionary(ifaceEntry =>
                                                                        ifaceEntry.Key,
                                                                    ifaceEntry =>
                                                                        (IReadOnlyDictionary<string, ServiceBinding>)
                                                                        new ReadOnlyDictionary<string, ServiceBinding>(ifaceEntry.Value))));
        }

        /// <summary>
        ///     For the parser to inspect all service relations
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ServiceRelationInfo>> GetAllServiceRelations()
        {
            return _serviceRelations.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<ServiceRelationInfo>)kvp.Value.AsReadOnly());
        }

        /// <summary>
        ///     Publishes initial state updates for all service properties and measuring points after startup
        /// </summary>
        /// <param name="logger"></param>
        public void PublishInitialStateUpdates(ILogger logger)
        {
            // Publish all service property states
            foreach (var (serviceIdentifier, interfaceDictionary) in _serviceProperties)
            {
                foreach (var interfaceEntry in interfaceDictionary.Values)
                {
                    foreach (var (propertyIdentifier, binding) in interfaceEntry)
                    {
                        try
                        {
                            var value = binding.Getter(binding.Source);
                            ServicePropertyValueChanged?.Invoke(this, new ServicePropertyChangedEventArgs(serviceIdentifier, propertyIdentifier, value));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Failed to publish initial state for service property {ServiceIdentifier}.{PropertyIdentifier}: {ExceptionMessage}",
                                              serviceIdentifier,
                                              propertyIdentifier,
                                              ex.Message);
                        }
                    }
                }
            }

            // Publish all service measuring point states
            foreach (var (serviceIdentifier, interfaceDictionary) in _serviceMeasuringPoints)
            {
                foreach (var interfaceEntry in interfaceDictionary.Values)
                {
                    foreach (var (measuringPointIdentifier, binding) in interfaceEntry)
                    {
                        try
                        {
                            var value = binding.Getter(binding.Source);
                            ServiceMeasuringPointValueChanged?.Invoke(this, new ServiceMeasuringPointChangedEventArgs(serviceIdentifier, measuringPointIdentifier, value));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Failed to publish initial state for service measuring point {ServiceIdentifier}.{MeasuringPointIdentifier}: {ExceptionMessage}",
                                              serviceIdentifier,
                                              measuringPointIdentifier,
                                              ex.Message);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Notifies that all retained messages should be cleared
        /// </summary>
        public void ClearRetainedMessages(ILogger logger)
        {
            // Clear all service property retained messages
            foreach (var (serviceIdentifier, interfaceDictionary) in _serviceProperties)
            {
                foreach (var interfaceEntry in interfaceDictionary.Values)
                {
                    foreach (var (propertyIdentifier, _) in interfaceEntry)
                    {
                        try
                        {
                            ServicePropertyCleared?.Invoke(this, new ServicePropertyClearedEventArgs(serviceIdentifier, propertyIdentifier));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Failed to clear retained message for service property {ServiceIdentifier}.{PropertyIdentifier}: {ExceptionMessage}",
                                              serviceIdentifier,
                                              propertyIdentifier,
                                              ex.Message);
                        }
                    }
                }
            }

            // Clear all service measuring point retained messages
            foreach (var (serviceIdentifier, interfaceDictionary) in _serviceMeasuringPoints)
            {
                foreach (var interfaceEntry in interfaceDictionary.Values)
                {
                    foreach (var (measuringPointIdentifier, _) in interfaceEntry)
                    {
                        try
                        {
                            ServiceMeasuringPointCleared?.Invoke(this, new ServiceMeasuringPointClearedEventArgs(serviceIdentifier, measuringPointIdentifier));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Failed to clear retained message for service measuring point {ServiceIdentifier}.{MeasuringPointIdentifier}: {ExceptionMessage}",
                                              serviceIdentifier,
                                              measuringPointIdentifier,
                                              ex.Message);
                        }
                    }
                }
            }
        }

        internal void RegisterServicePropertyBinding(string serviceIdentifier, Type? interfaceType, string propertyIdentifier, ServiceBinding binding)
        {
            var interfaceTypeKey = interfaceType ?? ExtraPropsKey;

            if (!_serviceProperties[serviceIdentifier].ContainsKey(interfaceTypeKey))
            {
                _serviceProperties[serviceIdentifier][interfaceTypeKey] = new Dictionary<string, ServiceBinding>();
            }

            _serviceProperties[serviceIdentifier][interfaceTypeKey][propertyIdentifier] = binding;

            if (binding.Source is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += (_, args) =>
                                       {
                                           if (args.PropertyName == binding.RootSourcePropertyName)
                                           {
                                               var value = binding.Getter(binding.Source);
                                               ServicePropertyValueChanged?.Invoke(this, new ServicePropertyChangedEventArgs(serviceIdentifier, propertyIdentifier, value));
                                           }
                                       };
            }
        }

        internal void RegisterServiceMeasuringPointBinding(string serviceIdentifier, Type? interfaceType, string serviceMeasuringPointIdentifier, ServiceBinding binding)
        {
            // For measuring points, we ensure there's no setter (enforcing read-only behavior)
            if (binding.Setter != null)
            {
                throw new InvalidOperationException("Measuring points cannot have setters. Use a property binding instead.");
            }

            var interfaceTypeKey = interfaceType ?? ExtraPropsKey;

            if (!_serviceMeasuringPoints[serviceIdentifier].ContainsKey(interfaceTypeKey))
            {
                _serviceMeasuringPoints[serviceIdentifier][interfaceTypeKey] = new Dictionary<string, ServiceBinding>();
            }

            _serviceMeasuringPoints[serviceIdentifier][interfaceTypeKey][serviceMeasuringPointIdentifier] = binding;

            if (binding.Source is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += (_, args) =>
                                       {
                                           if (args.PropertyName == binding.RootSourcePropertyName)
                                           {
                                               var value = binding.Getter(binding.Source);
                                               ServiceMeasuringPointValueChanged?.Invoke(this,
                                                                                         new ServiceMeasuringPointChangedEventArgs(serviceIdentifier,
                                                                                             serviceMeasuringPointIdentifier,
                                                                                             value));
                                           }
                                       };
            }
        }

        internal void RegisterServiceRelation(string serviceIdentifier, ServiceRelationInfo relationInfo)
        {
            if (!_serviceRelations.ContainsKey(serviceIdentifier))
            {
                _serviceRelations[serviceIdentifier] = [];
            }

            _serviceRelations[serviceIdentifier].Add(relationInfo);
        }

        private class ExtraPropertiesSentinel
        {
        }
    }
}