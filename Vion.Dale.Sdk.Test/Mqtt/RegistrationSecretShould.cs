using System;
using System.IO;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     The secret a host registers with is read from a file it keeps and made once where the file holds
    ///     none, so every later start registers with the same one. Each test works in a temporary directory of
    ///     its own.
    /// </summary>
    [TestClass]
    public class RegistrationSecretShould
    {
        private string _root = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "dale-registration-secret-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // Best effort — a temporary directory left behind fails nothing.
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-005.1")]
        public void ReturnSecretHeldInFileTrimmed()
        {
            // Arrange
            var path = Path.Combine(_root, "secret.txt");
            File.WriteAllText(path, "  held-secret \n");

            // Act
            var secret = RegistrationSecret.LoadOrCreate(path);

            // Assert
            Assert.AreEqual("held-secret", secret);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-005.1")]
        [DataRow("", DisplayName = "empty")]
        [DataRow(" \n\t", DisplayName = "whitespace")]
        public void GenerateSecretWhenFileHoldsNone(string content)
        {
            // Arrange
            var path = Path.Combine(_root, "secret.txt");
            File.WriteAllText(path, content);

            // Act
            var secret = RegistrationSecret.LoadOrCreate(path);

            // Assert
            Assert.MatchesRegex("^[0-9a-f]{32}$", secret);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-005.1")]
        public void GenerateSecretIntoMissingDirectory()
        {
            // Arrange
            var path = Path.Combine(_root, "missing", "secret.txt");

            // Act
            var secret = RegistrationSecret.LoadOrCreate(path);

            // Assert
            Assert.MatchesRegex("^[0-9a-f]{32}$", secret);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-005.1")]
        public void ReturnGeneratedSecretOnLaterRead()
        {
            // Arrange
            var path = Path.Combine(_root, "secret.txt");
            var generated = RegistrationSecret.LoadOrCreate(path);

            // Act
            var secret = RegistrationSecret.LoadOrCreate(path);

            // Assert
            Assert.AreEqual(generated, secret);
        }
    }
}
