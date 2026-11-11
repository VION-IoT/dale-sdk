using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Vion.Contracts.Conventions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Plugin
{
    /// <summary>
    ///     The <c>Vion.Dale.Sdk</c> versions a host and a logic-block library carry, and whether the host
    ///     meets the library's minimum. Every version is compared as <c>MAJOR.MINOR.PATCH</c>, and
    ///     <c>0.0.0</c> — what a local or CI build stamps — is unknown.
    /// </summary>
    /// <remarks>
    ///     A library's versions are read from assembly metadata, without loading any assembly, so a host
    ///     can read a library built against a newer SDK than its own. A library declares its minimum with
    ///     the <c>MinimumDaleSdkVersion</c> MSBuild property; one that declares none needs the version it
    ///     was built against.
    /// </remarks>
    public static class DaleSdkVersions
    {
        private static readonly string SdkAssemblyName = typeof(LogicBlockBase).Assembly.GetName().Name!;

        private static readonly Regex MajorMinorPatch = new(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);

        /// <summary>The <c>Vion.Dale.Sdk</c> version this process has loaded, or <c>null</c> when unknown.</summary>
        public static Version? Host { get; } = Known(typeof(LogicBlockBase).Assembly.GetName().Version);

        /// <summary>
        ///     Reads a library's target version from every assembly directly in
        ///     <paramref name="pluginDirectory" />, and its declared minimum from
        ///     <paramref name="mainAssemblyPath" />.
        /// </summary>
        public static LibraryDaleSdkVersions ReadLibrary(string pluginDirectory, string mainAssemblyPath)
        {
            return new LibraryDaleSdkVersions(ReadTarget(pluginDirectory), Known(ParseMajorMinorPatch(ReadDeclaredMinimumValue(mainAssemblyPath))));
        }

        /// <summary>
        ///     Returns the library's minimum when <paramref name="host" /> is below it within the same major,
        ///     and <c>null</c> when the minimum is met, either version is unknown, or the majors differ.
        /// </summary>
        public static UnmetDaleSdkMinimum? Compare(LibraryDaleSdkVersions library, Version? host)
        {
            var declaredMinimum = Known(library.DeclaredMinimum);
            var minimum = declaredMinimum ?? Known(library.Target);
            var knownHost = Known(host);

            // A differing major is not an unmet minimum: the load context refuses that library outright.
            if (minimum == null || knownHost == null || minimum.Major != knownHost.Major || minimum <= knownHost)
            {
                return null;
            }

            return new UnmetDaleSdkMinimum(minimum, declaredMinimum != null, knownHost);
        }

        /// <summary>
        ///     <paramref name="version" /> as <c>MAJOR.MINOR.PATCH</c>, or <c>null</c> when it is absent or
        ///     <c>0.0.0</c>. An assembly reference carries four parts and a prerelease none of its own, so a
        ///     prerelease compares as its release.
        /// </summary>
        internal static Version? Known(Version? version)
        {
            if (version == null)
            {
                return null;
            }

            var majorMinorPatch = new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
            return majorMinorPatch == new Version(0, 0, 0) ? null : majorMinorPatch;
        }

        /// <summary><paramref name="value" /> as a version when it is exactly <c>MAJOR.MINOR.PATCH</c>, else <c>null</c>.</summary>
        internal static Version? ParseMajorMinorPatch(string? value)
        {
            return value != null && MajorMinorPatch.IsMatch(value) && Version.TryParse(value, out var version) ? version : null;
        }

        /// <summary>
        ///     The raw <c>MinimumDaleSdkVersion</c> assembly metadata value of the assembly at
        ///     <paramref name="assemblyPath" />, or <c>null</c> when it carries none or cannot be read.
        /// </summary>
        internal static string? ReadDeclaredMinimumValue(string assemblyPath)
        {
            try
            {
                using var stream = File.OpenRead(assemblyPath);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                {
                    return null;
                }

                var metadataReader = peReader.GetMetadataReader();
                foreach (var attributeHandle in metadataReader.GetAssemblyDefinition().GetCustomAttributes())
                {
                    var attribute = metadataReader.GetCustomAttribute(attributeHandle);
                    if (!IsAssemblyMetadataAttribute(metadataReader, attribute))
                    {
                        continue;
                    }

                    // An AssemblyMetadataAttribute blob is the prolog, then the key and the value as
                    // serialized strings — the attribute's two constructor arguments.
                    var blob = metadataReader.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() != 1)
                    {
                        continue;
                    }

                    if (blob.ReadSerializedString() == DaleSdkCompatibilityConventions.MinimumDaleSdkVersionAnnotationKey)
                    {
                        return blob.ReadSerializedString();
                    }
                }
            }
            catch (Exception)
            {
                // A file that is not a readable .NET assembly declares nothing — the same posture as the
                // SDK version gate's own metadata read.
            }

            return null;
        }

        private static bool IsAssemblyMetadataAttribute(MetadataReader metadataReader, CustomAttribute attribute)
        {
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                return false;
            }

            var constructor = metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                return false;
            }

            var type = metadataReader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            return metadataReader.GetString(type.Name) == "AssemblyMetadataAttribute" && metadataReader.GetString(type.Namespace) == "System.Reflection";
        }

        private static Version? ReadTarget(string pluginDirectory)
        {
            if (!Directory.Exists(pluginDirectory))
            {
                return null;
            }

            return Directory.EnumerateFiles(pluginDirectory, "*.dll")
                            .Select(dllPath => Known(PluginLoadContext.TryReadReferencedSdkVersion(Path.GetFullPath(dllPath), SdkAssemblyName)))
                            .Where(version => version != null)
                            .Max();
        }
    }
}