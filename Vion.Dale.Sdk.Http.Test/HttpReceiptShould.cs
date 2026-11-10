using System;
using System.Collections.Generic;
using System.Net;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What <c>RetryAfter</c> reads off headers no platform rendered first — a receipt a block builds itself, as a test
    ///     double does. Through the client the platform has already rendered every date it understands canonical, so these
    ///     rows cannot reach the parse that way; <c>HttpRequestExecutorShould</c> proves the delay through the client.
    /// </summary>
    [TestClass]
    public class HttpReceiptShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.7")]
        [DataRow("tue, 10 nov 2026 12:02:00 GMT", DisplayName = "day and month names in the wrong case")]
        [DataRow("Mon, 10 Nov 2026 12:02:00 GMT", DisplayName = "a day name the date does not fall on")]
        [DataRow("Tue, 10 Nov 2026 12:02:00 +0000", DisplayName = "a numeric zone")]
        public void ReportNoRetryAfterDelayForDateOutsideGrammar(string retryAfter)
        {
            // Arrange
            var sut = new HttpReceipt(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
                                      0,
                                      TimeSpan.Zero,
                                      HttpOutcome.ClientError,
                                      HttpStatusCode.TooManyRequests,
                                      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = retryAfter });

            // Act
            var retryAfterDelay = sut.RetryAfter;

            // Assert
            Assert.IsNull(retryAfterDelay);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.6")]
        [DataRow("Tue, 10 Nov 2026 12:02:00 GMT", 120)]
        [DataRow("Tue, 10 Nov 2026 12:00:30 GMT", 30)]
        public void ReportRetryAfterDelayForDateInGrammar(string retryAfter, int expectedSeconds)
        {
            // Arrange
            var sut = new HttpReceipt(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
                                      0,
                                      TimeSpan.Zero,
                                      HttpOutcome.ClientError,
                                      HttpStatusCode.TooManyRequests,
                                      new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Retry-After"] = retryAfter });

            // Act
            var retryAfterDelay = sut.RetryAfter;

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), retryAfterDelay);
        }
    }
}
