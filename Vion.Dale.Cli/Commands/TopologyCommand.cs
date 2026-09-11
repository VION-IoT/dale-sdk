using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    /// <summary>
    ///     The topology verbs. Both run with nothing on the port: <c>validate</c> judges a
    ///     <c>*.topology.json</c> against <see cref="TopologyFileChecks" />, and <c>schema</c> prints the
    ///     generic schema the CLI carries embedded. There is no host-enrichment half here, unlike
    ///     <c>dale scenario schema</c> — the topology schema is generic by construction, and the checks a
    ///     running host adds (an instance's type resolving, a contract mapping naming a real binding) need
    ///     the loaded catalog rather than a richer document.
    /// </summary>
    public static class TopologyCommand
    {
        public static Command Create()
        {
            var command = new Command("topology", "Work with *.topology.json files: validate them and generate the editor schema — both with no DevHost running");
            command.Subcommands.Add(CreateValidate());
            command.Subcommands.Add(CreateSchema());
            return command;
        }

        private static Command CreateValidate()
        {
            var validate = new Command("validate",
                                       "Validate every topology file against the rules the host's loader applies — structure, ids, instance names, mappings and pairings — with no host running");
            var dirOption = new Option<string>("--dir") { Description = "Topologies directory (default ./topologies).", DefaultValueFactory = _ => "topologies" };
            var requireSchemaOption = new Option<bool>("--require-schema-ref")
                                      {
                                          Description =
                                              "Fail a file that carries no \"$schema\" reference, instead of warning about it. The loader treats the reference as optional.",
                                      };
            validate.Options.Add(dirOption);
            validate.Options.Add(requireSchemaOption);

            validate.SetAction(parseResult =>
                               {
                                   var dir = parseResult.GetValue(dirOption)!;
                                   if (!Directory.Exists(dir))
                                   {
                                       DaleConsole.Error($"No topologies directory at '{Path.GetFullPath(dir)}'.");
                                       return 1;
                                   }

                                   var requireSchemaRef = parseResult.GetValue(requireSchemaOption);
                                   var results = new List<object>();
                                   var failed = false;
                                   foreach (var path in Directory.EnumerateFiles(dir, $"*{TopologyFileChecks.FileSuffix}").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                                   {
                                       var fileName = Path.GetFileName(path);
                                       var outcome = TopologyFileChecks.Validate(fileName, File.ReadAllText(path), requireSchemaRef);
                                       failed |= outcome.Errors.Count > 0;
                                       results.Add(new { file = fileName, errors = outcome.Errors, warnings = outcome.Warnings });

                                       if (DaleConsole.JsonMode)
                                       {
                                           continue;
                                       }

                                       if (outcome.Errors.Count > 0)
                                       {
                                           DaleConsole.Error($"{fileName}:");
                                       }
                                       else if (outcome.Warnings.Count == 0)
                                       {
                                           DaleConsole.Info($"  ✓ {fileName}");
                                       }
                                       else
                                       {
                                           DaleConsole.Info($"  ! {fileName}");
                                       }

                                       foreach (var error in outcome.Errors)
                                       {
                                           DaleConsole.Info($"    ✗ {error}");
                                       }

                                       foreach (var warning in outcome.Warnings)
                                       {
                                           DaleConsole.Info($"    ! {warning}");
                                       }
                                   }

                                   if (results.Count == 0)
                                   {
                                       // A validator asked to validate nothing has been pointed at the wrong place — a
                                       // renamed directory, or a suffix typo. The gate this verb exists to be has no
                                       // value at all if an empty walk is a pass.
                                       DaleConsole.Error($"No *{TopologyFileChecks.FileSuffix} files in '{Path.GetFullPath(dir)}'.");
                                       return 1;
                                   }

                                   if (DaleConsole.JsonMode)
                                   {
                                       DaleConsole.WriteJsonResult(new { valid = !failed, files = results });
                                   }
                                   else if (!failed)
                                   {
                                       DaleConsole.Success("Validated", $"{results.Count} topology file(s)");
                                   }

                                   return failed ? 1 : 0;
                               });

            return validate;
        }

        private static Command CreateSchema()
        {
            var schema = new Command("schema",
                                     "Print the generic topology JSON Schema — commit it as topologies/.dale/topology.schema.json and reference it from topology files via \"$schema\" for editor completion");
            var outputOption = new Option<string?>("--out", "-O", "-o")
                               {
                                   Description =
                                       "Write to this file instead of printing (conventionally topologies/.dale/topology.schema.json, what the files' \"$schema\" points at). `-o` is a deprecated alias for `--out`; it is the global output-format option everywhere else.",
                               };
            schema.Options.Add(outputOption);

            schema.SetAction(parseResult =>
                             {
                                 // The schema ships embedded in the CLI, so this verb needs no DevHost at all —
                                 // the offline guarantee `dale scenario schema` already carries. Unlike that one
                                 // there is nothing to enrich: the topology schema is generic by construction, so
                                 // the canonical file's own text is emitted byte for byte rather than re-serialized
                                 // from a JsonNode. A round-trip reflows the source's one-line arrays and drops its
                                 // final newline, which turns a consumer's regeneration into a whole-file diff
                                 // instead of the hunks their copy has actually drifted by.
                                 var json = LoadEmbeddedGenericSchema();

                                 // No --out: the schema IS the output (pipe or inspect it) — a schema command
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
                                     DaleConsole.WriteJsonResult(new { written = Path.GetFullPath(output) });
                                 }
                                 else
                                 {
                                     DaleConsole.Success("Wrote", output);
                                     DaleConsole.Info($"    Reference it from topology files for editor completion: \"$schema\": \"{TopologyFileChecks.DevTopologySchemaRef}\"");
                                 }

                                 return 0;
                             });

            return schema;
        }

        // The generic topology schema, embedded in the CLI assembly (linked from the single source file in
        // Vion.Dale.DevHost — see the .csproj). Reading it here is what lets `dale topology schema` run
        // without a DevHost, and what keeps the emitted document from drifting from the host's.
        private static string LoadEmbeddedGenericSchema()
        {
            const string resourceName = "Vion.Dale.Cli.topology.schema.json";
            var assembly = typeof(TopologyCommand).Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName) ??
                               throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing from the CLI assembly.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}