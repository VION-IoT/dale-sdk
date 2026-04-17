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

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  DaleConsole.Info($"Packing {project.ProjectName} v{project.Version ?? "??"}...");

                                  var result = await DotnetRunner.RunAsync("pack", new[] { project.CsprojPath, "-c", "Release", "-p:IsPackable=true" }, project.ProjectDirectory);
                                  if (result != 0)
                                  {
                                      DaleConsole.Error("Pack failed.");
                                      return result;
                                  }

                                  // Find and report the nupkg path
                                  var nupkgPath = UploadCommand.FindNupkg(project);

                                  if (DaleConsole.JsonMode)
                                  {
                                      DaleConsole.WriteJsonResult(new { packageId = project.PackageId, version = project.Version, nupkg = nupkgPath });
                                  }
                                  else
                                  {
                                      DaleConsole.Success("Packed", $"{project.ProjectName} v{project.Version ?? "??"}");
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