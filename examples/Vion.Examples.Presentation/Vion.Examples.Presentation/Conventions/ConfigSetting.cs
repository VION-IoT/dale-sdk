using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: a persistent operator-tunable setting. Renders in the
    ///     <c>Configuration</c> section.
    /// </summary>
    public class ConfigSetting : PresentationAttribute
    {
        public ConfigSetting()
        {
            Group = PropertyGroup.Configuration;
        }
    }
}
