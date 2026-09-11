using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     `dale topology validate` over every <c>*.topology.json</c> this repository commits. A synthetic
    ///     fixture passes almost any implementation; the committed files carry the shapes a consumer writes —
    ///     contract pairings, instantiation parameters, explicit contract mappings, a dozen instances — and
    ///     each of them loads today, so the validator refusing one is the validator being wrong.
    ///     <para>
    ///         This pins an implementation premise rather than a criterion — that the mirror and the corpus
    ///         agree — so it cites no acceptance id (<c>testing-conventions.md</c> §17).
    ///     </para>
    /// </summary>
    [TestClass]
    public class TopologyCorpusTests
    {
        [TestMethod]
        public void PassEveryCommittedTopologyFile()
        {
            // Arrange
            var files = Directory.EnumerateFiles(RepoRoot(), $"*{TopologyFileChecks.FileSuffix}", SearchOption.AllDirectories)
                                 .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                                                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                                 .ToArray();

            // Act
            var refused = files.Select(path => (Path: path, Outcome: TopologyFileChecks.Validate(Path.GetFileName(path), File.ReadAllText(path))))
                               .Where(result => result.Outcome.Errors.Count > 0 || result.Outcome.Warnings.Count > 0)
                               .Select(result => $"{result.Path}: {string.Join("; ", result.Outcome.Errors.Concat(result.Outcome.Warnings))}")
                               .ToArray();

            // Assert — the floor is anti-vacuous: an empty walk means the discovery broke, not that the
            // corpus is clean, and a green run over zero files would say nothing about the mirror.
            Assert.IsGreaterThan(20, files.Length, "the discovery walk found almost no committed topology files");
            Assert.IsEmpty(refused, string.Join(Environment.NewLine, refused));
        }

        // The repository root, by the .git marker — the bound ScenarioDefinitionSitesShould walks to.
        private static string RepoRoot()
        {
            var current = AppContext.BaseDirectory;
            for (var depth = 0; depth < 8 && current is not null; depth++)
            {
                var marker = Path.Combine(current, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new DirectoryNotFoundException($"no .git ancestor within 8 levels of {AppContext.BaseDirectory}");
        }
    }
}