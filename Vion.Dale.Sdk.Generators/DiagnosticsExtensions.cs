using Microsoft.CodeAnalysis;

namespace Vion.Dale.Sdk.Generators
{
    public static class DiagnosticsExtensions
    {
        public static void LogDebug(this SourceProductionContext context, string message, params object[] args)
        {
            var descriptor = new DiagnosticDescriptor($"{nameof(LogicClassGenerator)}DBG",
                                                      "Source Generator Info",
                                                      message,
                                                      "SourceGenerator",
                                                      DiagnosticSeverity.Info,
                                                      true);

            var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
            context.ReportDiagnostic(diagnostic);
        }

        public static void LogInfo(this SourceProductionContext context, string message, params object[] args)
        {
            var descriptor = new DiagnosticDescriptor($"{nameof(LogicClassGenerator)}INF",
                                                      "Source Generator Info as Warning",
                                                      message,
                                                      "SourceGenerator",
                                                      DiagnosticSeverity.Warning,
                                                      true);

            var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
            context.ReportDiagnostic(diagnostic);
        }

        public static void LogError(this SourceProductionContext context, string message, params object[] args)
        {
            var descriptor = new DiagnosticDescriptor($"{nameof(LogicClassGenerator)}ERR",
                                                      "Source Generator Error",
                                                      message,
                                                      "SourceGenerator",
                                                      DiagnosticSeverity.Error,
                                                      true);

            var diagnostic = Diagnostic.Create(descriptor, Location.None, args);
            context.ReportDiagnostic(diagnostic);
        }
    }
}