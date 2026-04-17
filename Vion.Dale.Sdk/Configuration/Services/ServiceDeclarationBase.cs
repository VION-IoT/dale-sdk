using System;
using System.Reflection;

namespace Vion.Dale.Sdk.Configuration.Services
{
    /// <summary>
    ///     Base non-generic implementation of service declaration for reflection-free binding
    /// </summary>
    public class ServiceDeclarationBase
    {
        private readonly ServiceBinder _binder;

        private readonly string _serviceIdentifier;

        private readonly Type _serviceInterfaceType;

        public ServiceDeclarationBase(ServiceBinder binder, string serviceIdentifier, Type serviceInterfaceType)
        {
            _binder = binder;
            _serviceIdentifier = serviceIdentifier;
            _serviceInterfaceType = serviceInterfaceType;
        }

        /// <summary>
        ///     Core registration method for property bindings, used by both string-based and expression-based bindings
        /// </summary>
        protected void RegisterPropertyBinding(string servicePropertyName,
                                               object source,
                                               string sourcePropertyPath,
                                               PropertyInfo rootSourcePropertyInfo,
                                               Type targetPropertyType,
                                               Func<object, object?> getter,
                                               Action<object, object?>? setter)
        {
            var binding = new ServiceBinding
                          {
                              Source = source,
                              SourcePropertyName = sourcePropertyPath,
                              RootSourcePropertyName = rootSourcePropertyInfo.Name,
                              RootSourcePropertyInfo = rootSourcePropertyInfo,
                              TargetPropertyType = targetPropertyType,
                              Getter = getter,
                              Setter = setter,
                              ServicePropertyName = servicePropertyName,
                          };

            _binder.RegisterServicePropertyBinding(_serviceIdentifier, _serviceInterfaceType, servicePropertyName, binding);
        }

        /// <summary>
        ///     Core registration method for measuring point bindings, used by both string-based and expression-based bindings
        /// </summary>
        protected void RegisterMeasuringPointBinding(string serviceMeasuringPointName,
                                                     object source,
                                                     string sourcePropertyPath,
                                                     PropertyInfo rootSourcePropertyInfo,
                                                     Type targetPropertyType,
                                                     Func<object, object?> getter,
                                                     Action<object, object?>? setter)
        {
            var binding = new ServiceBinding
                          {
                              Source = source,
                              SourcePropertyName = sourcePropertyPath,
                              RootSourcePropertyName = rootSourcePropertyInfo.Name,
                              RootSourcePropertyInfo = rootSourcePropertyInfo,
                              TargetPropertyType = targetPropertyType,
                              Getter = getter,
                              Setter = setter,
                              ServicePropertyName = serviceMeasuringPointName,
                          };

            _binder.RegisterServiceMeasuringPointBinding(_serviceIdentifier, _serviceInterfaceType, serviceMeasuringPointName, binding);
        }

        /// <summary>
        ///     Binds a property using a PropertyInfo with compiled expressions for better performance
        /// </summary>
        internal void BindPropertyWithCompiledExpression(string servicePropertyName, object source, PropertyInfo propertyInfo)
        {
            // Create a compiled getter expression
            var getter = ReflectionHelper.CreateCompiledGetter(propertyInfo, source.GetType());

            // Create a compiled setter expression (if not read-only)
            var setter = ReflectionHelper.HasPublicSetter(propertyInfo) ? ReflectionHelper.CreateCompiledSetter(propertyInfo, source.GetType()) : null;

            // Use the core registration method with the compiled expressions
            RegisterPropertyBinding(servicePropertyName,
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
        internal void BindMeasuringPointWithCompiledExpression(string serviceMeasuringPointName, object source, PropertyInfo propertyInfo)
        {
            // Create a compiled getter expression
            var getter = ReflectionHelper.CreateCompiledGetter(propertyInfo, source.GetType());

            // Use the core registration method with the compiled expression
            RegisterMeasuringPointBinding(serviceMeasuringPointName,
                                          source,
                                          propertyInfo.Name,
                                          propertyInfo,
                                          propertyInfo.PropertyType, // Target property type
                                          getter,
                                          null); // Measuring points are read-only
        }

        /// <summary>
        ///     Register a service relation with the binder
        /// </summary>
        internal void RegisterServiceRelation(ServiceRelationInfo relationInfo)
        {
            _binder.RegisterServiceRelation(_serviceIdentifier, relationInfo);
        }
    }
}