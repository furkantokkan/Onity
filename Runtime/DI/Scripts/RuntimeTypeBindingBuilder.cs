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

        internal RuntimeTypeBindingBuilder(OnityContainer container, Type contractType)
        {
            m_container = container;
            m_contractType = contractType;
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
            if (implementationType == null)
            {
                throw new OnityBindingException("Implementation type cannot be null.");
            }

            m_implementationType = implementationType;
            return this;
        }

        /// <summary>
        /// Registers this binding as singleton.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder AsSingle()
        {
            m_container.RegisterRuntime(m_contractType, m_implementationType, Lifetime.Singleton);
            m_isBound = true;
            return this;
        }

        /// <summary>
        /// Registers this binding as transient.
        /// </summary>
        /// <returns>Current builder instance.</returns>
        public RuntimeTypeBindingBuilder AsTransient()
        {
            m_container.RegisterRuntime(m_contractType, m_implementationType, Lifetime.Transient);
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
                    $"Call {nameof(AsSingle)} or {nameof(AsTransient)} before {nameof(NonLazy)}.");
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

            Type contractType = m_contractType;
            m_container.RegisterBuildCallback(resolver => resolver.Resolve(contractType));
            m_isNonLazyRegistered = true;
            return this;
        }
    }
}
