using System;
using System.IO;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Generates and persists registration secrets for service providers.
    ///     The secret is used as an MQTT topic segment during the registration handshake.
    /// </summary>
    [PublicApi]
    public static class RegistrationSecret
    {
        /// <summary>
        ///     Generates a new registration secret suitable for use as an MQTT topic segment.
        ///     Returns a 32-character lowercase hex string (UUID v4 without hyphens).
        /// </summary>
        public static string Generate()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        ///     Loads an existing secret from <paramref name="filePath" />, or generates a new one
        ///     and persists it. Subsequent calls with the same path return the same secret.
        /// </summary>
        /// <param name="filePath">The file path to read from or write to.</param>
        /// <returns>The secret string.</returns>
        public static string LoadOrCreate(string filePath)
        {
            if (File.Exists(filePath))
            {
                var existing = File.ReadAllText(filePath).Trim();
                if (existing.Length > 0)
                {
                    return existing;
                }
            }

            var secret = Generate();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, secret);
            return secret;
        }
    }
}
