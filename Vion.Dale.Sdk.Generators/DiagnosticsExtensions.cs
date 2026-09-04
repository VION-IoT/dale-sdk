using Microsoft.CodeAnalysis;

namespace Vion.Dale.Sdk.Generators
{
    /// <summary>
    ///     The source generator's own diagnostics. They are outside the <c>DALE</c> registry — a separate
    ///     category, no <c>WellKnownDiagnosticTags</c>, no id a consumer's <c>.editorconfig</c> can reach
    ///     through a <c>DALE</c> prefix — and they are absent from any build where the generator does not
    ///     run.
    ///     <para>
    ///         Each descriptor carries a constant <c>{0}</c> message format with the message as its
    ///         argument. Passing the message itself as the format made every brace in it a placeholder, so
    ///         a template error naming Scriban's own <c>{{ … }}</c> syntax threw inside
    ///         <see cref="Diagnostic.Create(DiagnosticDescriptor, Location, object[])" /> and replaced the
    ///         message with a generator crash.
    ///     </para>
    /// </summary>
    public static class DiagnosticsExtensions
    {
        private const string MessageFormat = "{0}";

        private static readonly DiagnosticDescriptor InfoDescriptor = new($"{nameof(LogicClassGenerator)}INF",
                                                                          "Source Generator Info",
                                                                          MessageFormat,
                                                                          "SourceGenerator",
                                                                          DiagnosticSeverity.Info,
                                                                          true);

        private static readonly DiagnosticDescriptor ErrorDescriptor =
            new($"{nameof(LogicClassGenerator)}ERR",
                "Source Generator Error",
                MessageFormat,
                "SourceGenerator",
                DiagnosticSeverity.Error,
                true);

        public static void LogInfo(this SourceProductionContext context, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(InfoDescriptor, Location.None, message));
        }

        public static void LogError(this SourceProductionContext context, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(ErrorDescriptor, Location.None, message));
        }
    }
}