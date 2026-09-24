using System;
using Onity.Core;

namespace Onity.DI
{
    /// <summary>
    /// Fluent builder for a single contract binding.
    /// </summary>
    /// <typeparam name="TContract">Contract type.</typeparam>
    public sealed class TypeBindingBuilder<TContract>
    {
        private readonly OnityContainer m_container;
        private Type m_implementationType;
        private bool m_isBound;
        private bool m_isNonLazyRegistered;

        internal TypeBindingBuilder(OnityContainer container)
        {
            m_container = container;
            m_implementationType = typeof(TContract);
            m_isBound = false;
            m_isNonLazyRegistered = false;
        }

        /// <summary>
        /// Sets the concrete implementation for this contract.
        /// </summary>
        /// <typeparam name="TConcrete">Concrete type.</typeparam>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> To<TConcrete>()
            where TConcrete : TContract
        {
            m_implementationType = typeof(TConcrete);
            return this;
        }

        /// <summary>
        /// Registers this binding as singleton.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> AsSingle()
        {
            m_container.Register(typeof(TContract), m_implementationType, Lifetime.Singleton);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers this binding as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> AsTransient()
        {
            m_container.Register(typeof(TContract), m_implementationType, Lifetime.Transient);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Resolves this contract during container build.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public TypeBindingBuilder<TContract> NonLazy()
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

            m_container.RegisterBuildCallback(resolver => resolver.Resolve<TContract>());
            m_isNonLazyRegistered = true;
            return this;
        }
    }
}
