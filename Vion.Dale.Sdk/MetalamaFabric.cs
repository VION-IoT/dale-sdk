using Metalama.Framework.Aspects;
using Metalama.Framework.Code;
using Metalama.Framework.Fabrics;
using Metalama.Patterns.Observability;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk
{
    /// <summary>
    ///     Metalama fabric to configure observability patterns for the Vion.Dale.Sdk project itself.
    ///     This applies the [Observable] aspect to types within the Vion.Dale.Sdk project.
    /// </summary>
    public class MetalamaProjectFabric : ProjectFabric
    {
        public override void AmendProject(IProjectAmender amender)
        {
            // Apply Observable aspect to types in THIS project (Vion.Dale.Sdk)
            amender.SelectMany(compilation => compilation.AllTypes).Where(MetalamaSharedLogic.IsServiceType).AddAspect<ObservableAttribute>();
        }
    }

    /// <summary>
    ///     Metalama fabric to configure observability patterns for all consuming projects
    ///     that reference Vion.Dale.Sdk as a NuGet package.
    ///     This fabric applies the [Observable] aspect to:
    ///     1. All types that derive from LogicBlockBase (main logic blocks)
    ///     2. Nested types that are detected as services (similar to DeclarativeServiceBinder logic)
    /// </summary>
    public class MetalamaTransitiveFabric : TransitiveProjectFabric
    {
        public override void AmendProject(IProjectAmender amender)
        {
            // Apply Observable aspect to types in CONSUMING projects
            amender.SelectMany(compilation => compilation.AllTypes).Where(MetalamaSharedLogic.IsServiceType).AddAspect<ObservableAttribute>();
        }
    }

    /// <summary>
    ///     Shared logic for determining if a type should receive the [Observable] aspect.
    ///     Used by both ProjectFabric and TransitiveProjectFabric.
    /// </summary>
    [CompileTime]
    internal static class MetalamaSharedLogic
    {
        /// <summary>
        ///     Determines if a type should be treated as a service and get the Observable aspect.
        ///     This replicates the detection logic from DeclarativeServiceBinder:
        ///     - Derives from LogicBlockBase, OR
        ///     - Has explicit [Service] attribute, OR
        ///     - Implements a [ServiceInterface], OR
        ///     - Has properties with [ServiceProperty] or [ServiceMeasuringPoint] attributes
        /// </summary>
        public static bool IsServiceType(INamedType type)
        {
            // Check if it is a logic block 
            if (type.IsConvertibleTo(typeof(LogicBlockBase), ConversionKind.TypeDefinition))
            {
                return true;
            }

            // Check for explicit [Service] attribute
            foreach (var attr in type.Attributes)
            {
                if (attr.Type.IsConvertibleTo(typeof(ServiceAttribute), ConversionKind.TypeDefinition))
                {
                    return true;
                }
            }

            // Check if type implements any interface marked with [ServiceInterface]
            foreach (var iface in type.AllImplementedInterfaces)
            {
                foreach (var attr in iface.Attributes)
                {
                    if (attr.Type.IsConvertibleTo(typeof(ServiceInterfaceAttribute), ConversionKind.TypeDefinition))
                    {
                        return true;
                    }
                }
            }

            // Check if type has properties with [ServiceProperty] or [ServiceMeasuringPoint] attributes
            foreach (var prop in type.Properties)
            {
                foreach (var attr in prop.Attributes)
                {
                    if (attr.Type.IsConvertibleTo(typeof(ServicePropertyAttribute), ConversionKind.TypeDefinition) ||
                        attr.Type.IsConvertibleTo(typeof(ServiceMeasuringPointAttribute), ConversionKind.TypeDefinition))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
