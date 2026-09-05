using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
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
            var versionOption = new Option<string?>("--version") { Description = "Override the package version (drives the produced .nupkg, e.g. from a tag/CI)" };

            command.Options.Add(clientIdOption);
            command.Options.Add(clientSecretOption);
            command.Options.Add(releaseNotesOption);
            command.Options.Add(environmentOption);
            command.Options.Add(integratorIdOption);
            command.Options.Add(skipDuplicateOption);
            command.Options.Add(versionOption);

            command.SetAction(async (parseResult, cancellationToken) =>
                              {
                                  var projectPath = parseResult.GetValue<string?>("--project");

                                  // 1. Find project
                                  var project = CommandHelpers.RequireProject(projectPath);
                                  if (project == null)
                                  {
                                      return 1;
                                  }

                                  var versionOverride = parseResult.GetValue(versionOption);
                                  var requestedVersion = versionOverride ?? project.Version;

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
                                  var packNotices = new List<string>();
                                  string? responseBody = null;
                                  string? nupkgPath = null;
                                  string? effectiveVersion = null;

                                  if (DaleConsole.JsonMode)
                                  {
                                      // JSON mode: no progress bar, just run
                                      var packResult = await DotnetRunner.RunCaptureAsync("pack", BuildPackArgs(project, versionOverride), project.ProjectDirectory);
                                      packNotices = ExtractPackNotices(packResult.Output);
                                      if (packResult.ExitCode != 0)
                                      {
                                          DaleConsole.Error(DescribePackFailure(packResult.Output));
                                          return 1;
                                      }

                                      nupkgPath = FindNupkg(project);
                                      if (nupkgPath == null)
                                      {
                                          DaleConsole.Error("Could not find packed .nupkg file.");
                                          return 1;
                                      }

                                      effectiveVersion = ReadNupkgVersion(nupkgPath) ?? requestedVersion;

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
                                              var conflictBody = await response.Content.ReadAsStringAsync();
                                              if (!IsVersionAlreadyExistsConflict(conflictBody))
                                              {
                                                  DaleConsole.Error($"Upload failed: {DaleHttpClient.DescribeError(conflictBody)}");
                                                  return 1;
                                              }

                                              DaleConsole.WriteJsonResult(new
                                                                          {
                                                                              status = "skipped", reason = "version_exists", packageId = project.PackageId,
                                                                              version = effectiveVersion, notices = packNotices,
                                                                          });
                                              return 0;
                                          }

                                          // A shape this tool owns, so a caller can branch on `status` whether the
                                          // upload happened or was skipped; the endpoint's own answer rides under
                                          // `response`, and the parser's notices — which blocks the artifact left
                                          // out — travel with it as they do in table mode.
                                          DaleConsole.WriteJsonResult(new
                                                                      {
                                                                          status = "uploaded", packageId = project.PackageId, version = effectiveVersion,
                                                                          notices = packNotices,
                                                                          response = ReadResponseDocument(await response.Content.ReadAsStringAsync()),
                                                                      });
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
                                                                   var packTask = progressCtx.AddTask($"Packing {project.ProjectName} v{requestedVersion ?? "??"}", maxValue: 1);
                                                                   var packResult = await DotnetRunner.RunCaptureAsync("pack",
                                                                                                                       BuildPackArgs(project, versionOverride),
                                                                                                                       project.ProjectDirectory);
                                                                   packNotices = ExtractPackNotices(packResult.Output);
                                                                   if (packResult.ExitCode != 0)
                                                                   {
                                                                       errorMessage = DescribePackFailure(packResult.Output);
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
                                                                       if (response.StatusCode == HttpStatusCode.Conflict)
                                                                       {
                                                                           versionAlreadyExists = IsVersionAlreadyExistsConflict(responseBody);
                                                                           if (!versionAlreadyExists)
                                                                           {
                                                                               errorMessage = $"Upload failed: {DaleHttpClient.DescribeError(responseBody)}";
                                                                               failed = true;
                                                                               return;
                                                                           }
                                                                       }
                                                                   }
                                                                   catch (Exception ex)
                                                                   {
                                                                       errorMessage = $"Upload failed: {ex.Message}";
                                                                       failed = true;
                                                                       return;
                                                                   }

                                                                   uploadTask.Value = 1;
                                                               });

                                  // The pack output is captured, not inherited, so the parser's notices — which
                                  // blocks were left out of the artifact the cloud reads — would otherwise vanish.
                                  foreach (var notice in packNotices)
                                  {
                                      DaleConsole.Info(notice);
                                  }

                                  if (failed)
                                  {
                                      DaleConsole.Error(errorMessage!);
                                      return 1;
                                  }

                                  effectiveVersion = (nupkgPath != null ? ReadNupkgVersion(nupkgPath) : null) ?? requestedVersion;

                                  if (versionAlreadyExists)
                                  {
                                      DaleConsole.Info($"{project.ProjectName} v{effectiveVersion ?? "??"} already exists, skipping.");
                                      return 0;
                                  }

                                  DaleConsole.Success("Uploaded", $"{project.ProjectName} v{effectiveVersion ?? "??"}");
                                  return 0;
                              });

            return command;
        }

        /// <summary>
        ///     The package this project produced: the most recently written <c>.nupkg</c> whose file name is
        ///     the project's package id followed by a version. A prefix match would claim a sibling's
        ///     artifact — <c>Acme.Energy.Modbus.1.4.0.nupkg</c> begins with <c>Acme.Energy.</c> — and package
        ///     ids are globally unique (decision 0111), so the wrong artifact would land under a real
        ///     identity. Returns null rather than guessing when nothing matches.
        /// </summary>
        internal static string? FindNupkg(DaleProject project)
        {
            if (string.IsNullOrEmpty(project.PackageId))
            {
                return null;
            }

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

                var matching = Directory.GetFiles(dir, "*.nupkg", SearchOption.AllDirectories)
                                        .Where(file => IsPackageOf(project.PackageId!, file))
                                        .OrderByDescending(File.GetLastWriteTime)
                                        .FirstOrDefault();
                if (matching != null)
                {
                    return matching;
                }
            }

            return null;
        }

        /// <summary>
        ///     Whether a <c>.nupkg</c> file name is <paramref name="packageId" /> followed by a version —
        ///     the package id, a dot, and a remainder that starts with a digit. That last condition is what
        ///     separates a package from a longer-named sibling.
        /// </summary>
        internal static bool IsPackageOf(string packageId, string nupkgPath)
        {
            var fileName = Path.GetFileNameWithoutExtension(nupkgPath);
            if (!fileName.StartsWith(packageId + ".", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = fileName.Substring(packageId.Length + 1);
            return remainder.Length > 0 && char.IsDigit(remainder[0]);
        }

        /// <summary>
        ///     Tells the upload endpoint's two 409s apart. Both are <c>ConflictException</c> server-side, so
        ///     only the message distinguishes "this exact version was already uploaded" — the one
        ///     <c>--skip-duplicate</c> is for — from "this package id belongs to another integrator", which
        ///     is a hard failure: package ids are globally unique across the platform, so the fix is to
        ///     rename the package, not to retry. Anything unrecognised counts as the latter; reporting a
        ///     conflict we don't understand as a successful skip is the one outcome that hides a failed
        ///     publish (CI uploads with <c>--skip-duplicate</c>).
        /// </summary>
        internal static bool IsVersionAlreadyExistsConflict(string? body)
        {
            var message = DaleHttpClient.DescribeError(body);
            return message.Contains("version", StringComparison.OrdinalIgnoreCase) && message.Contains("already exists", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     What to say when <c>dotnet pack</c> failed. The output is captured rather than inherited (the
        ///     progress display and the JSON document both need the console to themselves), so a bare "Pack
        ///     failed." would throw away the only diagnosis there is.
        /// </summary>
        internal static string DescribePackFailure(string packOutput)
        {
            var detail = packOutput.Split('\n')
                                   .Select(line => line.Trim('\r', ' '))
                                   .Where(line => line.Contains(": error ", StringComparison.Ordinal))
                                   .Distinct(StringComparer.Ordinal)
                                   .ToList();

            return detail.Count > 0 ? "Pack failed:" + Environment.NewLine + string.Join(Environment.NewLine, detail) : "Pack failed.";
        }

        /// <summary>
        ///     The endpoint's answer as a JSON value where it is JSON, and as a string otherwise, so the
        ///     tool's own document nests it rather than quoting a document inside a document.
        /// </summary>
        internal static JsonNode? ReadResponseDocument(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(body!);
            }
            catch (JsonException)
            {
                return JsonValue.Create(body);
            }
        }

        /// <summary>
        ///     Build the <c>dotnet pack</c> arguments, optionally injecting an explicit package version
        ///     (<c>-p:Version=</c>) so the produced .nupkg version can be driven by a tag / CI instead of the csproj.
        /// </summary>
        internal static string[] BuildPackArgs(DaleProject project, string? version)
        {
            var args = new[] { project.CsprojPath, "-c", "Release", "-p:IsPackable=true" };
            return string.IsNullOrWhiteSpace(version) ? args : args.Append($"-p:Version={version}").ToArray();
        }

        /// <summary>
        ///     The notice lines <c>Vion.Dale.LogicBlockParser</c> writes during pack — today, which logic blocks
        ///     were left out of the introspection JSON for being development-only. Identified by the parser's
        ///     stable prefix; the prefix is stripped for display.
        /// </summary>
        internal static List<string> ExtractPackNotices(string packOutput)
        {
            const string noticePrefix = "Vion Dale: ";

            return packOutput.Split('\n')
                             .Select(line => line.Trim('\r', ' '))
                             .Where(line => line.StartsWith(noticePrefix, StringComparison.Ordinal))
                             .Select(line => line.Substring(noticePrefix.Length))
                             .ToList();
        }

        /// <summary>
        ///     Read the effective package version back from a produced .nupkg by reading its bundled .nuspec.
        ///     This is authoritative — it reflects what was actually packed, not the (possibly stale) csproj value.
        ///     Returns null if the file is missing, not a valid package, or carries no version.
        /// </summary>
        internal static string? ReadNupkgVersion(string nupkgPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(nupkgPath);
                var nuspec = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                if (nuspec == null)
                {
                    return null;
                }

                using var stream = nuspec.Open();
                var doc = XDocument.Load(stream);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                return doc.Descendants(ns + "version").FirstOrDefault()?.Value;
            }
            catch
            {
                return null;
            }
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