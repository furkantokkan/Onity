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
        private object m_id;
        private Type m_consumerType;
        private OnityContainer.IBakedProvider m_provider;

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
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractTypes, m_implementationType, BindingLifetime.Singleton, m_id, m_consumerType);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers all contracts as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public MultiTypeBindingBuilder AsTransient()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractTypes, m_implementationType, BindingLifetime.Transient, m_id, m_consumerType);
            m_isBound = true;
            return this;
        }

        /// <summary>Shares one implementation across these contracts per resolving scope.</summary>
        /// <returns>Current builder instance.</returns>
        public MultiTypeBindingBuilder AsScoped()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractTypes, m_implementationType, BindingLifetime.Scoped, m_id, m_consumerType);
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
                    $"Call {nameof(AsSingle)}, {nameof(AsScoped)}, or {nameof(AsTransient)} before {nameof(NonLazy)}.");
            }

            if (m_isNonLazyRegistered)
            {
                return this;
            }

            if (m_consumerType != null)
            {
                throw new OnityBindingException("NonLazy requires an unconditional binding.");
            }

            m_container.RegisterNonLazy(m_provider);
            m_isNonLazyRegistered = true;
            return this;
        }

        /// <summary>Sets an identifier for every contract before registering the lifetime.</summary>
        /// <param name="id">Stable binding identifier; null selects unkeyed bindings.</param>
        /// <returns>Current builder.</returns>
        public MultiTypeBindingBuilder WithId(object id)
        {
            ValidateNotBound();
            m_id = id;
            return this;
        }

        /// <summary>Restricts injection to this consumer type and its derived types. Call before the lifetime.</summary>
        /// <typeparam name="TConsumer">Consuming implementation type or interface.</typeparam>
        /// <returns>Current builder.</returns>
        public MultiTypeBindingBuilder WhenInjectedInto<TConsumer>()
        {
            ValidateNotBound();
            m_consumerType = typeof(TConsumer);
            return this;
        }

        private void ValidateNotBound()
        {
            if (m_isBound)
            {
                throw new OnityBindingException("Configure the binding before selecting a lifetime.");
            }
        }
    }
}
