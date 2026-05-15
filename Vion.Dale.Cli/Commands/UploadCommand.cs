using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Spectre.Console;
using Vion.Dale.Cli.Auth;
using Vion.Dale.Cli.Helpers;
using Vion.Dale.Cli.Infrastructure;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Commands
{
    public static class UploadCommand
    {
        public static Command Create()
        {
            var command = new Command("upload", "Pack and upload library to Vion Cloud");

            var clientIdOption = new Option<string?>("--client-id") { Description = "Keycloak client ID (for CI/non-interactive auth)" };
            var clientSecretOption = new Option<string?>("--client-secret") { Description = "Keycloak client secret (for CI/non-interactive auth)" };
            var releaseNotesOption = new Option<string?>("--release-notes") { Description = "Release notes for this version" };
            var environmentOption = new Option<string?>("--environment", "-e") { Description = "Target environment (overrides stored config)" };
            var integratorIdOption = new Option<Guid?>("--integrator-id") { Description = "Integrator ID (overrides stored config)" };
            var skipDuplicateOption = new Option<bool>("--skip-duplicate") { Description = "Treat 409 Conflict (version already exists) as success" };

            command.Options.Add(clientIdOption);
            command.Options.Add(clientSecretOption);
            command.Options.Add(releaseNotesOption);
            command.Options.Add(environmentOption);
            command.Options.Add(integratorIdOption);
            command.Options.Add(skipDuplicateOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  // 1. Find project
                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  // 2. Resolve cloud context
                                  CommandContext ctx;
                                  try
                                  {
                                      ctx = await CommandContext.ResolveAsync(parseResult.GetValue(environmentOption),
                                                                              parseResult.GetValue(integratorIdOption),
                                                                              parseResult.GetValue(clientIdOption),
                                                                              parseResult.GetValue(clientSecretOption));
                                  }
                                  catch (DaleAuthException ex)
                                  {
                                      DaleConsole.Error(ex.Message);
                                      return 1;
                                  }

                                  // 3. Pack + Upload with progress
                                  var skipDuplicate = parseResult.GetValue(skipDuplicateOption);
                                  string? responseBody = null;
                                  string? nupkgPath = null;

                                  if (DaleConsole.JsonMode)
                                  {
                                      // JSON mode: no progress bar, just run
                                      var packResult = await DotnetRunner.RunCaptureAsync("pack",
                                                                                          new[] { project.CsprojPath, "-c", "Release", "-p:IsPackable=true" },
                                                                                          project.ProjectDirectory);
                                      if (packResult.ExitCode != 0)
                                      {
                                          DaleConsole.Error("Pack failed.");
                                          return 1;
                                      }

                                      nupkgPath = FindNupkg(project);
                                      if (nupkgPath == null)
                                      {
                                          DaleConsole.Error("Could not find packed .nupkg file.");
                                          return 1;
                                      }

                                      try
                                      {
                                          var response = await UploadNupkg(ctx.AccessToken,
                                                                           ctx.ApiBaseUrl,
                                                                           ctx.IntegratorId,
                                                                           nupkgPath,
                                                                           parseResult.GetValue(releaseNotesOption),
                                                                           skipDuplicate);
                                          if (response.StatusCode == HttpStatusCode.Conflict)
                                          {
                                              DaleConsole.WriteJsonResult(new
                                                                          {
                                                                              status = "skipped", reason = "version_exists", packageId = project.PackageId,
                                                                              version = project.Version,
                                                                          });
                                              return 0;
                                          }

                                          DaleConsole.WriteJson(await response.Content.ReadAsStringAsync() ?? "{}");
                                      }
                                      catch (Exception ex)
                                      {
                                          DaleConsole.Error($"Upload failed: {ex.Message}");
                                          return 1;
                                      }

                                      return 0;
                                  }

                                  // Human mode: progress bar with stages
                                  var failed = false;
                                  var versionAlreadyExists = false;
                                  string? errorMessage = null;

                                  await AnsiConsole.Progress()
                                                   .AutoClear(true)
                                                   .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new SpinnerColumn())
                                                   .StartAsync(async progressCtx =>
                                                               {
                                                                   // Stage 1: Pack
                                                                   var packTask = progressCtx.AddTask($"Packing {project.ProjectName} v{project.Version ?? "??"}", maxValue: 1);
                                                                   var packResult = await DotnetRunner.RunCaptureAsync("pack",
                                                                                                                       new[]
                                                                                                                       {
                                                                                                                           project.CsprojPath, "-c", "Release",
                                                                                                                           "-p:IsPackable=true",
                                                                                                                       },
                                                                                                                       project.ProjectDirectory);
                                                                   if (packResult.ExitCode != 0)
                                                                   {
                                                                       errorMessage = "Pack failed.";
                                                                       failed = true;
                                                                       return;
                                                                   }

                                                                   nupkgPath = FindNupkg(project);
                                                                   if (nupkgPath == null)
                                                                   {
                                                                       errorMessage = "Could not find packed .nupkg file.";
                                                                       failed = true;
                                                                       return;
                                                                   }

                                                                   packTask.Value = 1;

                                                                   // Stage 2: Upload
                                                                   var uploadTask = progressCtx.AddTask("Uploading to cloud", maxValue: 1);
                                                                   try
                                                                   {
                                                                       var response = await UploadNupkg(ctx.AccessToken,
                                                                                                        ctx.ApiBaseUrl,
                                                                                                        ctx.IntegratorId,
                                                                                                        nupkgPath,
                                                                                                        parseResult.GetValue(releaseNotesOption),
                                                                                                        skipDuplicate);
                                                                       responseBody = await response.Content.ReadAsStringAsync();
                                                                       versionAlreadyExists = response.StatusCode == HttpStatusCode.Conflict;
                                                                   }
                                                                   catch (Exception ex)
                                                                   {
                                                                       errorMessage = $"Upload failed: {ex.Message}";
                                                                       failed = true;
                                                                       return;
                                                                   }

                                                                   uploadTask.Value = 1;
                                                               });

                                  if (failed)
                                  {
                                      DaleConsole.Error(errorMessage!);
                                      return 1;
                                  }

                                  if (versionAlreadyExists)
                                  {
                                      DaleConsole.Info($"{project.ProjectName} v{project.Version ?? "??"} already exists, skipping.");
                                      return 0;
                                  }

                                  DaleConsole.Success("Uploaded", $"{project.ProjectName} v{project.Version ?? "??"}");
                                  return 0;
                              });

            return command;
        }

        internal static string? FindNupkg(DaleProject project)
        {
            var searchDirs = new[]
                             {
                                 Path.Combine(project.ProjectDirectory, "bin", "Release"),
                                 project.ProjectDirectory,
                             };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var allNupkgs = Directory.GetFiles(dir, "*.nupkg", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime).ToArray();

                // Prefer exact match by PackageId
                if (!string.IsNullOrEmpty(project.PackageId))
                {
                    var packagePrefix = project.PackageId + ".";
                    var matching = allNupkgs.FirstOrDefault(f => Path.GetFileName(f).StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase));
                    if (matching != null)
                    {
                        return matching;
                    }
                }

                // Fall back to newest
                if (allNupkgs.Length > 0)
                {
                    return allNupkgs[0];
                }
            }

            return null;
        }

        /// <summary>
        ///     Uploads the .nupkg to the cloud API. When skipDuplicate is true, 409 Conflict is returned as a response instead of
        ///     throwing.
        /// </summary>
        private static async Task<HttpResponseMessage> UploadNupkg(string accessToken,
                                                                   string apiBaseUrl,
                                                                   Guid integratorId,
                                                                   string nupkgPath,
                                                                   string? releaseNotes,
                                                                   bool skipDuplicate)
        {
            var uploadUrl = $"{apiBaseUrl}/Integrator/{integratorId}/LogicBlockLibraryVersions";

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(nupkgPath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "nugetPackageFile", Path.GetFileName(nupkgPath));

            if (releaseNotes != null)
            {
                form.Add(new StringContent(releaseNotes), "releaseNotes");
            }

            var allowed = skipDuplicate ? new[] { HttpStatusCode.Conflict } : Array.Empty<HttpStatusCode>();
            return await DaleHttpClient.PostAsync(uploadUrl, form, accessToken, default, allowed);
        }
    }
}
