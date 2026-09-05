using System;
using System.IO;
using System.Text;

namespace Vion.Dale.Cli.Test.TestHelpers
{
    /// <summary>
    ///     A Dale project on disk — a csproj referencing the SDK package and one logic block — for the
    ///     tests that drive an <c>add</c> command through the parser rather than its snippet builder. The
    ///     generators write real files, so the only way to assert "before writing anything" is to read the
    ///     bytes back.
    /// </summary>
    internal sealed class TemporaryDaleProject : IDisposable
    {
        /// <summary>A logic block with one plain <c>double</c> property, the annotate path's target.</summary>
        public const string DefaultBlock = """
                                           using Vion.Dale.Sdk.Core;

                                           namespace MyLib
                                           {
                                               public class MyBlock : LogicBlockBase
                                               {
                                                   public double Power { get; private set; }
                                               }
                                           }
                                           """;

        public string Directory { get; }

        public string CsprojPath { get; }

        public string BlockPath { get; }

        public TemporaryDaleProject(string blockSource = DefaultBlock, string newLine = "\n", bool byteOrderMark = false)
        {
            Directory = Path.Combine(Path.GetTempPath(), "dale-cli-project-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            CsprojPath = Path.Combine(Directory, "MyLib.csproj");
            File.WriteAllText(CsprojPath,
                              """
                              <Project Sdk="Microsoft.NET.Sdk">
                                  <PropertyGroup>
                                      <TargetFramework>netstandard2.1</TargetFramework>
                                  </PropertyGroup>
                                  <ItemGroup>
                                      <PackageReference Include="Vion.Dale.Sdk" Version="0.11.2"/>
                                  </ItemGroup>
                              </Project>
                              """);

            BlockPath = Path.Combine(Directory, "MyBlock.cs");
            Write(BlockPath, blockSource, newLine, byteOrderMark);
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, true);
            }
            catch (IOException)
            {
                // Best effort — a temporary directory left behind fails nothing.
            }
        }

        public void WriteDependencyInjection(string source, string newLine, bool byteOrderMark)
        {
            Write(Path.Combine(Directory, "DependencyInjection.cs"), source, newLine, byteOrderMark);
        }

        public byte[] ReadBytes(string fileName)
        {
            return File.ReadAllBytes(Path.Combine(Directory, fileName));
        }

        public string ReadText(string fileName)
        {
            return File.ReadAllText(Path.Combine(Directory, fileName));
        }

        private static void Write(string path, string source, string newLine, bool byteOrderMark)
        {
            var text = source.Replace("\r\n", "\n").Replace("\n", newLine) + newLine;
            File.WriteAllText(path, text, new UTF8Encoding(byteOrderMark));
        }
    }
}