using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Vion.Dale.Plugin.Test.TestHelpers
{
    /// <summary>
    ///     Compiles throwaway assemblies onto disk so the resolution tests can bind against real PE
    ///     files. Fixtures are what the plugin loader actually reads — it decides sharing from an
    ///     assembly's metadata and its SDK reference from an assembly reference row — so a stub type
    ///     or a mock cannot stand in for one.
    /// </summary>
    internal static class FixtureAssembly
    {
        /// <summary>
        ///     A type in the stand-in SDK a fixture can derive from so its reference to that SDK
        ///     survives compilation.
        /// </summary>
        public const string StandInSdkAnchorType = "Vion.Dale.Sdk.Core.SdkAnchor";

        private static IEnumerable<MetadataReference> RuntimeReferences
        {
            get
            {
                yield return MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
                yield return MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location);

                // The real Vion.Dale.Sdk targets netstandard2.1, so a fixture referencing it needs
                // the facade to resolve System.Attribute through the SDK's own type forwards.
                yield return MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location);
            }
        }

        /// <summary>
        ///     Every fixture gets a name unique to its test run. The loader's shared-extension
        ///     registry is keyed by simple name and lives for the lifetime of the process, so two
        ///     tests reusing a name would resolve each other's assemblies.
        /// </summary>
        public static string UniqueName(string prefix)
        {
            return $"{prefix}{Guid.NewGuid():N}";
        }

        public static string CreateDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), $"dale-plug-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        ///     Emits a minimal assembly named <paramref name="simpleName" /> into
        ///     <paramref name="directory" /> and returns its path.
        /// </summary>
        /// <param name="references">Assemblies the fixture references, on top of the runtime's own.</param>
        /// <param name="attributes">
        ///     Assembly-level attribute usages, written verbatim (e.g.
        ///     <c>[assembly: Vion.Dale.Sdk.Core.DaleSharedAssembly]</c>).
        /// </param>
        /// <param name="body">
        ///     The fixture's type declaration. Override it to make the fixture genuinely use a
        ///     referenced assembly: the compiler drops an assembly reference nothing touches, and an
        ///     assembly reference row is precisely what the SDK version gate reads.
        /// </param>
        /// <param name="fileName">
        ///     The file name to write, without extension; defaults to <paramref name="simpleName" />.
        ///     Pass a different one to build the assembly whose file name and simple name disagree.
        /// </param>
        public static string Emit(string directory,
                                  string simpleName,
                                  IEnumerable<string>? references = null,
                                  IEnumerable<string>? attributes = null,
                                  string body = "public class FixtureMarker { }",
                                  string? fileName = null)
        {
            var source = string.Join(Environment.NewLine, string.Join(Environment.NewLine, attributes ?? Array.Empty<string>()), body);

            var metadataReferences = RuntimeReferences.Concat((references ?? Array.Empty<string>()).Select(r => MetadataReference.CreateFromFile(r)));
            var compilation = CSharpCompilation.Create(simpleName,
                                                       new[] { CSharpSyntaxTree.ParseText(source) },
                                                       metadataReferences,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var path = Path.Combine(directory, $"{fileName ?? simpleName}.dll");
            var result = compilation.Emit(path);
            if (!result.Success)
            {
                throw new
                    InvalidOperationException($"Fixture '{simpleName}' did not compile: {string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}");
            }

            return path;
        }

        /// <summary>
        ///     Emits a stand-in <c>Vion.Dale.Sdk</c> at <paramref name="version" /> declaring the
        ///     <c>[DaleSharedAssembly]</c> attribute and <see cref="StandInSdkAnchorType" />, so a
        ///     fixture can be built against an SDK version other than the host's. The loader
        ///     recognises the attribute by name and namespace, never by resolved identity, which is
        ///     what makes this stand-in work.
        /// </summary>
        public static string EmitStandInSdk(string directory, Version version)
        {
            var source = $$"""
                           [assembly: System.Reflection.AssemblyVersion("{{version}}")]

                           namespace Vion.Dale.Sdk.Core
                           {
                               public sealed class DaleSharedAssemblyAttribute : System.Attribute
                               {
                               }

                               public class SdkAnchor
                               {
                               }
                           }
                           """;

            var compilation = CSharpCompilation.Create("Vion.Dale.Sdk",
                                                       new[] { CSharpSyntaxTree.ParseText(source) },
                                                       RuntimeReferences,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var path = Path.Combine(directory, "Vion.Dale.Sdk.dll");
            var result = compilation.Emit(path);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Stand-in SDK did not compile: {string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}");
            }

            return path;
        }

        /// <summary>
        ///     Emits a library declaring a <c>DaleSharedAssemblyAttribute</c> in
        ///     <paramref name="attributeNamespace" />, so a fixture can apply an attribute of the
        ///     right name from the wrong namespace. Declaring it in the fixture itself would not do:
        ///     the loader reads the attribute's constructor as a reference to another assembly, so a
        ///     locally declared one is passed over for a reason that has nothing to do with names.
        /// </summary>
        public static string EmitStandInMarkerLibrary(string directory, string simpleName, string attributeNamespace)
        {
            var source = $$"""
                           namespace {{attributeNamespace}}
                           {
                               public sealed class DaleSharedAssemblyAttribute : System.Attribute
                               {
                               }
                           }
                           """;

            var compilation = CSharpCompilation.Create(simpleName,
                                                       new[] { CSharpSyntaxTree.ParseText(source) },
                                                       RuntimeReferences,
                                                       new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var path = Path.Combine(directory, $"{simpleName}.dll");
            var result = compilation.Emit(path);
            if (!result.Success)
            {
                throw new
                    InvalidOperationException($"Stand-in marker library did not compile: {string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}");
            }

            return path;
        }

        /// <summary>
        ///     Writes a file with a <c>.dll</c> extension that is not a PE image at all.
        /// </summary>
        public static string EmitUnreadable(string directory, string simpleName)
        {
            var path = Path.Combine(directory, $"{simpleName}.dll");
            File.WriteAllText(path, "this is not a PE file");
            return path;
        }
    }
}