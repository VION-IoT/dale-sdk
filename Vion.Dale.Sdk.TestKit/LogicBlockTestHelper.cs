using System;
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
        public static T Create<T>()
            where T : LogicBlockBase
        {
            var loggerMock = CreateLoggerMock();
            return (T)Activator.CreateInstance(typeof(T), loggerMock.Object)!;
        }

        /// <summary>
        ///     Creates a logic block instance and returns both the instance and the logger mock,
        ///     for tests that need to verify log output.
        ///     <code>var (block, loggerMock) = LogicBlockTestHelper.CreateWithLogger&lt;MyBlock&gt;();</code>
        /// </summary>
        public static (T LogicBlock, Mock<ILogger> LoggerMock) CreateWithLogger<T>()
            where T : LogicBlockBase
        {
            var loggerMock = CreateLoggerMock();
            var instance = (T)Activator.CreateInstance(typeof(T), loggerMock.Object)!;
            return (instance, loggerMock);
        }
    }
}
