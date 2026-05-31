using System;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     A single captured log entry, surfaced by <see cref="IDevHostControl.SubscribeLogs" /> and
    ///     <see cref="IDevHostControl.RecentLogs" />. Structured (level / category / timestamp / message /
    ///     exception) so a tool or agent can filter without scraping console text — the programmatic
    ///     equivalent of reading the DevHost console.
    /// </summary>
    public sealed class LogLine
    {
        public LogLine(LogLevel level, string category, DateTimeOffset timestamp, string message, string? exception)
        {
            Level = level;
            Category = category;
            Timestamp = timestamp;
            Message = message;
            Exception = exception;
        }

        public LogLevel Level { get; }

        public string Category { get; }

        public DateTimeOffset Timestamp { get; }

        public string Message { get; }

        public string? Exception { get; }

        public override string ToString()
        {
            var shortCategory = Category.Contains('.') ? Category[(Category.LastIndexOf('.') + 1)..] : Category;
            var line = $"{Timestamp:HH:mm:ss.fff} [{Level}] {shortCategory}: {Message}";
            return Exception is null ? line : $"{line}\n{Exception}";
        }
    }
}
