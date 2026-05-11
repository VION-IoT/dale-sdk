using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using Spectre.Console;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Models;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class ListCommand
    {
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

                                  RenderTable(project, pluginInfo);
                                  return 0;
                              });

            return command;
        }

        private static void RenderTable(DaleProject project, DalePluginInfo pluginInfo)
        {
            DaleConsole.Info($"Project: {project.ProjectName} (v{project.Version ?? "??"})");
            if (project.SdkVersion != null)
            {
                DaleConsole.Info($"SDK: Vion.Dale.Sdk {project.SdkVersion}");
            }

            DaleConsole.Blank();

            if (pluginInfo.LogicBlocks.Count == 0)
            {
                DaleConsole.Info("No logic blocks found.");
                return;
            }

            foreach (var lb in pluginInfo.LogicBlocks)
            {
                var shortName = lb.TypeFullName.Split('.').Last();

                var table = new Table().Border(TableBorder.Rounded).AddColumn(new TableColumn(shortName).NoWrap()).AddColumn(new TableColumn(string.Empty));

                if (lb.Contracts.Count > 0)
                {
                    var contractStr = string.Join(", ", lb.Contracts.Select(c => $"{c.Identifier} ({c.MatchingContractType})"));
                    table.AddRow("Contracts", contractStr);
                }

                var allProperties = lb.Services.SelectMany(s => s.Properties).ToList();
                if (allProperties.Count > 0)
                {
                    var propStr = string.Join(", ", allProperties.Select(p => p.Identifier));
                    table.AddRow("Properties", propStr);
                }

                var allMeasuring = lb.Services.SelectMany(s => s.MeasuringPoints).ToList();
                if (allMeasuring.Count > 0)
                {
                    var mpStr = string.Join(", ", allMeasuring.Select(m => m.Identifier));
                    table.AddRow("Measuring", mpStr);
                }

                if (lb.Interfaces.Count > 0)
                {
                    var ifStr = string.Join(", ", lb.Interfaces.Select(i => i.Identifier));
                    table.AddRow("Interfaces", ifStr);
                }

                AnsiConsole.Write(table);
                AnsiConsole.WriteLine();
            }
        }

        private static CliListOutput MapToCliOutput(DalePluginInfo info, DaleProject project)
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
                                Name = lb.TypeFullName?.Split('.')[^1] ?? lb.TypeFullName ?? "Unknown",
                                FullName = lb.TypeFullName ?? "Unknown",
                                Interfaces = lb.Interfaces?.Select(i => i.Identifier ?? string.Empty).Where(s => s != string.Empty).ToList() ??
                                             new List<string>(),
                                Contracts = lb.Contracts?.Select(c => c.Identifier ?? string.Empty).Where(s => s != string.Empty).ToList() ??
                                            new List<string>(),
                                Services = lb.Services
                                             ?.Select(s => new CliServiceOutput
                                                           {
                                                               Properties = s.Properties
                                                                             ?.Select(p => new CliPropertyOutput
                                                                                           {
                                                                                               Name = p.Identifier ?? string.Empty,
                                                                                               Type = p.TypeFullName ?? string
                                                                                                          .Empty,
                                                                                           })
                                                                             .ToList() ?? new List<CliPropertyOutput>(),
                                                               MeasuringPoints = s.MeasuringPoints
                                                                                  ?.Select(m => new CliPropertyOutput
                                                                                                {
                                                                                                    Name = m.Identifier ?? string.Empty,
                                                                                                    Type = m.TypeFullName ?? string.Empty,
                                                                                                })
                                                                                  .ToList() ?? new List<CliPropertyOutput>(),
                                                           })
                                             .ToList() ?? new List<CliServiceOutput>(),
                            };
                output.LogicBlocks.Add(block);
            }

            return output;
        }
    }
}
