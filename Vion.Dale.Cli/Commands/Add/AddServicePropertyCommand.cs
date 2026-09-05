using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Add
{
    public static class AddServicePropertyCommand
    {
        public static Command Create()
        {
            var command = new Command("serviceproperty", "Add a [ServiceProperty] to a LogicBlock");

            var nameArg = new Argument<string>("name") { Description = "Property name" };
            command.Arguments.Add(nameArg);

            var typeOption = new Option<string>("--type", "-t") { Description = "C# type of the property (e.g. double, string, bool)", Required = true };
            var toOption = new Option<string?>("--to") { Description = "Target LogicBlock class name (auto-detected if only one exists)" };
            var setterOption = new Option<string>("--setter") { Description = "Setter visibility", DefaultValueFactory = _ => "private" };
            setterOption.AcceptOnlyFromAmong("public", "private");
            var defaultNameOption = new Option<string?>("--default-name") { Description = "Title for [ServiceProperty] (defaults to the property name)" };
            var persistentOption = new Option<bool>("--persistent") { Description = "Add [Persistent] attribute" };
            var groupOption = new Option<string?>("--group")
                              { Description = "[Presentation] group: a PropertyGroup name (Status, Configuration, Metric, Diagnostics, Identity, Alarm) or an arbitrary raw key" };
            var importanceOption = new Option<string?>("--importance") { Description = "[Presentation] importance: Primary, Secondary, Normal, or Hidden" };
            importanceOption.AcceptOnlyFromAmong(PresentationSnippet.Importances);
            var decimalsOption = new Option<int?>("--decimals") { Description = "[Presentation] numeric display precision" };
            var formatOption = new Option<string?>("--format") { Description = "[Presentation] date/duration/numeric format-token string" };

            command.Options.Add(typeOption);
            command.Options.Add(toOption);
            command.Options.Add(setterOption);
            command.Options.Add(defaultNameOption);
            command.Options.Add(persistentOption);
            command.Options.Add(groupOption);
            command.Options.Add(importanceOption);
            command.Options.Add(decimalsOption);
            command.Options.Add(formatOption);

            command.SetAction(parseResult =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var type = parseResult.GetValue(typeOption);
                                  var to = parseResult.GetValue(toOption);
                                  var setter = parseResult.GetValue(setterOption);
                                  var defaultName = parseResult.GetValue(defaultNameOption);
                                  var persistent = parseResult.GetValue(persistentOption);
                                  var group = parseResult.GetValue(groupOption);
                                  var importance = parseResult.GetValue(importanceOption);
                                  var decimals = parseResult.GetValue(decimalsOption);
                                  var format = parseResult.GetValue(formatOption);
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  if (!CSharpNames.IsIdentifier(name))
                                  {
                                      DaleConsole.Error(CSharpNames.DescribeInvalidIdentifier("property name", name));
                                      return 1;
                                  }

                                  if (!CSharpNames.IsTypeReference(type))
                                  {
                                      DaleConsole.Error(CSharpNames.DescribeInvalidTypeReference(type));
                                      return 1;
                                  }

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  var target = CommandHelpers.RequireTarget(project, to);
                                  if (target == null)
                                  {
                                      return 1;
                                  }

                                  // A member of the same name may be the sibling annotation's half of a
                                  // dual-annotated member — one property carrying both [ServiceProperty] and
                                  // [ServiceMeasuringPoint], which `emission.md` specifies and the repository
                                  // CLAUDE.md calls common for telemetry. Adding the second annotation to it is
                                  // the answer; every other collision is still refused.
                                  var sourceContent = File.ReadAllText(target.FilePath);
                                  var existing = SourceInserter.FindMember(sourceContent, name!);
                                  if (existing is { } member)
                                  {
                                      if (member.CarriesAttribute("ServiceProperty"))
                                      {
                                          DaleConsole.Error($"property '{name}' already exists in {target.ClassName}.");
                                          return 1;
                                      }

                                      if (!member.IsPropertyOfType(type!))
                                      {
                                          DaleConsole.Error($"'{name}' already exists in {target.ClassName} and is not a {type} property, so it cannot also be a property.");
                                          return 1;
                                      }

                                      // The same attribute lines the create path writes, minus the ones the member
                                      // already carries: [Presentation] and [Persistent] are single-use, so emitting
                                      // a second copy onto the sibling annotation's member produces source that does
                                      // not compile.
                                      var annotations = BuildAttributeLines(name!,
                                                                            defaultName,
                                                                            persistent,
                                                                            group,
                                                                            importance,
                                                                            decimals,
                                                                            format)
                                                        .Where(line => !member.CarriesAttribute(SourceInserter.AttributeNameOf(line)))
                                                        .ToList();

                                      if (!SourceInserter.AddAttributesToMember(target.FilePath, name!, annotations))
                                      {
                                          DaleConsole.Error($"Failed to annotate '{name}' in {target.ClassName}.");
                                          return 1;
                                      }

                                      if (DaleConsole.JsonMode)
                                      {
                                          DaleConsole.WriteJsonResult(new
                                                                      {
                                                                          file = target.FilePath,
                                                                          property = name,
                                                                          type,
                                                                          logicBlock = target.ClassName,
                                                                          annotated = "ServiceProperty",
                                                                      });
                                      }
                                      else
                                      {
                                          DaleConsole.Success("Annotated", $"{name} in {target.ClassName} as a property as well");
                                      }

                                      return 0;
                                  }

                                  // Build the snippet
                                  var snippet = BuildPropertySnippet(name!,
                                                                     type!,
                                                                     setter!,
                                                                     defaultName,
                                                                     persistent,
                                                                     group,
                                                                     importance,
                                                                     decimals,
                                                                     format);

                                  // Insert into class
                                  if (!SourceInserter.InsertIntoClass(target.FilePath, target.ClassName, snippet))
                                  {
                                      DaleConsole.Error($"Failed to insert property into {target.ClassName}.");
                                      return 1;
                                  }

                                  // Ensure using statements
                                  SourceInserter.EnsureUsing(target.FilePath, "Vion.Dale.Sdk.Core");

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { file = target.FilePath, property = name, type, logicBlock = target.ClassName });
                                  }
                                  else
                                  {
                                      var setterDesc = setter == "public" ? "public" : "private";
                                      DaleConsole.Success("Added", $"[ServiceProperty] {type} {name} ({setterDesc} set) to {target.ClassName}");
                                  }

                                  return 0;
                              });

            return command;
        }

        internal static string BuildPropertySnippet(string name,
                                                    string type,
                                                    string setter,
                                                    string? defaultName,
                                                    bool persistent,
                                                    string? group,
                                                    string? importance,
                                                    int? decimals,
                                                    string? format)
        {
            var lines = BuildAttributeLines(name,
                                            defaultName,
                                            persistent,
                                            group,
                                            importance,
                                            decimals,
                                            format);

            // Property declaration
            var setterSuffix = setter == "public" ? "set;" : "private set;";
            lines.Add($"public {type} {name} {{ get; {setterSuffix} }}");

            return string.Join("\n", lines);
        }

        /// <summary>
        ///     The attribute lines above a service property, in the order they are written. One builder with
        ///     two callers: the create path puts the declaration under them, the annotate path inserts them
        ///     above a member that is already declared — so an option given on either path reaches the source
        ///     the same way.
        /// </summary>
        internal static List<string> BuildAttributeLines(string name,
                                                         string? defaultName,
                                                         bool persistent,
                                                         string? group,
                                                         string? importance,
                                                         int? decimals,
                                                         string? format)
        {
            var displayName = defaultName ?? name;
            var lines = new List<string> { $"[ServiceProperty(Title = \"{PresentationSnippet.EscapeCsString(displayName)}\")]" };

            // [Presentation(...)] attribute, only when ≥1 presentation flag was supplied.
            var presentation = PresentationSnippet.Build(group, importance, decimals, format);
            if (presentation != null)
            {
                lines.Add(presentation);
            }

            // [Persistent] attribute if requested
            if (persistent)
            {
                lines.Add("[Persistent]");
            }

            return lines;
        }
    }
}