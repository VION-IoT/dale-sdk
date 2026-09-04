using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Timers
{
    public static class DeclarativeTimerBinder
    {
        /// <summary>
        ///     The longest interval a real clock can wait, in seconds — the same bound a scenario's durations
        ///     carry (<c>AC-SCEN-003.2</c>), measured rather than recalled: <c>Task.Delay</c> accepts
        ///     4294967.294 s and refuses 4294968. The number is repeated here rather than shared because the
        ///     SDK targets netstandard and cannot reference the development host that declares it.
        /// </summary>
        private const double MaxIntervalSeconds = 4294967;

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

            // One identifier namespace per block. A duplicate is refused rather than resolved: the callback
            // table keeps the last registration while every registration arms its own tick chain, so the
            // block would run one method on two cadences and never run the other.
            var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var method in timerMethods)
            {
                var timerAttribute = method.GetCustomAttribute<TimerAttribute>()!;
                var identifier = ResolveIdentifier(type, method, timerAttribute, identifiers);
                var interval = ResolveInterval(type, method, timerAttribute);

                // Create the callback action
                var callback = (Action)Delegate.CreateDelegate(typeof(Action), logicBlock, method);

                timerFactory.RegisterTimer(identifier, interval, callback);
            }
        }

        /// <summary>
        ///     The identifier a timer ticks under: the attribute's, or the method's name. Refused when it is
        ///     empty, whitespace, or already taken by another timer of the same block.
        /// </summary>
        private static string ResolveIdentifier(Type type, MethodInfo method, TimerAttribute timerAttribute, Dictionary<string, string> identifiers)
        {
            var identifier = timerAttribute.Identifier ?? method.Name;

            // An identifier with nothing in it keys the callback table and tags the timer's vitals, so an
            // operator reads an unnamed timer on the dashboard and cannot say which method it is.
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new InvalidOperationException($"Method '{method.Name}' in '{type.Name}' has [Timer] attribute but an empty identifier. " +
                                                    "A timer's identifier names it in logs and in the runtime's per-timer diagnostics. " +
                                                    $"Omit the argument to use the method name. Example: [Timer(10.0)] private void {method.Name}() {{ /* timer logic */ }}");
            }

            if (identifiers.TryGetValue(identifier, out var owner))
            {
                throw new InvalidOperationException($"Methods '{owner}' and '{method.Name}' in '{type.Name}' both declare the [Timer] identifier '{identifier}'. " +
                                                    "One block cannot have two timers under one identifier: only one callback would ever run, and it would run on both intervals. " +
                                                    "Give each timer its own identifier, or omit the argument to use the method name.");
            }

            identifiers.Add(identifier, method.Name);
            return identifier;
        }

        /// <summary>
        ///     The interval a timer ticks at. The attribute's constructor refuses zero and anything below it,
        ///     which leaves four values that reach here and cannot be scheduled: not-a-number and either
        ///     infinity (both compare false against zero), a value longer than a real clock can wait, and a
        ///     positive value shorter than one clock tick — which converts to no delay at all and would arm a
        ///     self-send chain that never yields.
        /// </summary>
        private static TimeSpan ResolveInterval(Type type, MethodInfo method, TimerAttribute timerAttribute)
        {
            var seconds = timerAttribute.IntervalSeconds;
            var interval = double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds > MaxIntervalSeconds ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);

            if (interval <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"Method '{method.Name}' in '{type.Name}' has [Timer] attribute but an interval no timer can be scheduled at. " +
                                                    $"IntervalSeconds is '{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}'; it must be a finite number " +
                                                    $"of at least one clock tick and at most {MaxIntervalSeconds} seconds. " +
                                                    $"Example: [Timer(10.0)] private void {method.Name}() {{ /* timer logic */ }}");
            }

            return interval;
        }

        /// <summary>
        ///     Retrieves all void parameterless methods of the given type that are decorated with the TimerAttribute.
        /// </summary>
        private static List<MethodInfo> GetTimerMethods(Type type)
        {
            return GetDeclaredTimerMethods(type).Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0).ToList();
        }

        /// <summary>
        ///     Retrieves methods with [Timer] attribute that have invalid signatures (not void or have parameters).
        /// </summary>
        private static List<MethodInfo> GetInvalidTimerMethods(Type type)
        {
            return GetDeclaredTimerMethods(type).Where(m => m.ReturnType != typeof(void) || m.GetParameters().Length > 0).ToList();
        }

        /// <summary>
        ///     Every <c>[Timer]</c> method the instance carries, most-derived first. Walks the base chain
        ///     declaration by declaration because <c>Type.GetMethods</c> returns a base class's <em>private</em>
        ///     members for no binding flags: a base class of blocks that schedules its own cycle from a private
        ///     method would otherwise bind no timer and never tick. An override and the virtual it overrides are
        ///     one timer, so the walk keeps the declaration it reaches first and drops the one it overrides.
        /// </summary>
        private static List<MethodInfo> GetDeclaredTimerMethods(Type type)
        {
            var definitions = new HashSet<MethodInfo>();
            var methods = new List<MethodInfo>();

            for (var declaring = type; declaring != null && declaring != typeof(object); declaring = declaring.BaseType)
            {
                foreach (var method in declaring.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method.GetCustomAttribute<TimerAttribute>() == null || !definitions.Add(method.GetBaseDefinition()))
                    {
                        continue;
                    }

                    methods.Add(method);
                }
            }

            return methods;
        }
    }
}