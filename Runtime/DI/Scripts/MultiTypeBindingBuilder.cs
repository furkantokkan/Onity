using System;
using Onity.Core;

namespace Onity.DI
{
    /// <summary>
    /// Fluent builder for binding multiple contracts to one implementation.
    /// </summary>
    public sealed class MultiTypeBindingBuilder
    {
        private readonly OnityContainer m_container;
        private readonly Type[] m_contractTypes;
        private readonly Type m_implementationType;
        private bool m_isBound;
        private bool m_isNonLazyRegistered;

        internal MultiTypeBindingBuilder(OnityContainer container, Type[] contractTypes, Type implementationType)
        {
            m_container = container;
            m_contractTypes = contractTypes;
            m_implementationType = implementationType;
            m_isBound = false;
            m_isNonLazyRegistered = false;
        }

        /// <summary>
        /// Registers all contracts as singleton.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public MultiTypeBindingBuilder AsSingle()
        {
            m_container.Register(m_contractTypes, m_implementationType, Lifetime.Singleton);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers all contracts as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public MultiTypeBindingBuilder AsTransient()
        {
            m_container.Register(m_contractTypes, m_implementationType, Lifetime.Transient);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Resolves implementation type during container build.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public MultiTypeBindingBuilder NonLazy()
        {
            if (m_isBound == false)
            {
                throw new OnityBindingException(
                    $"Call {nameof(AsSingle)} or {nameof(AsTransient)} before {nameof(NonLazy)}.");
            }

            if (m_isNonLazyRegistered)
            {
                return this;
            }

            m_container.RegisterBuildCallback(resolver => resolver.Resolve(m_implementationType));
            m_isNonLazyRegistered = true;
            return this;
        }
    }
}
