using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Static helper methods to create logic block instances with mocked dependencies for testing.
    /// </summary>
    [PublicApi]
    public static class LogicBlockTestHelper
    {
        /// <summary>
        ///     Creates a mock ILogger for logic blocks.
        /// </summary>
        public static Mock<ILogger> CreateLoggerMock()
        {
            return new Mock<ILogger>();
        }

        /// <summary>
        ///     Creates a logic block instance with a default logger mock.
        ///     The logic block must have a constructor that accepts <see cref="ILogger" />.
        ///     <code>var block = LogicBlockTestHelper.Create&lt;MyBlock&gt;();</code>
        /// </summary>
        /// <exception cref="MissingMethodException">
        ///     Thrown when <typeparamref name="T" /> is abstract, or declares no public constructor taking a
        ///     single <see cref="ILogger" />.
        /// </exception>
        public static T Create<T>()
            where T : LogicBlockBase
        {
            return Construct<T>(CreateLoggerMock());
        }

        /// <summary>
        ///     Creates a logic block instance and returns both the instance and the logger mock,
        ///     for tests that need to verify log output.
        ///     <code>var (block, loggerMock) = LogicBlockTestHelper.CreateWithLogger&lt;MyBlock&gt;();</code>
        /// </summary>
        /// <exception cref="MissingMethodException">
        ///     Thrown when <typeparamref name="T" /> is abstract, or declares no public constructor taking a
        ///     single <see cref="ILogger" />.
        /// </exception>
        public static (T LogicBlock, Mock<ILogger> LoggerMock) CreateWithLogger<T>()
            where T : LogicBlockBase
        {
            var loggerMock = CreateLoggerMock();
            return (Construct<T>(loggerMock), loggerMock);
        }

        /// <summary>
        ///     The one construction path both entry points take. Reflection's own refusals name the type and
        ///     nothing else — "Constructor on type 'X' not found." is true of an abstract type, of a block
        ///     taking a different constructor, and of nothing a caller can act on — so each shape is
        ///     recognised before the call and told what it needs.
        /// </summary>
        private static T Construct<T>(Mock<ILogger> loggerMock)
            where T : LogicBlockBase
        {
            var type = typeof(T);
            if (type.IsAbstract)
            {
                throw new MissingMethodException($"Logic block type '{type.FullName}' is abstract and cannot be constructed. " +
                                                 "Pass the concrete block under test.");
            }

            if (type.GetConstructor(new[] { typeof(ILogger) }) == null)
            {
                throw new MissingMethodException($"Logic block type '{type.FullName}' declares no public constructor taking a single " +
                                                 $"{typeof(ILogger).FullName}. Construct it yourself and call CreateTestContext() on the instance.");
            }

            try
            {
                return (T)Activator.CreateInstance(type, loggerMock.Object)!;
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // The block's own constructor threw. Rethrow it with its stack intact: the reflection wrapper
                // names the invocation and buries the reason the author needs.
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
