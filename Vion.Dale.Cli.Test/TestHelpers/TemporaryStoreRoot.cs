using System;
using System.IO;
using Vion.Dale.Cli.Auth;

namespace Vion.Dale.Cli.Test.TestHelpers
{
    /// <summary>
    ///     Points <see cref="TokenStore" /> at a fresh temporary directory for the life of the scope and
    ///     restores the user profile afterwards, so no test reads or writes the developer's <c>~/.dale</c>.
    /// </summary>
    internal sealed class TemporaryStoreRoot : IDisposable
    {
        public string Root { get; }

        public string CredentialsPath
        {
            get => Path.Combine(Root, ".dale", "credentials.json");
        }

        public string ConfigPath
        {
            get => Path.Combine(Root, ".dale", "config.json");
        }

        public TemporaryStoreRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "dale-cli-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            TokenStore.UseRoot(Root);
        }

        public void Dispose()
        {
            TokenStore.UseRoot(null);
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                // Best effort — a temporary directory left behind fails nothing.
            }
        }

        public void WriteCredentials(string json)
        {
            Directory.CreateDirectory(Path.Combine(Root, ".dale"));
            File.WriteAllText(CredentialsPath, json);
        }

        public void WriteConfig(string json)
        {
            Directory.CreateDirectory(Path.Combine(Root, ".dale"));
            File.WriteAllText(ConfigPath, json);
        }
    }
}