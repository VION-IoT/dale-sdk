using System;
using System.Reflection;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Internal reflection helpers used by TestKit infrastructure.
    /// </summary>
    internal static class ReflectionExtensions
    {
        /// <summary>
        ///     Gets the value of a (possibly non-public) instance field and casts to <typeparamref name="TValue" />.
        ///     The field search walks the type hierarchy (base classes included).
        /// </summary>
        internal static TValue? GetPrivateField<TValue>(this object instance, string fieldName)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var field = FindField(instance.GetType(), fieldName) ??
                        throw new InvalidOperationException($"Field '{fieldName}' not found on type hierarchy of '{instance.GetType().FullName}'.");

            var val = field.GetValue(instance);
            return (TValue?)val;
        }

        internal static FieldInfo? FindField(Type? type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
