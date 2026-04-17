using System;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.Logging;
using Moq;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Extension methods to verify log output on ILogger mocks.
    /// </summary>
    [PublicApi]
    public static class ILoggerMockExtensions
    {
        /// <summary>
        ///     Verifies that a log entry containing the specified string was logged at the specified log level the expected number
        ///     of times.
        /// </summary>
        /// <summary>
        ///     Verifies that a log entry containing the specified string was logged at the specified log level the expected number
        ///     of times.
        /// </summary>
        public static void VerifyLogContains(this Mock<ILogger> loggerMock, string contains, LogLevel logLevel, Times times)
        {
            loggerMock.Verify(l => l.Log(logLevel,
                                         It.IsAny<EventId>(),
                                         It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(contains)),
                                         It.IsAny<Exception>(),
                                         It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                              times);
        }

        public static void VerifyLogContains<T>(this Mock<ILogger<T>> loggerMock, string contains, LogLevel logLevel, Times times)
        {
            loggerMock.Verify(l => l.Log(logLevel,
                                         It.IsAny<EventId>(),
                                         It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(contains)),
                                         It.IsAny<Exception>(),
                                         It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                              times);
        }
    }
}