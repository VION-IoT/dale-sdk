using System;
using System.Text.Json;
using System.Threading.Tasks;
using Vion.Dale.Cli.Infrastructure;
using Spectre.Console;

namespace Vion.Dale.Cli.Output
{
    public static class DaleConsole
    {
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
                // Structured JSON error on stdout so agents can parse it
                Console.WriteLine(JsonSerializer.Serialize(new { error = message }, JsonDefaults.Options));
                return;
            }

            AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(message)}");
        }

        public static void Info(string message)
        {
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.MarkupLine($"  {Markup.Escape(message)}");
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
            if (JsonMode)
            {
                return;
            }

            AnsiConsole.WriteLine();
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