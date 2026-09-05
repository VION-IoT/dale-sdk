using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json;
using Spectre.Console;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Models;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class ListCommand
    {
        /// <summary>
        ///     Contract annotation key the SDK emits for a contract type declared
        ///     <c>DevelopmentOnly</c> (<c>ServiceProviderContractAnnotations.DevelopmentOnly</c>). Duplicated
        ///     rather than referenced, like the rest of this mirror — the CLI takes no Vion dependency.
        /// </summary>
        private const string DevelopmentOnlyAnnotation = "developmentOnly";

        public static Command Create()
        {
            var command = new Command("list", "Show project info (logic blocks, contracts, properties, etc.)");

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  DalePluginInfo? pluginInfo = null;
                                  await DaleConsole.WithSpinner("Building and introspecting", async () => { pluginInfo = await ParserRunner.RunIntrospectionAsync(project); });

                                  if (pluginInfo == null)
                                  {
                                      DaleConsole.Error("Introspection failed. Ensure the project builds and Vion.Dale.LogicBlockParser is available.");
                                      return 1;
                                  }

                                  if (DaleConsole.JsonMode)
                                  {
                                      var cliOutput = MapToCliOutput(pluginInfo, project);
                                      DaleConsole.WriteJsonResult(cliOutput);
                                      return 0;
                                  }

                                  RenderTable(AnsiConsole.Console, project, pluginInfo);
                                  return 0;
                              });

            return command;
        }

        /// <summary>
        ///     The last segment of a logic block's identity. The identity is the CLR full type name
        ///     (`AC-INTRO-004.1`), so a nested block's short name sits past the nesting separator as well as
        ///     the namespace separator — `Outer+Inner` is two names, not one.
        /// </summary>
        internal static string ShortName(string? typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName))
            {
                return "Unknown";
            }

            return typeFullName.Split('.', '+')[^1];
        }

        internal static CliListOutput MapToCliOutput(DalePluginInfo info, DaleProject project)
        {
            var output = new CliListOutput
                         {
                             PackageId = info.PackageId ?? project.PackageId ?? project.ProjectName,
                             Version = info.PackageVersion ?? project.Version,
                             SdkVersion = project.SdkVersion,
                             LogicBlocks = new List<CliLogicBlockOutput>(),
                         };

            foreach (var lb in info.LogicBlocks)
            {
                var block = new CliLogicBlockOutput
                            {
                                Name = ShortName(lb.TypeFullName),
                                FullName = lb.TypeFullName ?? "Unknown",
                                Interfaces = lb.Interfaces?.Select(i => i.Identifier ?? string.Empty).ToList() ?? new List<string>(),
                                Contracts = lb.Contracts?.Select(c => c.Identifier ?? string.Empty).ToList() ?? new List<string>(),
                                DevelopmentOnly = IsDevelopmentOnly(lb),
                                Services = lb.Services
                                             ?.Select(s => new CliServiceOutput
                                                           {
                                                               Name = s.Identifier ?? string.Empty,
                                                               IncludedWhen = s.IncludedWhen,
                                                               Properties = s.Properties
                                                                             ?.Select(p => new CliPropertyOutput
                                                                                           {
                                                                                               Name = p.Identifier ?? string.Empty,
                                                                                               Type =
                                                                                                   SchemaSummary
                                                                                                       .Describe(p.Schema),
                                                                                           })
                                                                             .ToList() ?? new List<CliPropertyOutput>(),
                                                               MeasuringPoints = s.MeasuringPoints
                                                                                  ?.Select(m => new CliPropertyOutput
                                                                                                {
                                                                                                    Name = m.Identifier ?? string.Empty,
                                                                                                    Type = SchemaSummary.Describe(m.Schema),
                                                                                                })
                                                                                  .ToList() ?? new List<CliPropertyOutput>(),
                                                           })
                                             .ToList() ?? new List<CliServiceOutput>(),
                            };
                output.LogicBlocks.Add(block);
            }

            return output;
        }

        /// <summary>
        ///     Whether the block binds a contract the SDK declares development-only (a provider face). Read
        ///     from the introspection annotation the parser emits — the same flag the pack gate filters on and
        ///     the production runtime refuses. <c>dale list</c> shows such a block, marked; it never hides it.
        /// </summary>
        internal static bool IsDevelopmentOnly(LogicBlockResult logicBlock)
        {
            return logicBlock.Contracts?.Any(IsDevelopmentOnlyContract) == true;
        }

        /// <summary>
        ///     The human listing. The console is a parameter so the whole rendering — the project header and
        ///     the empty-project line as well as the tables, and the escaping every identifier needs before
        ///     Spectre reads it as markup — can be asserted against a captured writer.
        /// </summary>
        internal static void RenderTable(IAnsiConsole console, DaleProject project, DalePluginInfo pluginInfo)
        {
            DaleConsole.Info(console, $"Project: {project.ProjectName} (v{project.Version ?? "??"})");
            if (project.SdkVersion != null)
            {
                DaleConsole.Info(console, $"SDK: Vion.Dale.Sdk {project.SdkVersion}");
            }

            DaleConsole.Blank(console);

            if (pluginInfo.LogicBlocks.Count == 0)
            {
                DaleConsole.Info(console, "No logic blocks found.");
                return;
            }

            foreach (var lb in pluginInfo.LogicBlocks)
            {
                var shortName = ShortName(lb.TypeFullName);

                // A development-only block is listed like any other, marked — it is part of the project, it
                // just never reaches the cloud (`dale pack` filters it out of the introspection JSON).
                var header = IsDevelopmentOnly(lb) ? $"{Markup.Escape(shortName)} [yellow](development-only)[/]" : Markup.Escape(shortName);

                var table = new Table().Border(TableBorder.Rounded).AddColumn(new TableColumn(header).NoWrap()).AddColumn(new TableColumn(string.Empty));

                // Every rendered value is escaped: an identifier is author-supplied and Spectre reads
                // square brackets as markup, so an unescaped one corrupts the row or throws.
                var contracts = lb.Contracts ?? new List<ContractInfo>();
                if (contracts.Count > 0)
                {
                    table.AddRow("Contracts", Markup.Escape(string.Join(", ", contracts.Select(c => $"{c.Identifier} ({c.MatchingContractType})"))));
                }

                var services = lb.Services ?? new List<ServiceInfo>();

                var allProperties = services.SelectMany(service => service.Properties ?? new List<ServicePropertyInfo>()).ToList();
                if (allProperties.Count > 0)
                {
                    table.AddRow("Properties", Markup.Escape(string.Join(", ", allProperties.Select(property => property.Identifier))));
                }

                var allMeasuring = services.SelectMany(service => service.MeasuringPoints ?? new List<ServiceMeasuringPointInfo>()).ToList();
                if (allMeasuring.Count > 0)
                {
                    table.AddRow("Measuring", Markup.Escape(string.Join(", ", allMeasuring.Select(point => point.Identifier))));
                }

                var interfaces = lb.Interfaces ?? new List<InterfaceInfo>();
                if (interfaces.Count > 0)
                {
                    table.AddRow("Interfaces", Markup.Escape(string.Join(", ", interfaces.Select(i => i.Identifier))));
                }

                console.Write(table);
                console.WriteLine();
            }
        }

        private static bool IsDevelopmentOnlyContract(ContractInfo contract)
        {
            if (contract.Annotations is null || !contract.Annotations.TryGetValue(DevelopmentOnlyAnnotation, out var flag))
            {
                return false;
            }

            // The mirror deserializes annotation values as JsonElement; a value set in-process is a bool.
            return flag switch
            {
                bool value => value,
                JsonElement element => element.ValueKind == JsonValueKind.True,
                _ => false,
            };
        }
    }
}