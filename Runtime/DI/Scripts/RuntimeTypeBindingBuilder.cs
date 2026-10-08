using System;
using Onity.Core;

namespace Onity.DI
{
    /// <summary>
    /// Fluent builder for a binding declared with runtime <see cref="Type" /> objects
    /// rather than generic parameters. Supports open generic definitions -
    /// <c>Bind(typeof(IRepo&lt;&gt;)).To(typeof(Repo&lt;&gt;)).AsSingle()</c> - so a later
    /// resolve of a closed <c>IRepo&lt;Foo&gt;</c> constructs <c>Repo&lt;Foo&gt;</c> on
    /// demand, and also closed runtime-typed bindings (handy for reflection- or
    /// AI-driven registration that only has <see cref="Type" /> values).
    /// </summary>
    public sealed class RuntimeTypeBindingBuilder
    {
        private readonly OnityContainer m_container;
        private readonly Type m_contractType;
        private Type m_implementationType;
        private bool m_isBound;
        private bool m_isNonLazyRegistered;
        private readonly bool m_replace;
        private object m_id;
        private Type m_consumerType;
        private OnityContainer.IBakedProvider m_provider;

        internal RuntimeTypeBindingBuilder(OnityContainer container, Type contractType, bool replace = false, object id = null)
        {
            m_container = container;
            m_contractType = contractType;
            m_replace = replace;
            m_id = id;
            m_implementationType = contractType;
            m_isBound = false;
            m_isNonLazyRegistered = false;
        }

        /// <summary>
        /// Sets the implementation type. For an open generic contract this must be an
        /// open generic definition with the same type-parameter count (e.g.
        /// <c>typeof(Repo&lt;&gt;)</c>).
        /// </summary>
        /// <param name="implementationType">Implementation type.</param>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder To(Type implementationType)
        {
            ValidateNotBound();
            if (implementationType == null)
            {
                throw new OnityBindingException("Implementation type cannot be null.");
            }

            m_implementationType = implementationType;
            return this;
        }

        /// <summary>Sets an identifier before registering the lifetime. Null selects the unkeyed binding.</summary>
        /// <param name="id">Stable binding identifier, compared by value.</param>
        /// <returns>Current builder.</returns>
        public RuntimeTypeBindingBuilder WithId(object id)
        {
            ValidateNotBound();
            m_id = id;
            return this;
        }

        /// <summary>Restricts injection to this consumer type and its derived types. Call before the lifetime.</summary>
        /// <typeparam name="TConsumer">Consuming implementation type or interface.</typeparam>
        /// <returns>Current builder.</returns>
        public RuntimeTypeBindingBuilder WhenInjectedInto<TConsumer>()
        {
            ValidateNotBound();
            m_consumerType = typeof(TConsumer);
            return this;
        }

        /// <summary>
        /// Registers this binding as singleton.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder AsSingle()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractType, m_implementationType, BindingLifetime.Singleton, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers this binding as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder AsTransient()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractType, m_implementationType, BindingLifetime.Transient, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>Reuses one closed instance per resolving container scope.</summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder AsScoped()
        {
            ValidateNotBound();
            m_provider = m_container.RegisterConfigured(
                m_contractType, m_implementationType, BindingLifetime.Scoped, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Exports this closed contract from a child container installed per requesting scope.
        /// The child binding chooses the exported service lifetime.
        /// </summary>
        /// <param name="install">Registers the contract inside the child container.</param>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder FromSubContainerResolve(Action<OnityContainer> install)
        {
            ValidateNotBound();

            if (m_implementationType != m_contractType)
            {
                throw new OnityBindingException("To and FromSubContainerResolve cannot be combined.");
            }

            m_provider = m_container.RegisterSubContainer(m_contractType, install, m_id, m_consumerType, m_replace);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Resolves this contract during container build. Not supported for open
        /// generic bindings: the closed type is unknown until resolve.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder NonLazy()
        {
            if (m_isBound == false)
            {
                throw new OnityBindingException(
                    $"Select a lifetime or {nameof(FromSubContainerResolve)} before {nameof(NonLazy)}.");
            }

            if (m_contractType.IsGenericTypeDefinition)
            {
                throw new OnityBindingException(
                    "NonLazy is not supported for open generic bindings; the closed type is unknown until resolve.");
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

        private void ValidateNotBound()
        {
            if (m_isBound)
            {
                throw new OnityBindingException("Configure the binding before selecting a lifetime.");
            }
        }
    }
}
