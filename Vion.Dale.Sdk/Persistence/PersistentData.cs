using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Persistence
{
    /// <summary>
    ///     Handles capturing and restoring LogicBlock persistent data
    /// </summary>
    public class PersistentData
    {
        private readonly List<PersistentDataEntry> _currentSnapshot = [];

        private readonly PersistenceMetadata _metadata = new();

        private ILogger _logger = null!;

        private object _logicBlock = null!;

        private ServiceBinder _serviceBinder = null!;

        /// <summary>
        ///     Initialize persistent data metadata for the LogicBlock
        /// </summary>
        public void Initialize(object logicBlock, ServiceBinder serviceBinder, ILogger logger)
        {
            _logicBlock = logicBlock;
            _serviceBinder = serviceBinder;
            _logger = logger;

            // 1. Auto-discover writable service properties (unless excluded with [Persistent(Exclude = true)])
            DiscoverWritableServiceProperties(serviceBinder);

            // 2. Opt-in: Direct or nested properties with [Persistent] attribute
            DiscoverOptInProperties(logicBlock.GetType());
        }

        /// <summary>
        ///     Capture the current persistent data values of the LogicBlock
        /// </summary>
        public void CreateSnapshot()
        {
            CheckInitialized();

            _currentSnapshot.Clear();

            // Capture service properties
            foreach (var (key, meta) in _metadata.ServiceProperties)
            {
                try
                {
                    var value = _serviceBinder.GetPropertyValue(meta.ServiceIdentifier, meta.PropertyIdentifier);
                    _currentSnapshot.Add(new PersistentDataEntry(key, meta.PropertyType.FullName!, value!));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to capture service property '{PropertyKey}'", key);
                }
            }

            // Capture direct properties
            foreach (var (key, meta) in _metadata.OptInProperties)
            {
                try
                {
                    var value = meta.Getter(_logicBlock);
                    _currentSnapshot.Add(new PersistentDataEntry(key, meta.PropertyType.FullName!, value!));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to capture opt-in property '{PropertyKey}'", key);
                }
            }
        }

        /// <summary>
        ///     Apply/Restore persistent data to LogicBlock
        /// </summary>
        public void Apply(List<PersistentDataEntry> persistentDataEntries)
        {
            CheckInitialized();

            if (persistentDataEntries.Count == 0)
            {
                _logger.LogDebug("No persistent data entries to apply");
                return;
            }

            _logger.LogInformation("Applying {Count} persistent data entries", persistentDataEntries.Count);

            foreach (var entry in persistentDataEntries)
            {
                try
                {
                    SetPersistentDataValue(_logicBlock,
                                           _serviceBinder,
                                           _metadata,
                                           entry.Key,
                                           entry.Value,
                                           _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore property '{PropertyKey}'", entry.Key);
                }
            }
        }

        /// <summary>
        ///     Get the current persistent data snapshot
        /// </summary>
        public List<PersistentDataEntry> GetCurrentSnapshot()
        {
            CheckInitialized();

            return _currentSnapshot.ToList();
        }

        private void DiscoverWritableServiceProperties(ServiceBinder serviceBinder)
        {
            var allServicePropertyBindings = serviceBinder.GetAllServicePropertyBindings();

            foreach (var (serviceIdentifier, interfaceMap) in allServicePropertyBindings)
            {
                foreach (var (_, bindings) in interfaceMap)
                {
                    foreach (var (propertyId, binding) in bindings)
                    {
                        // Only writable properties
                        if (binding.Setter == null)
                        {
                            continue;
                        }

                        var propInfo = binding.RootSourcePropertyInfo;

                        // Skip if explicitly excluded
                        var persistentAttr = propInfo.GetCustomAttribute<PersistentAttribute>();
                        if (persistentAttr?.Exclude == true)
                        {
                            continue;
                        }

                        var propertyKey = CreateServicePropertyKey(serviceIdentifier, propertyId);
                        _metadata.ServiceProperties[propertyKey] = new ServicePropertyMetadata
                                                                   {
                                                                       ServiceIdentifier = serviceIdentifier,
                                                                       PropertyIdentifier = propertyId,
                                                                       PropertyType = binding.TargetPropertyType,
                                                                   };
                    }
                }
            }
        }

        private void DiscoverOptInProperties(Type type)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<PersistentAttribute>();

                // Check if this property itself is marked as [Persistent]
                if (attr != null && !attr.Exclude)
                {
                    if (!prop.CanWrite)
                    {
                        // Skip read-only properties marked as persistent
                        continue;
                    }

                    var propertyKey = CreateOptInPropertyKey(prop);
                    var getter = ReflectionHelper.CreateCompiledGetter(prop, type);
                    var setter = ReflectionHelper.CreateCompiledSetter(prop, type)!;

                    _metadata.OptInProperties[propertyKey] = new DirectPropertyMetadata
                                                             {
                                                                 PropertyType = prop.PropertyType,
                                                                 Getter = getter,
                                                                 Setter = setter,
                                                             };
                }

                // Check properties of this property's type (one level deep)
                // Skip if explicitly excluded, or if it's a primitive/string/enum/array type
                if (attr?.Exclude == true)
                {
                    continue;
                }

                if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string) && !prop.PropertyType.IsArray && !prop.PropertyType.IsPrimitive)
                {
                    // Get the instance of this property
                    var propertyInstance = prop.GetValue(_logicBlock);
                    if (propertyInstance != null)
                    {
                        DiscoverNestedProperties(prop.PropertyType, prop.Name);
                    }
                }
            }
        }

        private void DiscoverNestedProperties(Type type, string parentPropertyName)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var attr = prop.GetCustomAttribute<PersistentAttribute>();

                // Only process properties marked with [Persistent]
                if (attr == null || attr.Exclude)
                {
                    continue;
                }

                if (!prop.CanWrite)
                {
                    // Skip read-only properties
                    continue;
                }

                var propertyKey = CreateNestedOptInPropertyKey(parentPropertyName, prop.Name);
                var getter = CreateNestedGetter(parentPropertyName, prop.Name);
                var setter = CreateNestedSetter(parentPropertyName, prop.Name);

                _metadata.OptInProperties[propertyKey] = new DirectPropertyMetadata
                                                         {
                                                             PropertyType = prop.PropertyType,
                                                             Getter = getter,
                                                             Setter = setter,
                                                         };
            }
        }

        private Func<object, object?> CreateNestedGetter(string parentPropertyName, string propertyName)
        {
            var fullPath = $"{parentPropertyName}.{propertyName}";
            return logicBlock => ReflectionHelper.GetPropertyValue(logicBlock, fullPath);
        }

        private Action<object, object?> CreateNestedSetter(string parentPropertyName, string propertyName)
        {
            var fullPath = $"{parentPropertyName}.{propertyName}";
            return (logicBlock, value) => ReflectionHelper.SetPropertyValue(logicBlock, fullPath, value);
        }

        private static void SetPersistentDataValue(object logicBlock,
                                                   ServiceBinder serviceBinder,
                                                   PersistenceMetadata metadata,
                                                   string propertyKey,
                                                   object? value,
                                                   ILogger logger)
        {
            // Try service property first
            if (metadata.ServiceProperties.TryGetValue(propertyKey, out var servicePropertyMeta))
            {
                var typedValue = ConvertValue(value, servicePropertyMeta.PropertyType);
                serviceBinder.SetPropertyValue(servicePropertyMeta.ServiceIdentifier, servicePropertyMeta.PropertyIdentifier, typedValue);

                logger.LogDebug("Restored service property '{Key}' = {Value}", propertyKey, typedValue);
                return;
            }

            // Try other property
            if (metadata.OptInProperties.TryGetValue(propertyKey, out var otherMeta))
            {
                var typedValue = ConvertValue(value, otherMeta.PropertyType);
                otherMeta.Setter(logicBlock, typedValue);

                logger.LogDebug("Restored other property '{Key}' = {Value}", propertyKey, typedValue);
                return;
            }

            logger.LogWarning("Unknown property key '{PropertyKey}' in stored persistent data", propertyKey);
        }

        // Storage layers (e.g. dale's JsonFilePersistentDataStore) deserialize the JSON
        // persistence file as Dictionary<string, List<PersistentDataEntry>> with Value
        // typed as object — so each value lands as JsonElement. The store then tries
        // Type.GetType(entry.TypeFullName) to coerce it back to the original CLR type,
        // but Type.GetType only finds types in the calling assembly + corelib — it
        // returns null for ImmutableArray<int>, plugin-defined structs, and most other
        // compound types stored with FullName. Result: those values stayed as
        // JsonElement and the InvalidCastException fired inside ServiceBinder's compiled
        // Setter on every dale boot, leaving array/struct properties unrestored.
        //
        // Since we hold the resolved CLR Type in metadata regardless of what the store
        // managed to do, do the coercion here using the same JSON conventions
        // (camelCase + string-named enums) that JsonFilePersistentDataStore writes with.
        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value is null)
            {
                return null;
            }

            // Already the right CLR type — storage layer did the coercion successfully
            // (works for primitives, strings, dates, durations).
            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }

            if (value is JsonElement element)
            {
                return JsonSerializer.Deserialize(element.GetRawText(), targetType, PersistenceJsonOptions);
            }

            // Last-resort coercion for storage layers that left the value in some other
            // intermediate form (e.g. a nested Dictionary<string, object>): serialize
            // to JSON and deserialize against the target type with the same options.
            var text = JsonSerializer.Serialize(value, PersistenceJsonOptions);
            return JsonSerializer.Deserialize(text, targetType, PersistenceJsonOptions);
        }

        private static readonly JsonSerializerOptions PersistenceJsonOptions = new()
                                                                               {
                                                                                   PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                                                                   Converters = { new JsonStringEnumConverter() },
                                                                               };

        private void CheckInitialized()
        {
            if (_logicBlock == null || _serviceBinder == null || _logger == null)
            {
                throw new InvalidOperationException("PersistentData not initialized");
            }
        }

        private static string CreateServicePropertyKey(string serviceIdentifier, string propertyId)
        {
            return $"{serviceIdentifier}.{propertyId}";
        }

        private static string CreateOptInPropertyKey(PropertyInfo prop)
        {
            return $"_direct.{prop.Name}";
        }

        private static string CreateNestedOptInPropertyKey(string parentPath, string propertyName)
        {
            return $"_direct.{parentPath}.{propertyName}";
        }

        /// <summary>
        ///     Metadata about persistent data for a LogicBlock
        /// </summary>
        private class PersistenceMetadata
        {
            public Dictionary<string, ServicePropertyMetadata> ServiceProperties { get; } = new();

            public Dictionary<string, DirectPropertyMetadata> OptInProperties { get; } = new();
        }

        private class ServicePropertyMetadata
        {
            public required string ServiceIdentifier { get; init; }

            public required string PropertyIdentifier { get; init; }

            public required Type PropertyType { get; init; }
        }

        private class DirectPropertyMetadata
        {
            public required Type PropertyType { get; init; }

            public required Func<object, object?> Getter { get; init; }

            public required Action<object, object?> Setter { get; init; }
        }
    }
}
