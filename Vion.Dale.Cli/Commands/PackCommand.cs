using System.CommandLine;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class PackCommand
    {
        public static Command Create()
        {
            var command = new Command("pack", "Pack project into a NuGet package");

            var versionOption = new Option<string?>("--version") { Description = "Override the package version (drives the produced .nupkg, e.g. from a tag/CI)" };
            command.Options.Add(versionOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  var versionOverride = parseResult.GetValue(versionOption);
                                  var requestedVersion = versionOverride ?? project.Version;

                                  DaleConsole.Info($"Packing {project.ProjectName} v{requestedVersion ?? "??"}...");

                                  var result = await DotnetRunner.RunAsync("pack", UploadCommand.BuildPackArgs(project, versionOverride), project.ProjectDirectory);
                                  if (result != 0)
                                  {
                                      DaleConsole.Error("Pack failed.");
                                      return result;
                                  }

                                  // Find the nupkg and read the effective version back from it (authoritative, not the csproj value)
                                  var nupkgPath = UploadCommand.FindNupkg(project);
                                  var effectiveVersion = (nupkgPath != null ? UploadCommand.ReadNupkgVersion(nupkgPath) : null) ?? requestedVersion;

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { packageId = project.PackageId, version = effectiveVersion, nupkg = nupkgPath });
                                  }
                                  else
                                  {
                                      DaleConsole.Success("Packed", $"{project.ProjectName} v{effectiveVersion ?? "??"}");
                                      if (nupkgPath != null)
                                      {
                                          DaleConsole.Info($"  {nupkgPath}");
                                      }
                                  }

                                  return 0;
                              });

            return command;
        }
    }
}