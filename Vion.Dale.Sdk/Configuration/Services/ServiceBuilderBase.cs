using System;
using System.Reflection;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Configuration.Services
{
    /// <summary>
    ///     Non-generic base implementation of service builder for reflection-free binding
    /// </summary>
    internal class ServiceBuilderBase
    {
        protected readonly ServiceBinder Binder;

        protected readonly string ServiceIdentifier;

        protected ServiceBuilderBase(ServiceBinder binder, string serviceIdentifier)
        {
            Binder = binder;
            ServiceIdentifier = serviceIdentifier;
        }

        /// <summary>
        ///     Declares service implementation using the non-generic approach
        /// </summary>
        public void Implements(Type interfaceType, Action<ServiceDeclarationBase> configure)
        {
            var decl = new ServiceDeclarationBase(Binder, ServiceIdentifier, interfaceType);
            configure(decl);
        }

        /// <summary>
        ///     Binds a property using a PropertyInfo with compiled expressions for better performance
        /// </summary>
        public void BindPropertyWithCompiledExpression(string servicePropertyIdentifier, object source, PropertyInfo propertyInfo)
        {
            // Create a compiled getter expression
            var getter = ReflectionHelper.CreateCompiledGetter(propertyInfo, source.GetType());

            // Create a compiled setter expression (if not read-only)
            var setter = ReflectionHelper.HasPublicSetter(propertyInfo) ? ReflectionHelper.CreateCompiledSetter(propertyInfo, source.GetType()) : null;

            // Use the core registration method with the compiled expressions
            RegisterPropertyBinding(servicePropertyIdentifier,
                                    source,
                                    propertyInfo.Name,
                                    propertyInfo,
                                    propertyInfo.PropertyType, // Target property type
                                    getter,
                                    setter);
        }

        /// <summary>
        ///     Binds a measuring point using a PropertyInfo with compiled expressions for better performance
        /// </summary>
        public void BindMeasuringPointWithCompiledExpression(string serviceMeasuringPointIdentifier, object source, PropertyInfo propertyInfo)
        {
            // Create a compiled getter expression
            var getter = ReflectionHelper.CreateCompiledGetter(propertyInfo, source.GetType());

            // Use the core registration method with the compiled expression
            RegisterMeasuringPointBinding(serviceMeasuringPointIdentifier,
                                          source,
                                          propertyInfo.Name,
                                          propertyInfo,
                                          propertyInfo.PropertyType, // Target property type
                                          getter,
                                          null); // Measuring points are read-only
        }

        /// <summary>
        ///     Core registration method for property bindings, used by both string-based and expression-based bindings
        /// </summary>
        protected void RegisterPropertyBinding(string servicePropertyIdentifier,
                                               object source,
                                               string sourcePropertyPath,
                                               PropertyInfo rootSourcePropertyInfo,
                                               Type targetPropertyType,
                                               Func<object, object?> getter,
                                               Action<object, object?>? setter)
        {
            var typeRef = TypeRefBuilder.BuildForProperty(rootSourcePropertyInfo);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(rootSourcePropertyInfo.PropertyType);
            var metadata = PropertyMetadataBuilder.Build(rootSourcePropertyInfo, typeRef, structFieldAnnotations);

            var binding = new ServiceBinding
                          {
                              Source = source,
                              SourcePropertyName = sourcePropertyPath,
                              RootSourcePropertyName = rootSourcePropertyInfo.Name,
                              RootSourcePropertyInfo = rootSourcePropertyInfo,
                              TargetPropertyType = targetPropertyType,
                              Getter = getter,
                              Setter = setter,
                              ServicePropertyName = servicePropertyIdentifier,
                              Metadata = metadata,
                          };

            Binder.RegisterServicePropertyBinding(ServiceIdentifier, null, servicePropertyIdentifier, binding);
        }

        /// <summary>
        ///     Core registration method for measuring point bindings, used by both string-based and expression-based bindings
        /// </summary>
        protected void RegisterMeasuringPointBinding(string serviceMeasuringPointIdentifier,
                                                     object source,
                                                     string sourcePropertyPath,
                                                     PropertyInfo rootSourcePropertyInfo,
                                                     Type targetPropertyType,
                                                     Func<object, object?> getter,
                                                     Action<object, object?>? setter)
        {
            var typeRef = TypeRefBuilder.BuildForProperty(rootSourcePropertyInfo);
            var structFieldAnnotations = TypeRefBuilder.BuildStructFieldAnnotations(rootSourcePropertyInfo.PropertyType);
            var metadata = PropertyMetadataBuilder.Build(rootSourcePropertyInfo, typeRef, structFieldAnnotations);

            var binding = new ServiceBinding
                          {
                              Source = source,
                              SourcePropertyName = sourcePropertyPath,
                              RootSourcePropertyName = rootSourcePropertyInfo.Name,
                              RootSourcePropertyInfo = rootSourcePropertyInfo,
                              TargetPropertyType = targetPropertyType,
                              Getter = getter,
                              Setter = setter,
                              ServicePropertyName = serviceMeasuringPointIdentifier,
                              Metadata = metadata,
                          };

            Binder.RegisterServiceMeasuringPointBinding(ServiceIdentifier, null, serviceMeasuringPointIdentifier, binding);
        }
    }
}
