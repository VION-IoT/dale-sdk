using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text.RegularExpressions;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands.Add
{
    public static class AddMeasuringPointCommand
    {
        public static Command Create()
        {
            var command = new Command("measuringpoint", "Add a [ServiceMeasuringPoint] to a LogicBlock");

            var nameArg = new Argument<string>("name") { Description = "Measuring point name" };
            command.Arguments.Add(nameArg);

            var typeOption = new Option<string>("--type", "-t") { Description = "C# type (e.g. double, int, bool)", Required = true };
            var toOption = new Option<string?>("--to") { Description = "Target LogicBlock class name (auto-detected if only one exists)" };
            var defaultNameOption = new Option<string?>("--default-name") { Description = "Title for [ServiceMeasuringPoint] (defaults to the measuring point name)" };
            var persistentOption = new Option<bool>("--persistent") { Description = "Add [Persistent] attribute (measuring points are not persistent by default)" };
            var kindOption = new Option<string?>("--kind") { Description = "MeasuringPointKind on [ServiceMeasuringPoint]: Measurement, Total, or TotalIncreasing" };
            kindOption.AcceptOnlyFromAmong("Measurement", "Total", "TotalIncreasing");
            var groupOption = new Option<string?>("--group")
                              { Description = "[Presentation] group: a PropertyGroup name (Status, Configuration, Metric, Diagnostics, Identity, Alarm) or an arbitrary raw key" };
            var importanceOption = new Option<string?>("--importance") { Description = "[Presentation] importance: Primary, Secondary, Normal, or Hidden" };
            importanceOption.AcceptOnlyFromAmong(PresentationSnippet.Importances);
            var decimalsOption = new Option<int?>("--decimals") { Description = "[Presentation] numeric display precision" };
            var formatOption = new Option<string?>("--format") { Description = "[Presentation] date/duration/numeric format-token string" };

            command.Options.Add(typeOption);
            command.Options.Add(toOption);
            command.Options.Add(defaultNameOption);
            command.Options.Add(persistentOption);
            command.Options.Add(kindOption);
            command.Options.Add(groupOption);
            command.Options.Add(importanceOption);
            command.Options.Add(decimalsOption);
            command.Options.Add(formatOption);

            command.SetAction(parseResult =>
                              {
                                  var name = parseResult.GetValue(nameArg);
                                  var type = parseResult.GetValue(typeOption);
                                  var to = parseResult.GetValue(toOption);
                                  var defaultName = parseResult.GetValue(defaultNameOption);
                                  var persistent = parseResult.GetValue(persistentOption);
                                  var kind = parseResult.GetValue(kindOption);
                                  var group = parseResult.GetValue(groupOption);
                                  var importance = parseResult.GetValue(importanceOption);
                                  var decimals = parseResult.GetValue(decimalsOption);
                                  var format = parseResult.GetValue(formatOption);
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  if (!CSharpNames.IsIdentifier(name))
                                  {
                                      DaleConsole.Error(CSharpNames.DescribeInvalidIdentifier("measuring point name", name));
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
                                      if (member.CarriesAttribute("ServiceMeasuringPoint"))
                                      {
                                          DaleConsole.Error($"measuring point '{name}' already exists in {target.ClassName}.");
                                          return 1;
                                      }

                                      if (!member.IsPropertyOfType(type!))
                                      {
                                          DaleConsole.Error($"'{name}' already exists in {target.ClassName} and is not a {type} property, so it cannot also be a measuring point.");
                                          return 1;
                                      }

                                      if (!SourceInserter.AddAttributeToMember(target.FilePath, name!, MeasuringPointAttribute(defaultName ?? name!, kind)))
                                      {
                                          DaleConsole.Error($"Failed to annotate '{name}' in {target.ClassName}.");
                                          return 1;
                                      }

                                      DaleConsole.Success("Annotated", $"{name} in {target.ClassName} as a measuring point as well");
                                      return 0;
                                  }

                                  var snippet = BuildMeasuringPointSnippet(name!,
                                                                           type!,
                                                                           defaultName,
                                                                           persistent,
                                                                           kind,
                                                                           group,
                                                                           importance,
                                                                           decimals,
                                                                           format);

                                  if (!SourceInserter.InsertIntoClass(target.FilePath, target.ClassName, snippet))
                                  {
                                      DaleConsole.Error($"Failed to insert measuring point into {target.ClassName}.");
                                      return 1;
                                  }

                                  SourceInserter.EnsureUsing(target.FilePath, "Vion.Dale.Sdk.Core");

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { file = target.FilePath, measuringPoint = name, type, logicBlock = target.ClassName });
                                  }
                                  else
                                  {
                                      DaleConsole.Success("Added", $"[ServiceMeasuringPoint] {type} {name} to {target.ClassName}");
                                  }

                                  return 0;
                              });

            return command;
        }

        /// <summary>The <c>[ServiceMeasuringPoint(...)]</c> line, shared by the snippet and the annotate path.</summary>
        internal static string MeasuringPointAttribute(string displayName, string? kind)
        {
            var arguments = $"Title = \"{PresentationSnippet.EscapeCsString(displayName)}\"";
            if (kind != null)
            {
                arguments += $", Kind = MeasuringPointKind.{kind}";
            }

            return $"[ServiceMeasuringPoint({arguments})]";
        }

        internal static string BuildMeasuringPointSnippet(string name,
                                                          string type,
                                                          string? defaultName,
                                                          bool persistent,
                                                          string? kind,
                                                          string? group,
                                                          string? importance,
                                                          int? decimals,
                                                          string? format)
        {
            var lines = new List<string>();

            var displayName = defaultName ?? name;

            // --kind is emitted as a property INSIDE [ServiceMeasuringPoint(...)], not a separate attribute.
            lines.Add(MeasuringPointAttribute(displayName, kind));

            // [Presentation(...)] attribute, only when ≥1 presentation flag was supplied.
            var presentation = PresentationSnippet.Build(group, importance, decimals, format);
            if (presentation != null)
            {
                lines.Add(presentation);
            }

            if (persistent)
            {
                lines.Add("[Persistent]");
            }

            // Measuring points always have private set
            lines.Add($"public {type} {name} {{ get; private set; }}");

            return string.Join("\n", lines);
        }
    }
}