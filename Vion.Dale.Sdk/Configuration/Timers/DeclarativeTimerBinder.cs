using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Timers
{
    public static class DeclarativeTimerBinder
    {
        public static void BindTimersFromAttributes(object logicBlock, ITimerFactory timerFactory)
        {
            var type = logicBlock.GetType();
            var timerMethods = GetTimerMethods(type);
            var invalidTimerMethods = GetInvalidTimerMethods(type);

            // Provide helpful error messages for invalid timer methods
            foreach (var method in invalidTimerMethods)
            {
                var parameters = method.GetParameters();
                var parameterInfo = parameters.Length > 0 ?
                                        $"has {parameters.Length} parameter(s): ({string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))})" :
                                        "is parameterless";

                throw new InvalidOperationException($"Method '{method.Name}' in '{type.Name}' has [Timer] attribute but invalid signature. " +
                                                    $"Method returns '{method.ReturnType.Name}' and {parameterInfo}. " + $"Timer methods must be void and parameterless. " +
                                                    $"Example: [Timer(10.0)] private void {method.Name}() {{ /* timer logic */ }}");
            }

            foreach (var method in timerMethods)
            {
                var timerAttribute = method.GetCustomAttribute<TimerAttribute>()!;
                var identifier = timerAttribute.Identifier ?? method.Name;
                var interval = TimeSpan.FromSeconds(timerAttribute.IntervalSeconds);

                // Create the callback action
                var callback = (Action)Delegate.CreateDelegate(typeof(Action), logicBlock, method);

                timerFactory.RegisterTimer(identifier, interval, callback);
            }
        }

        /// <summary>
        ///     Retrieves all void parameterless methods of the given type that are decorated with the TimerAttribute.
        /// </summary>
        private static List<MethodInfo> GetTimerMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       .Where(m => m.GetCustomAttribute<TimerAttribute>() != null)
                       .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                       .ToList();
        }

        /// <summary>
        ///     Retrieves methods with [Timer] attribute that have invalid signatures (not void or have parameters).
        /// </summary>
        private static List<MethodInfo> GetInvalidTimerMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       .Where(m => m.GetCustomAttribute<TimerAttribute>() != null)
                       .Where(m => m.ReturnType != typeof(void) || m.GetParameters().Length > 0)
                       .ToList();
        }
    }
}