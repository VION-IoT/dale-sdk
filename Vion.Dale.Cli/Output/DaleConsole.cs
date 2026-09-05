using System;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;
using Vion.Dale.Cli.Infrastructure;

namespace Vion.Dale.Cli.Output
{
    public static class DaleConsole
    {
        /// <summary>
        ///     Where a table-mode failure is written. Standard error, so a caller that captures <c>2&gt;</c>
        ///     to report a failed step captures the sentence that explains it, and a caller that pipes
        ///     standard output gets only the tool's answer. JSON mode deliberately keeps one stream (see
        ///     <see cref="Error" />).
        /// </summary>
        private static readonly Lazy<IAnsiConsole> LazyErrorConsole = new(() => AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) }));

        private static IAnsiConsole ErrorConsole
        {
            get => LazyErrorConsole.Value;
        }

        public static bool JsonMode { get; set; }

        public static bool VerboseMode { get; set; }

        public static void Success(string pastVerb, string detail)
        {
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"  [green]✓[/] {pastVerb} {Markup.Escape(detail)}");
        }

        public static void Error(string message)
        {
            if (JsonMode)
            {
                // Structured JSON error on stdout so agents can parse it — one stream in this mode, which
                // is what a single-stream parser expects.
                Console.WriteLine(JsonSerializer.Serialize(new { error = message }, JsonDefaults.Options));
                return;
            }

            ErrorConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(message)}");
        }

        public static void Info(string message)
        {
            Info(AnsiConsole.Console, message);
        }

        /// <summary>
        ///     The same line, written to <paramref name="console" />. A renderer that takes its console as a
        ///     parameter has to write <em>every</em> line through it, or the half that still goes to the
        ///     global is invisible to the writer a test captures.
        /// </summary>
        public static void Info(IAnsiConsole console, string message)
        {
            if (JsonMode)
            {
                return;
            }

            console.MarkupLine($"  {Markup.Escape(message)}");
        }

        public static void Verbose(string message)
        {
            if (!VerboseMode || JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(message)}[/]");
        }

        public static void Blank()
        {
            Blank(AnsiConsole.Console);
        }

        /// <summary>The same blank line, written to <paramref name="console" />.</summary>
        public static void Blank(IAnsiConsole console)
        {
            if (JsonMode)
            {
                return;
            }

            console.WriteLine();
        }

        public static async Task WithSpinner(string gerund, Func<Task> action)
        {
            if (JsonMode)
            {
                await action();
                return;
            }

            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync($"  {gerund}...", async _ => await action());
        }

        public static void Warning(string message)
        {
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"  [yellow]⚠[/] {Markup.Escape(message)}");
        }

        public static void Header(string title)
        {
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"\n  [bold]{Markup.Escape(title)}[/]");
        }

        public static void KeyValue(string key, string value)
        {
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(key)}[/]  {Markup.Escape(value)}");
        }

        public static void WriteJson(string json)
        {
            Console.WriteLine(json);
        }

        /// <summary>
        ///     Serialize and write an object as JSON to stdout.
        ///     Convenience wrapper to eliminate repeated JsonSerializer.Serialize + JsonDefaults.Options calls.
        /// </summary>
        public static void WriteJsonResult(object result)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonDefaults.Options));
        }
    }
}