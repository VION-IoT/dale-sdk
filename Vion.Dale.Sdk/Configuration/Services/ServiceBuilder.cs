using System;
using System.Linq.Expressions;

namespace Vion.Dale.Sdk.Configuration.Services
{
    internal class ServiceBuilder : ServiceBuilderBase
    {
        public ServiceBuilder(ServiceBinder binder, string serviceIdentifier) : base(binder, serviceIdentifier)
        {
        }

        /// <summary>
        ///     Declares that this service implements the given service interface, and allows binding properties defined in that
        ///     interface.
        /// </summary>
        public ServiceBuilder Implements<TServiceInterface>(Action<ServiceDeclaration<TServiceInterface>> configure)
        {
            var decl = new ServiceDeclaration<TServiceInterface>(Binder, ServiceIdentifier);
            configure(decl);
            return this;
        }

        /// <summary>
        ///     Binds a property that is not part of any service interface.
        /// </summary>
        public ServiceBuilder BindProperty<TSource, TProp>(string servicePropertyIdentifier,
                                                           TSource source,
                                                           Expression<Func<TSource, TProp>> sourceGetter,
                                                           Action<TSource, TProp>? sourceSetter = null)
            where TSource : class
        {
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterPropertyBinding(servicePropertyIdentifier,
                                    source,
                                    fullPath,
                                    rootPropertyInfo,
                                    targetPropertyType,
                                    s => compiledGetter((TSource)s),
                                    sourceSetter != null ? (s, v) => sourceSetter((TSource)s, (TProp)v!) : null);

            return this;
        }

        /// <summary>
        ///     Binds a measuring point that is not part of any service interface.
        /// </summary>
        public ServiceBuilder BindMeasuringPoint<TSource, TProp>(string serviceMeasuringPointIdentifier, TSource source, Expression<Func<TSource, TProp>> sourceGetter)
            where TSource : class
        {
            var (fullPath, rootPropertyInfo) = ReflectionHelper.GetPropertyPath(sourceGetter);
            var compiledGetter = sourceGetter.Compile();
            var targetPropertyType = ReflectionHelper.GetTargetPropertyType(typeof(TSource), fullPath);

            RegisterMeasuringPointBinding(serviceMeasuringPointIdentifier,
                                          source,
                                          fullPath,
                                          rootPropertyInfo,
                                          targetPropertyType,
                                          s => compiledGetter((TSource)s),
                                          null); // Measuring points don't have setters

            return this;
        }
    }
}