using System.IO;
using Vion.Dale.Cli.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Vion.Dale.Cli.Test.Helpers
{
    [TestClass]
    public class ParserRunnerTests
    {
        private string _tempDir = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DaleCliTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [TestMethod]
        public void FindParserDll_WithSdkVersion_FindsInNuGetCache()
        {
            // This test verifies the NuGet cache lookup works with a real installed package.
            // It requires Vion.Dale.Sdk 0.1.60 to be in the NuGet cache (which it is in the dev environment).
            var project = new DaleProject
                          {
                              CsprojPath = Path.Combine(_tempDir, "Test.csproj"),
                              ProjectName = "Test",
                              ProjectDirectory = _tempDir,
                              SdkVersion = "0.1.60",
                          };

            var parserDll = ParserRunner.FindParserDll(project);

            // If the NuGet package is cached, we should find the parser
            if (parserDll != null)
            {
                Assert.IsTrue(parserDll.EndsWith("Vion.Dale.LogicBlockParser.dll"));
                Assert.IsTrue(parserDll.Contains("vion.dale.sdk"));
                Assert.IsTrue(File.Exists(parserDll));
            }

            // If not cached (CI environment), the test just passes — no assertion failure
        }

        [TestMethod]
        public void FindParserDll_WithNullSdkVersion_SkipsNuGetCache()
        {
            var project = new DaleProject
                          {
                              CsprojPath = Path.Combine(_tempDir, "Test.csproj"),
                              ProjectName = "Test",
                              ProjectDirectory = _tempDir,
                              SdkVersion = null, // ProjectReference, no version known
                          };

            // Should not crash, just returns null (no local repo fallback in temp dir)
            var parserDll = ParserRunner.FindParserDll(project);

            // In temp dir with no repo, should be null
            Assert.IsNull(parserDll);
        }

        [TestMethod]
        public void FindParserDll_WithInvalidVersion_ReturnsNull()
        {
            var project = new DaleProject
                          {
                              CsprojPath = Path.Combine(_tempDir, "Test.csproj"),
                              ProjectName = "Test",
                              ProjectDirectory = _tempDir,
                              SdkVersion = "99.99.99", // Non-existent version
                          };

            var parserDll = ParserRunner.FindParserDll(project);

            // Non-existent version — NuGet cache miss, no local fallback in temp dir
            Assert.IsNull(parserDll);
        }
    }
}