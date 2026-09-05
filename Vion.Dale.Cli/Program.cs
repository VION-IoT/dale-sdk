using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Commands.Add;
using Vion.Dale.Cli.Commands.Auth;
using Vion.Dale.Cli.Commands.Config;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            UseUtf8Output();
            CliComposition.UseProductionDependencies();

            var rootCommand = BuildRootCommand();
            var parseResult = rootCommand.Parse(args);

            // --version answers before any command runs, so it works in a directory with no project and
            // with no credential store. It is a root-level flag: a subcommand name claims the rest of the
            // line, which is what keeps `dale pack --version 1.2.3` a pack and `dale build --version` a
            // forwarded token. The output mode is read off the parse result rather than the raw arguments,
            // so `--version --output json` gets a document like every other answer.
            if (WantsVersion(args, TopLevelCommandNames(rootCommand)))
            {
                if (WantsJsonOutput(parseResult))
                {
                    DaleConsole.WriteJsonResult(new { version = Version() });
                }
                else
                {
                    Console.WriteLine($"dale {Version()} — Vion IoT");
                }

                return 0;
            }

            // A refused option value is the parser's to report. Reading a global option before the
            // invocation would let its conversion throw out of Main, past the handler that turns a bad
            // command line into one line and exit 1.
            if (parseResult.Errors.Count > 0)
            {
                return await parseResult.InvokeAsync();
            }

            // Configure output mode
            DaleConsole.JsonMode = parseResult.GetValue<string>("--output") == "json";
            DaleConsole.VerboseMode = parseResult.GetValue<bool>("--verbose");

            return await parseResult.InvokeAsync();
        }

        /// <summary>
        ///     Whether the command line asks for the tool's version. True only while no subcommand has been
        ///     named: past a subcommand, <c>--version</c> belongs to that command (<c>dale pack --version</c>)
        ///     or is forwarded to <c>dotnet</c> (<c>dale build --version</c>).
        /// </summary>
        internal static bool WantsVersion(IReadOnlyList<string> args, IReadOnlyCollection<string> subcommandNames)
        {
            foreach (var arg in args)
            {
                if (subcommandNames.Contains(arg))
                {
                    return false;
                }

                if (arg is "--version" or "-v")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     The tool's version with the source-link commit suffix removed — that suffix is build
        ///     provenance, not the version a consumer pins.
        /// </summary>
        internal static string Version()
        {
            var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                          typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

            var plusIndex = version.IndexOf('+');
            return plusIndex >= 0 ? version.Substring(0, plusIndex) : version;
        }

        internal static HashSet<string> TopLevelCommandNames(RootCommand rootCommand)
        {
            return rootCommand.Subcommands.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);
        }

        internal static RootCommand BuildRootCommand()
        {
            var rootCommand = new RootCommand("dale — develop and publish Dale LogicBlock libraries");

            var outputOption = new Option<string>("--output", "-o")
                               {
                                   Description = "Output format",
                                   DefaultValueFactory = _ => "table",
                                   Recursive = true,
                               };
            outputOption.AcceptOnlyFromAmong("table", "json");
            rootCommand.Options.Add(outputOption);

            var projectOption = new Option<string?>("--project")
                                {
                                    Description = "Path to .csproj file",
                                    Recursive = true,
                                };
            rootCommand.Options.Add(projectOption);

            var verboseOption = new Option<bool>("--verbose") { Description = "Show detailed output", Recursive = true };
            rootCommand.Options.Add(verboseOption);

            // --- Local development ---

            rootCommand.Subcommands.Add(NewCommand.Create());
            rootCommand.Subcommands.Add(BuildCommand.Create());
            rootCommand.Subcommands.Add(TestCommand.Create());
            rootCommand.Subcommands.Add(DevCommand.Create());
            rootCommand.Subcommands.Add(ListCommand.Create());
            rootCommand.Subcommands.Add(ScenarioCommand.Create());

            var addCommand = new Command("add", "Add elements to a LogicBlock project");
            addCommand.Subcommands.Add(AddLogicBlockCommand.Create());
            addCommand.Subcommands.Add(AddServicePropertyCommand.Create());
            addCommand.Subcommands.Add(AddMeasuringPointCommand.Create());
            addCommand.Subcommands.Add(AddTimerCommand.Create());
            rootCommand.Subcommands.Add(addCommand);

            // --- Publishing ---

            rootCommand.Subcommands.Add(PackCommand.Create());
            rootCommand.Subcommands.Add(UploadCommand.Create());

            // --- Auth & config ---

            rootCommand.Subcommands.Add(LoginCommand.Create());
            rootCommand.Subcommands.Add(LogoutCommand.Create());
            rootCommand.Subcommands.Add(WhoamiCommand.Create());

            var configCommand = new Command("config", "Manage CLI configuration");
            configCommand.Subcommands.Add(ShowConfigCommand.Create());
            configCommand.Subcommands.Add(SetIntegratorCommand.Create());
            configCommand.Subcommands.Add(SetEnvironmentCommand.Create());
            rootCommand.Subcommands.Add(configCommand);

            return rootCommand;
        }

        /// <summary>
        ///     Whether the command line selects JSON output. Readable even where <c>--version</c> is itself an
        ///     unrecognised token, which is what lets the version answer carry the mode; it throws only where
        ///     <c>--output</c> was given a value the option refuses, and a refused value is not JSON mode —
        ///     the refusal itself belongs to the command that claimed the line.
        /// </summary>
        private static bool WantsJsonOutput(ParseResult parseResult)
        {
            try
            {
                return parseResult.GetValue<string>("--output") == "json";
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Writes standard output and standard error as UTF-8. The console's default encoding maps every
        ///     non-ASCII character this tool prints — the status glyphs, the em dash — to a replacement or a
        ///     best-fit substitute as soon as the stream is redirected, so a CI log and a script's capture
        ///     see text the tool never wrote. Best effort: a host that refuses the change is left alone.
        /// </summary>
        private static void UseUtf8Output()
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
                // Some hosts (a redirected handle on an older console, a restricted sandbox) refuse it.
            }
        }
    }
}