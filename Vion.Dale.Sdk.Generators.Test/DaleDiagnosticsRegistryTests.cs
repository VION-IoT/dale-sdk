using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Vion.Dale.Sdk.Generators.Analyzers;

namespace Vion.Dale.Sdk.Generators.Test
{
    /// <summary>
    ///     The registry as a contract with the consumer. Each rule here holds over the whole registry
    ///     rather than over a roster, so a descriptor added tomorrow is judged by it without anyone
    ///     remembering to come back — which is the point: an id, its category and its default severity are
    ///     what a consumer configures against in <c>.editorconfig</c>.
    /// </summary>
    [TestClass]
    public class DaleDiagnosticsRegistryTests
    {
        private static readonly ImmutableArray<DiagnosticDescriptor> Descriptors =
            typeof(DaleDiagnostics).GetFields(BindingFlags.Public | BindingFlags.Static)
                                   .Where(f => f.FieldType == typeof(DiagnosticDescriptor))
                                   .Select(f => (DiagnosticDescriptor)f.GetValue(null)!)
                                   .ToImmutableArray();

        /// <summary>The analyzers that report from a whole-compilation action, which is what the tag is for.</summary>
        private static readonly DiagnosticAnalyzer[] WholeCompilationAnalyzers = [new PublicApiDocumentationAnalyzer(), new ServiceRelationAnalyzer()];

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.1")]
        public void ReportEveryDiagnosticUnderOneCategory()
        {
            // Arrange / Act
            var categories = Descriptors.Select(d => d.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

            // Assert
            Assert.HasCount(1, categories, "one category is what lets a consumer configure the whole rule set with one prefix: " + string.Join(", ", categories));
            Assert.AreEqual("Vion.Dale.Usage", categories[0]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.3")]
        public void EnableEveryDiagnosticByDefault()
        {
            // Arrange / Act
            var disabled = Descriptors.Where(d => !d.IsEnabledByDefault).Select(d => d.Id).ToList();

            // Assert
            Assert.IsEmpty(disabled, "a rule that ships switched off enforces nothing: " + string.Join(", ", disabled));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.5")]
        public void CarryOneDescriptorPerDiagnosticId()
        {
            // Arrange / Act
            var repeated = Descriptors.GroupBy(d => d.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            // Assert
            Assert.IsEmpty(repeated,
                           "two descriptors sharing an id would let a consumer's .editorconfig configure one and not the other; " +
                           "an id whose rules differ in severity reports through the effectiveSeverity overload instead: " + string.Join(", ", repeated));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.4")]
        public void TagEveryWholeCompilationDiagnosticForLiveAnalysis()
        {
            // Arrange
            var reportedFromWholeCompilation = WholeCompilationAnalyzers.SelectMany(a => a.SupportedDiagnostics).Select(d => d.Id).ToImmutableHashSet(StringComparer.Ordinal);

            // Act
            var tagged = Descriptors.Where(d => d.CustomTags.Contains(WellKnownDiagnosticTags.CompilationEnd)).Select(d => d.Id).ToImmutableHashSet(StringComparer.Ordinal);

            // Assert
            Assert.IsEmpty(reportedFromWholeCompilation.Except(tagged).OrderBy(id => id, StringComparer.Ordinal),
                           "an untagged whole-compilation diagnostic is dropped from IDE live analysis, so an author sees it only on a full build.");
            Assert.IsEmpty(tagged.Except(reportedFromWholeCompilation).OrderBy(id => id, StringComparer.Ordinal), "the tag on a per-symbol diagnostic claims an analysis it does not do.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-003.3")]
        [DataRow("DALE003", new[] { "bool", "string", "byte", "short", "ushort", "int", "uint", "long", "float", "double", "DateTime", "TimeSpan", "Guid", "enum" })]
        [DataRow("DALE016", new[] { "primitive", "enum", "string", "TimeSpan", "Guid" })]
        public void NameEveryAcceptedTypeInEachSupportedTypeMessage(string id, string[] accepted)
        {
            // Arrange
            var message = Descriptors.Single(d => d.Id == id).MessageFormat.ToString();

            // Act
            var unnamed = accepted.Where(type => !message.Contains(type, StringComparison.Ordinal)).ToList();

            // Assert
            Assert.IsEmpty(unnamed, $"{id}'s message is the only place an author is told the whole set, and it omits: " + string.Join(", ", unnamed));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.6")]
        public void ReserveErrorSeverityForRulesThatFailBuilds()
        {
            // Arrange / Act
            var unexpected = Descriptors.Where(d => d.DefaultSeverity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning or DiagnosticSeverity.Info))
                                        .Select(d => $"{d.Id}={d.DefaultSeverity}")
                                        .ToList();

            // Assert
            Assert.IsEmpty(unexpected, "a Hidden diagnostic reaches no build log and no editor squiggle, so it enforces nothing: " + string.Join(", ", unexpected));
            Assert.IsNotEmpty(Descriptors.Where(d => d.DefaultSeverity == DiagnosticSeverity.Error), "the registry's whole purpose is that some rules fail a build.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-020.1")]
        public void LeaveEveryDiagnosticConfigurableByConsumers()
        {
            // Arrange / Act
            var notConfigurable = Descriptors.Where(d => d.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable)).Select(d => d.Id).ToList();

            // Assert
            Assert.IsEmpty(notConfigurable,
                           "a NotConfigurable tag would defeat #pragma, [SuppressMessage], NoWarn and .editorconfig at once, and every fixture " +
                           "in this repository that declares a deliberately illegal shape depends on them: " + string.Join(", ", notConfigurable));
        }

        [TestMethod]
        [TestProperty("spec", "AC-ANLZ-001.2")]
        public void LeaveRetiredDiagnosticIdsUnused()
        {
            // Arrange
            var retired = new HashSet<string>(StringComparer.Ordinal) { "DALE006", "DALE029" };

            // Act
            var reused = Descriptors.Select(d => d.Id).Where(retired.Contains).ToList();

            // Assert
            Assert.IsEmpty(reused,
                           "a consumer's .editorconfig entry or #pragma naming a retired id must never start configuring a different rule: " + string.Join(", ", reused));
        }
    }
}
