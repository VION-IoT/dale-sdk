using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    /// <summary>
    ///     The scenario verbs (RFC 0006 R4). Per the CLI's rules they carry no SDK dependency — they operate
    ///     on <c>*.scenario.json</c> files, the wired-host configuration export
    ///     (<c>dale dev --export-config</c>), and the running host's localhost <c>/api</c>. The C#
    ///     <c>ScenarioRunner</c> stays the one authoritative evaluator; <c>validate</c> is a deliberately
    ///     lite, language-neutral mirror of its up-front checks (the same checks a Python runner would
    ///     implement), built for catching renames fast in CI.
    /// </summary>
    public static class ScenarioCommand
    {
        public static Command Create()
        {
            var command = new Command("scenario", "Work with *.scenario.json files (RFC 0006): run, validate, generate the editor schema, open in the Player");
            command.Subcommands.Add(CreateRun());
            command.Subcommands.Add(CreateValidate());
            command.Subcommands.Add(CreateSchema());
            command.Subcommands.Add(CreateOpen());
            return command;
        }

        private static Option<int> PortOption()
        {
            return new Option<int>("--port") { Description = "Port of the running DevHost (default 5000).", DefaultValueFactory = _ => 5000 };
        }

        private static Command CreateRun()
        {
            var run = new Command("run", "Execute a scenario against the running DevHost and report the result — the same report the Player's copy button produces");
            var idArgument = new Argument<string>("id") { Description = "Scenario id (the file name without .scenario.json)." };
            var portOption = PortOption();
            var restartOption = new Option<bool>("--restart") { Description = "Cancel an active run and take over (the API's ?restart=true)." };
            var forceOption = new Option<bool>("--force") { Description = "Proceed despite a topology mismatch (the Player's run-anyway)." };
            var timeoutOption = new Option<int>("--timeout") { Description = "Seconds to wait for the run to finish (default 600).", DefaultValueFactory = _ => 600 };
            run.Arguments.Add(idArgument);
            run.Options.Add(portOption);
            run.Options.Add(restartOption);
            run.Options.Add(forceOption);
            run.Options.Add(timeoutOption);

            run.SetAction(async (parseResult, cancellationToken) =>
                          {
                              var id = parseResult.GetValue(idArgument)!;
                              var port = parseResult.GetValue(portOption);
                              using var http = NewClient(port);

                              var query = new List<string>();
                              if (parseResult.GetValue(restartOption))
                              {
                                  query.Add("restart=true");
                              }

                              if (parseResult.GetValue(forceOption))
                              {
                                  query.Add("force=true");
                              }

                              HttpResponseMessage apply;
                              try
                              {
                                  apply = await http.PostAsync($"/api/scenarios/{Uri.EscapeDataString(id)}/apply{(query.Count > 0 ? "?" + string.Join("&", query) : "")}",
                                                               null,
                                                               cancellationToken);
                              }
                              catch (HttpRequestException e)
                              {
                                  DaleConsole.Error($"No DevHost reachable on port {port} ({e.Message}). Start one with `dale dev --headless`.");
                                  return 1;
                              }

                              var applyBody = await apply.Content.ReadAsStringAsync(cancellationToken);
                              if (!apply.IsSuccessStatusCode)
                              {
                                  DaleConsole.Error($"apply returned {(int)apply.StatusCode}: {applyBody}");
                                  return 1;
                              }

                              var runId = JsonNode.Parse(applyBody)?["runId"]?.GetValue<string>();
                              var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(parseResult.GetValue(timeoutOption));
                              JsonNode? report = null;
                              while (DateTimeOffset.UtcNow < deadline)
                              {
                                  var response = await http.GetAsync($"/api/scenarios/{Uri.EscapeDataString(id)}/run", cancellationToken);
                                  if (response.IsSuccessStatusCode)
                                  {
                                      report = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                                      var current = report?["runId"]?.GetValue<string>();
                                      var status = report?["status"]?.GetValue<string>();
                                      if (current == runId && status != "running")
                                      {
                                          break;
                                      }
                                  }

                                  await Task.Delay(500, cancellationToken);
                              }

                              if (report is null || report["status"]?.GetValue<string>() == "running")
                              {
                                  DaleConsole.Error($"run '{runId}' did not finish within the timeout — it may still be executing.");
                                  return 1;
                              }

                              if (DaleConsole.JsonMode)
                              {
                                  Console.WriteLine(report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                              }
                              else
                              {
                                  RenderReport(report);
                              }

                              return report["status"]?.GetValue<string>() == "succeeded" ? 0 : 1;
                          });

            return run;
        }

        private static Command CreateValidate()
        {
            var validate = new Command("validate",
                                       "Validate every scenario file: structure, name paths, and topology against the wired-host configuration — catches renames fast in CI");
            var dirOption = new Option<string>("--dir") { Description = "Scenarios directory (default ./scenarios).", DefaultValueFactory = _ => "scenarios" };
            var configOption = new Option<string?>("--config")
                               {
                                   Description =
                                       "Configuration export from `dale dev --export-config <file>`. When omitted, the running DevHost's /api/configuration is used.",
                               };
            var portOption = PortOption();
            validate.Options.Add(dirOption);
            validate.Options.Add(configOption);
            validate.Options.Add(portOption);

            validate.SetAction(async (parseResult, cancellationToken) =>
                               {
                                   var dir = parseResult.GetValue(dirOption)!;
                                   if (!Directory.Exists(dir))
                                   {
                                       DaleConsole.Error($"No scenarios directory at '{Path.GetFullPath(dir)}'.");
                                       return 1;
                                   }

                                   var config = await LoadConfigAsync(parseResult.GetValue(configOption), parseResult.GetValue(portOption), cancellationToken);
                                   if (config is null)
                                   {
                                       return 1;
                                   }

                                   var results = new List<object>();
                                   var failed = false;
                                   foreach (var path in Directory.EnumerateFiles(dir, "*.scenario.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                                   {
                                       var fileName = Path.GetFileName(path);
                                       var outcome = ScenarioFileChecks.Validate(fileName, File.ReadAllText(path), config);
                                       failed |= outcome.Errors.Count > 0;
                                       results.Add(new { file = fileName, skipped = outcome.SkippedForTopology, errors = outcome.Errors });

                                       if (!DaleConsole.JsonMode)
                                       {
                                           if (outcome.Errors.Count > 0)
                                           {
                                               DaleConsole.Error($"{fileName}:");
                                               foreach (var error in outcome.Errors)
                                               {
                                                   DaleConsole.Info($"    ✗ {error}");
                                               }
                                           }
                                           else
                                           {
                                               DaleConsole.Info(outcome.SkippedForTopology is null ? $"  ✓ {fileName}" :
                                                                    $"  - {fileName} (topology '{outcome.SkippedForTopology}' is not the exported one — paths not checked)");
                                           }
                                       }
                                   }

                                   if (DaleConsole.JsonMode)
                                   {
                                       DaleConsole.WriteJsonResult(new { valid = !failed, files = results });
                                   }
                                   else if (!failed)
                                   {
                                       DaleConsole.Success("Validated", $"{results.Count} scenario file(s)");
                                   }

                                   return failed ? 1 : 0;
                               });

            return validate;
        }

        private static Command CreateSchema()
        {
            var schema = new Command("schema",
                                     "Print the scenario JSON Schema, enriched with this topology's actual name paths when a configuration is available — " +
                                     "commit it as scenarios/.dale/scenario.schema.json and reference it from scenario files via \"$schema\" for editor completion");
            var outputOption = new Option<string?>("--output", "-o")
                               {
                                   Description =
                                       "Write to this file instead of printing (conventionally scenarios/.dale/scenario.schema.json, what the files' \"$schema\" points at).",
                               };
            var configOption = new Option<string?>("--config") { Description = "Configuration export to enrich from (default: the running DevHost)." };
            var portOption = PortOption();
            schema.Options.Add(outputOption);
            schema.Options.Add(configOption);
            schema.Options.Add(portOption);

            schema.SetAction(async (parseResult, cancellationToken) =>
                             {
                                 var port = parseResult.GetValue(portOption);

                                 // The generic structural schema ships embedded in the CLI, so `dale scenario
                                 // schema` runs fully offline — the same offline story as `dale scenario
                                 // validate` (DF-10). Enrichment with the actual block/property name paths is
                                 // best-effort: an explicit --config export or a running host supplies them;
                                 // with neither we emit the un-enriched generic schema (a clear, still-useful
                                 // result, not an error).
                                 var document = JsonNode.Parse(LoadEmbeddedGenericSchema())!;

                                 var configPath = parseResult.GetValue(configOption);
                                 JsonNode? config;
                                 if (configPath is not null)
                                 {
                                     config = await LoadConfigAsync(configPath, port, cancellationToken);
                                     if (config is null)
                                     {
                                         // An explicitly requested --config that doesn't load is an error.
                                         return 1;
                                     }
                                 }
                                 else
                                 {
                                     config = await LoadConfigAsync(null, port, cancellationToken, false);
                                     if (config is null && !DaleConsole.JsonMode)
                                     {
                                         DaleConsole.Info("    Note: emitting the generic schema — pass --config <export> or run `dale dev` for name-path completion.");
                                     }
                                 }

                                 if (config is not null)
                                 {
                                     ScenarioFileChecks.EnrichSchemaWithNamePaths(document, config);
                                 }

                                 var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

                                 // No -o: the schema IS the output (pipe or inspect it) — a schema command
                                 // should show a schema, not write to a magic location.
                                 var output = parseResult.GetValue(outputOption);
                                 if (output is null)
                                 {
                                     Console.WriteLine(json);
                                     return 0;
                                 }

                                 Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                                 File.WriteAllText(output, json);

                                 if (DaleConsole.JsonMode)
                                 {
                                     DaleConsole.WriteJsonResult(new { written = Path.GetFullPath(output), enriched = config is not null });
                                 }
                                 else
                                 {
                                     DaleConsole.Success("Wrote", output);
                                     DaleConsole.Info("    Reference it from scenario files for editor completion: \"$schema\": \"./.dale/scenario.schema.json\"");
                                 }

                                 return 0;
                             });

            return schema;
        }

        private static Command CreateOpen()
        {
            var open = new Command("open", "Open a scenario in the running DevHost's Player (the #/scenario/{id} deep link)");
            var idArgument = new Argument<string>("id") { Description = "Scenario id." };
            var portOption = PortOption();
            open.Arguments.Add(idArgument);
            open.Options.Add(portOption);

            open.SetAction((parseResult, cancellationToken) =>
                           {
                               var url = $"http://localhost:{parseResult.GetValue(portOption)}/#/scenario/{Uri.EscapeDataString(parseResult.GetValue(idArgument)!)}";
                               try
                               {
                                   Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                               }
                               catch (Exception e)
                               {
                                   DaleConsole.Error($"Could not open the browser: {e.Message}. Navigate to {url} manually.");
                                   return Task.FromResult(1);
                               }

                               DaleConsole.Info($"Opened {url}");
                               return Task.FromResult(0);
                           });

            return open;
        }

        private static HttpClient NewClient(int port)
        {
            return new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };
        }

        // Loads the wired-host configuration to resolve name paths against: from a --config export file, or
        // from a running DevHost's /api/configuration. When requireSource is false, an unreachable host is
        // not an error (the caller can proceed without enrichment — see `dale scenario schema`).
        private static async Task<JsonNode?> LoadConfigAsync(string? configPath, int port, CancellationToken cancellationToken, bool requireSource = true)
        {
            if (configPath is not null)
            {
                if (!File.Exists(configPath))
                {
                    DaleConsole.Error($"No configuration export at '{configPath}'. Produce one with `dale dev --export-config {configPath}`.");
                    return null;
                }

                return JsonNode.Parse(File.ReadAllText(configPath));
            }

            using var http = NewClient(port);
            try
            {
                return JsonNode.Parse(await http.GetStringAsync("/api/configuration", cancellationToken));
            }
            catch (HttpRequestException e)
            {
                if (requireSource)
                {
                    DaleConsole.Error($"No configuration source: no --config file given and no DevHost reachable on port {port} ({e.Message}). " +
                                      "Run `dale dev --headless` or export one with `dale dev --export-config <file>`.");
                }

                return null;
            }
        }

        // The generic scenario-file schema, embedded in the CLI assembly (linked from the single source file
        // in Vion.Dale.DevHost — see the .csproj). Reading it here is what lets `dale scenario schema` run
        // without a DevHost (DF-10).
        private static string LoadEmbeddedGenericSchema()
        {
            const string resourceName = "Vion.Dale.Cli.scenario.schema.json";
            var assembly = typeof(ScenarioCommand).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName) ??
                               throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing from the CLI assembly.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void RenderReport(JsonNode report)
        {
            var status = report["status"]?.GetValue<string>();
            DaleConsole.Info($"scenario {report["scenarioId"]} — {status} (run {report["runId"]})");
            foreach (var error in report["validationErrors"] as JsonArray ?? new JsonArray())
            {
                DaleConsole.Info($"  ✗ {error}");
            }

            void Section(string name)
            {
                var steps = report[name] as JsonArray;
                if (steps is null || steps.Count == 0)
                {
                    return;
                }

                foreach (var step in steps)
                {
                    var glyph = step!["status"]?.GetValue<string>() switch
                    {
                        "ok" => "✓",
                        "failed" => "✗",
                        "skipped" => "⊘",
                        _ => "◌",
                    };
                    var argument = step["argument"]?.GetValue<string>() is { } a ? $" {a}" : "";
                    var elapsed = step["elapsedMs"]?.GetValue<double>() is { } ms ? $" ({Math.Round(ms)} ms)" : "";
                    var detail = step["detail"]?.GetValue<string>() is { } d ? $" — {d}" : "";
                    DaleConsole.Info($"  {glyph} {step["kind"]} {step["target"]}{argument}{elapsed}{detail}");
                }
            }

            Section("setup");
            Section("steps");
            foreach (var judgment in report["judge"] as JsonArray ?? new JsonArray())
            {
                DaleConsole.Info($"  ☐ {judgment!["text"]} (requires human{(judgment["spec"] is { } s ? $" — {s}" : "")})");
            }
        }
    }
}