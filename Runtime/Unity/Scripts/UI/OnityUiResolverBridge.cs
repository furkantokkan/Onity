using System;
using Onity.DI;

namespace Onity.Unity.UI
{
    /// <summary>
    /// Connects an Onity resolver scope to UI service locator and presenter factory.
    /// </summary>
    public sealed class OnityUiResolverBridge : IDisposable
    {
        private readonly IDisposable m_resolverScope;
        private readonly Func<Type, IOnityUiPresenter> m_previousFactory;
        private readonly Func<Type, IOnityUiPresenter> m_presenterFactory;
        private readonly Func<Type, object> m_resolver;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes UI resolver bridge for the provided resolver scope.
        /// </summary>
        /// <param name="resolver">Resolver used by UI service locator.</param>
        public OnityUiResolverBridge(IResolver resolver)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            m_resolver = type =>
            {
                if (type == null)
                {
                    return null;
                }

                return resolver.TryResolve(type, out object service) ? service : null;
            };

            m_resolverScope = OnityUiServiceLocator.PushResolverScope(m_resolver);
            m_previousFactory = OnityUiPresenterFactory.CustomFactory;
            m_presenterFactory = ResolvePresenter;
            OnityUiPresenterFactory.CustomFactory = m_presenterFactory;
            m_isDisposed = false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            m_resolverScope.Dispose();

            if (ReferenceEquals(OnityUiPresenterFactory.CustomFactory, m_presenterFactory))
            {
                OnityUiPresenterFactory.CustomFactory = m_previousFactory;
            }
        }

        private IOnityUiPresenter ResolvePresenter(Type presenterType)
        {
            if (m_resolver(presenterType) is IOnityUiPresenter presenter)
            {
                return presenter;
            }

            if (m_previousFactory != null)
            {
                return m_previousFactory(presenterType);
            }

            return null;
        }
    }
}
