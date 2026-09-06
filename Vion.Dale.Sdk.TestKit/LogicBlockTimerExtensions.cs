using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Extension methods for simulating timer ticks in unit tests.
    ///     Uses reflection to access the internal timer callback dictionary on <see cref="LogicBlockBase" />.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTimerExtensions
    {
        /// <summary>
        ///     Fires a timer callback by its identifier.
        ///     The identifier is typically the method name unless overridden via
        ///     <c>[Timer(identifier: "custom")]</c>.
        /// </summary>
        public static void FireTimer(this LogicBlockBase logicBlock, string identifier)
        {
            var callbacks = GetTimerCallbacks(logicBlock);

            var timer = RequireTimer(callbacks, identifier);

            timer.callback();
        }

        /// <summary>
        ///     Fires a timer callback using a method selector expression for type-safety.
        ///     The method must be accessible from the test.
        ///     <code>block.FireTimer((MyBlock lb) => lb.OnTimer());</code>
        /// </summary>
        public static void FireTimer<TLogicBlock>(this TLogicBlock logicBlock, Expression<Action<TLogicBlock>> timerMethodSelector)
            where TLogicBlock : LogicBlockBase
        {
            var methodName = GetMethodName(timerMethodSelector);
            logicBlock.FireTimer(methodName);
        }

        /// <summary>
        ///     Returns the configured interval for the specified timer.
        /// </summary>
        public static TimeSpan GetTimerInterval(this LogicBlockBase logicBlock, string identifier)
        {
            var callbacks = GetTimerCallbacks(logicBlock);

            return RequireTimer(callbacks, identifier).interval;
        }

        /// <summary>
        ///     Returns the configured interval for the specified timer using a method selector expression.
        ///     <code>var interval = block.GetTimerInterval((MyBlock lb) => lb.OnTimer());</code>
        /// </summary>
        public static TimeSpan GetTimerInterval<TLogicBlock>(this TLogicBlock logicBlock, Expression<Action<TLogicBlock>> timerMethodSelector)
            where TLogicBlock : LogicBlockBase
        {
            var methodName = GetMethodName(timerMethodSelector);
            return logicBlock.GetTimerInterval(methodName);
        }

        /// <summary>
        ///     The one refusal both queries give. Naming the registered identifiers is most of the message's
        ///     value — the mistake behind an unknown identifier is almost always a typo or a renamed method,
        ///     and the answer is in the list — so neither query may give the short form.
        /// </summary>
        private static (TimeSpan interval, Action callback) RequireTimer(Dictionary<string, (TimeSpan interval, Action callback)> callbacks, string identifier)
        {
            if (callbacks.TryGetValue(identifier, out var timer))
            {
                return timer;
            }

            var available = callbacks.Count > 0 ? string.Join(", ", callbacks.Keys.Select(k => $"'{k}'")) : "(none)";

            throw new TestKitVerificationException($"No timer registered with identifier '{identifier}'. Available timers: {available}.");
        }

        private static Dictionary<string, (TimeSpan interval, Action callback)> GetTimerCallbacks(LogicBlockBase logicBlock)
        {
            var callbacks = logicBlock.GetPrivateField<Dictionary<string, (TimeSpan interval, Action callback)>>("_timerCallbacks");
            if (callbacks == null)
            {
                throw new TestKitVerificationException("Could not access timer callbacks on LogicBlockBase.");
            }

            return callbacks;
        }

        private static string GetMethodName<TLogicBlock>(Expression<Action<TLogicBlock>> expression)
        {
            if (expression.Body is MethodCallExpression methodCall)
            {
                return methodCall.Method.Name;
            }

            throw new ArgumentException("Expression must be a method call, e.g. lb => lb.OnTimer()", nameof(expression));
        }
    }
}