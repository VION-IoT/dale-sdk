using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public static class ReflectionHelper
    {
        public static string GetSinglePropertyName<T, TProp>(Expression<Func<T, TProp>> expr)
        {
            var body = expr.Body is UnaryExpression u ? u.Operand : expr.Body;
            if (body is MemberExpression m && m.Expression is ParameterExpression)
            {
                return m.Member.Name;
            }

            throw new InvalidOperationException("Expression must be a direct property access.");
        }

        public static (string FullPath, PropertyInfo RootPropertyInfo) GetPropertyPath<T, TProp>(Expression<Func<T, TProp>> expr)
        {
            // Peel off conversions like (object)x or (double)…
            static Expression StripConvert(Expression e)
            {
                return e is UnaryExpression u && (u.NodeType == ExpressionType.Convert || u.NodeType == ExpressionType.ConvertChecked) ? StripConvert(u.Operand) : e;
            }

            var body = StripConvert(expr.Body);

            if (body is not MemberExpression member)
            {
                throw new ArgumentException("Expression must be a property access chain", nameof(expr));
            }

            var parts = new List<string>();
            PropertyInfo? rootPropertyInfo;

            while (true)
            {
                parts.Insert(0, member.Member.Name);

                var inner = StripConvert(member.Expression!);
                if (inner is MemberExpression innerMember)
                {
                    member = innerMember;
                }
                else if (inner is ParameterExpression)
                {
                    // We've reached the parameter, the current member is the root property
                    rootPropertyInfo = member.Member as PropertyInfo;
                    if (rootPropertyInfo == null)
                    {
                        throw new ArgumentException("Root member must be a property", nameof(expr));
                    }

                    break;
                }
                else
                {
                    throw new ArgumentException("Expression must consist only of property accessors.", nameof(expr));
                }
            }

            if (rootPropertyInfo == null)
            {
                throw new ArgumentException("Could not determine root property", nameof(expr));
            }

            return (string.Join(".", parts), rootPropertyInfo);
        }

        /// <summary>
        ///     Gets the target property type from a property path on a given source type
        /// </summary>
        public static Type GetTargetPropertyType(Type sourceType, string propertyPath)
        {
            var parts = propertyPath.Split('.');
            var currentType = sourceType;

            foreach (var part in parts)
            {
                var property = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property == null)
                {
                    throw new InvalidOperationException($"Property '{part}' not found on type '{currentType.FullName}'");
                }

                currentType = property.PropertyType;
            }

            return currentType;
        }

        /// <summary>
        ///     Gets properties with a specific attribute, with option to include private properties
        /// </summary>
        public static List<PropertyInfo> GetPropertiesWithAttribute<TAttribute>(Type type, bool includePrivate)
            where TAttribute : Attribute
        {
            return GetProperties(type, includePrivate).Where(p => p.GetCustomAttribute<TAttribute>() != null).ToList();
        }

        /// <summary>
        ///     Gets a property value by path (supports nested properties with dot notation)
        /// </summary>
        public static object? GetPropertyValue(object? source, string propertyPath)
        {
            if (source == null)
            {
                return null;
            }

            var parts = propertyPath.Split('.');
            var result = source;

            foreach (var part in parts)
            {
                if (result == null)
                {
                    return null;
                }

                var type = result.GetType();
                var property = type.GetProperty(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property == null)
                {
                    return null;
                }

                result = property.GetValue(result);
            }

            return result;
        }

        /// <summary>
        ///     Sets a property value by path (supports nested properties with dot notation)
        /// </summary>
        public static void SetPropertyValue(object? source, string propertyPath, object? value)
        {
            if (source == null)
            {
                return;
            }

            var parts = propertyPath.Split('.');
            var result = source;

            // Navigate to the final object that owns the property
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (result == null)
                {
                    return;
                }

                var type = result.GetType();
                var property = type.GetProperty(parts[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property == null)
                {
                    return;
                }

                result = property.GetValue(result);
            }

            if (result == null)
            {
                return;
            }

            // Set the final property
            var finalProperty = result.GetType().GetProperty(parts[^1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (finalProperty != null && finalProperty.CanWrite)
            {
                // Handle type conversion if needed
                var targetType = finalProperty.PropertyType;
                var convertedValue = value;

                if (value != null && !targetType.IsInstanceOfType(value))
                {
                    try
                    {
                        convertedValue = Convert.ChangeType(value, targetType);
                    }
                    catch
                    {
                        // If conversion fails, try to use the original value
                    }
                }

                finalProperty.SetValue(result, convertedValue);
            }
        }

        /// <summary>
        ///     Creates a compiled getter expression from a property info
        /// </summary>
        public static Func<object, object?> CreateCompiledGetter(PropertyInfo propertyInfo, Type sourceType)
        {
            // Create parameter: (object source)
            var sourceParam = Expression.Parameter(typeof(object), "source");

            // Create cast: (SourceType)source
            var castSource = Expression.Convert(sourceParam, sourceType);

            // Create property access: ((SourceType)source).Property
            var property = Expression.Property(castSource, propertyInfo);

            // Create object conversion: (object)((SourceType)source).Property
            var castResult = Expression.Convert(property, typeof(object));

            // Build lambda: (object source) => (object)((SourceType)source).Property
            var lambda = Expression.Lambda<Func<object, object?>>(castResult, sourceParam);

            // Compile and return
            return lambda.Compile();
        }

        /// <summary>
        ///     Creates a compiled setter expression from a property info
        /// </summary>
        public static Action<object, object?>? CreateCompiledSetter(PropertyInfo propertyInfo, Type sourceType)
        {
            if (!propertyInfo.CanWrite)
            {
                return null;
            }

            // Create parameters: (object source, object value)
            var sourceParam = Expression.Parameter(typeof(object), "source");
            var valueParam = Expression.Parameter(typeof(object), "value");

            // Create casts: (SourceType)source, (PropertyType)value
            var castSource = Expression.Convert(sourceParam, sourceType);
            var castValue = Expression.Convert(valueParam, propertyInfo.PropertyType);

            // Create property access: ((SourceType)source).Property
            var property = Expression.Property(castSource, propertyInfo);

            // Create assignment: ((SourceType)source).Property = (PropertyType)value
            var assign = Expression.Assign(property, castValue);

            // Build lambda: (object source, object value) => ((SourceType)source).Property = (PropertyType)value
            var lambda = Expression.Lambda<Action<object, object?>>(assign, sourceParam, valueParam);

            // Compile and return
            return lambda.Compile();
        }

        /// <summary>
        ///     Determines if a property setter is publicly accessible
        /// </summary>
        public static bool HasPublicSetter(PropertyInfo propertyInfo)
        {
            var setMethod = propertyInfo.GetSetMethod(false);
            return setMethod != null && setMethod.IsPublic;
        }

        /// <summary>
        ///     Determines if a property has any setter (public or private)
        /// </summary>
        public static bool HasSetter(PropertyInfo propertyInfo)
        {
            var setMethod = propertyInfo.GetSetMethod(true);
            return setMethod != null;
        }

        public static List<PropertyInfo> GetProperties(Type type, bool includePrivate)
        {
            var bindingFlags = BindingFlags.Public | BindingFlags.Instance;
            if (includePrivate)
            {
                bindingFlags |= BindingFlags.NonPublic;
            }

            return type.GetProperties(bindingFlags).ToList();
        }

        /// <summary>
        ///     Replaces the "+" in nested class full names with "." for better readability.
        /// </summary>
        public static string GetDisplayFullName(Type type)
        {
            return type.FullName!.Replace("+", ".");
        }
    }
}