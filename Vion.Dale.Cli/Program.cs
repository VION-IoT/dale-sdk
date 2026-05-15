using System;
using System.CommandLine;
using System.Reflection;
using System.Threading.Tasks;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Commands.Add;
using Vion.Dale.Cli.Commands.Auth;
using Vion.Dale.Cli.Commands.Config;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Handle --version before command parsing
            if (args.Length == 1 && args[0] is "--version" or "-v")
            {
                var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                              typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";

                // Strip SourceLink git hash suffix (e.g. "0.1.0+abc123" → "0.1.0")
                var plusIndex = version.IndexOf('+');
                if (plusIndex >= 0)
                {
                    version = version.Substring(0, plusIndex);
                }

                Console.WriteLine($"dale {version} — Vion IoT");
                return 0;
            }

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

            // Configure output mode
            var parseResult = rootCommand.Parse(args);
            DaleConsole.JsonMode = parseResult.GetValue<string>("--output") == "json";
            DaleConsole.VerboseMode = parseResult.GetValue(verboseOption);

            return await parseResult.InvokeAsync();
        }
    }
}