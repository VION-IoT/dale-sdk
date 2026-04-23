using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vion.Dale.Cli.Infrastructure;

namespace Vion.Dale.Cli.Auth
{
    public class StoredCredentials
    {
        public string AccessToken { get; set; } = string.Empty;

        public string? RefreshToken { get; set; }

        public DateTime ExpiresAt { get; set; }

        public string Environment { get; set; } = "production";

        [JsonIgnore]
        public bool IsExpired
        {
            get => DateTime.UtcNow >= ExpiresAt.AddSeconds(-30); // 30s buffer
        }
    }

    public class DaleConfig
    {
        public string Environment { get; set; } = "production";

        public string AuthBaseUrl { get; set; } = string.Empty;

        public string ApiBaseUrl { get; set; } = string.Empty;

        public Guid? IntegratorId { get; set; }

        public string? IntegratorName { get; set; }
    }

    public static class TokenStore
    {
        private static string DaleDir
        {
            get => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dale");
        }

        private static string CredentialsPath
        {
            get => Path.Combine(DaleDir, "credentials.json");
        }

        private static string ConfigPath
        {
            get => Path.Combine(DaleDir, "config.json");
        }

        public static StoredCredentials? LoadCredentials()
        {
            if (!File.Exists(CredentialsPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(CredentialsPath);
                return JsonSerializer.Deserialize<StoredCredentials>(json, JsonDefaults.Options);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveCredentials(StoredCredentials credentials)
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(credentials, JsonDefaults.Options);
            File.WriteAllText(CredentialsPath, json);
            SetFilePermissions(CredentialsPath);
        }

        public static DaleConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                return new DaleConfig();
            }

            try
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<DaleConfig>(json, JsonDefaults.Options) ?? new DaleConfig();
            }
            catch
            {
                return new DaleConfig();
            }
        }

        public static void SaveConfig(DaleConfig config)
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(config, JsonDefaults.Options);
            File.WriteAllText(ConfigPath, json);
        }

        public static string ResolveAuthBaseUrl(string environment)
        {
            return environment.ToLowerInvariant() switch
            {
                "test" => "https://auth.test.vion.swiss/realms/vion",
                "production" => "https://auth.vion.swiss/realms/vion",
                _ => null!,
            };
        }

        public static string ResolveApiBaseUrl(string environment)
        {
            return environment.ToLowerInvariant() switch
            {
                "test" => "https://api.test.vion.swiss",
                "production" => "https://api.vion.swiss",
                _ => null!,
            };
        }

        /// <summary>
        ///     Returns true if the environment is a known named environment.
        /// </summary>
        public static bool IsKnownEnvironment(string environment)
        {
            return environment.ToLowerInvariant() is "test" or "production";
        }

        public static void DeleteCredentials()
        {
            if (File.Exists(CredentialsPath))
            {
                File.Delete(CredentialsPath);
            }
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(DaleDir))
            {
                Directory.CreateDirectory(DaleDir);
            }
        }

        private static void SetFilePermissions(string path)
        {
            // On Unix, set file permissions to 0600 (owner read/write only)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Best effort — don't fail if permissions can't be set
                }
            }
        }
    }
}