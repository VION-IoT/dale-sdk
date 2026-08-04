using System;
using System.Linq.Expressions;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServiceDeclaration<TServiceInterface> : ServiceDeclarationBase
    {
        public ServiceDeclaration(ServiceBinder binder, string serviceIdentifier) : base(binder, serviceIdentifier, typeof(TServiceInterface))
        {
        }

        /// <summary>
        ///     Binds a property of the source object to a property of the service interface.
        /// </summary>
        public ServiceDeclaration<TServiceInterface> BindProperty<TSource, TProp>(Expression<Func<TServiceInterface, TProp>> serviceInterfaceProperty,
                                                                                  TSource source,
                                                                                  Expression<Func<TSource, TProp>> sourceGetter,
                                                                                  Action<TSource, TProp>? sourceSetter = null)
            where TSource : class
        {
            var servicePropertyName = ReflectionHelper.GetSinglePropertyName(serviceInterfaceProperty);
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterPropertyBinding(servicePropertyName,
                                    source,
                                    fullPath,
                                    rootPropertyInfo,
                                    targetPropertyType,
                                    s => compiledGetter((TSource)s),
                                    sourceSetter != null ? (s, v) => sourceSetter((TSource)s, (TProp)v!) : null);

            return this;
        }

        /// <summary>
        ///     Binds a measuring point of the source object to a property of the service interface.
        /// </summary>
        public ServiceDeclaration<TServiceInterface> BindMeasuringPoint<TSource, TProp>(Expression<Func<TServiceInterface, TProp>> serviceInterfaceProperty,
                                                                                        TSource source,
                                                                                        Expression<Func<TSource, TProp>> sourceGetter,
                                                                                        Action<TSource, TProp>? sourceSetter = null)
            where TSource : class
        {
            var serviceMeasuringPointName = ReflectionHelper.GetSinglePropertyName(serviceInterfaceProperty);
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterMeasuringPointBinding(serviceMeasuringPointName,
                                          source,
                                          fullPath,
                                          rootPropertyInfo,
                                          targetPropertyType,
                                          s => compiledGetter((TSource)s),
                                          sourceSetter != null ? (s, v) => sourceSetter((TSource)s, (TProp)v!) : null);

            return this;
        }
    }
}