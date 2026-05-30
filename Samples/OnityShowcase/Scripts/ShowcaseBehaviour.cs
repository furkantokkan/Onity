using Onity.Reactive;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Thin MonoBehaviour base for showcase views. Provides a <see cref="CompositeDisposable"/>
    /// that is disposed on destroy — the local stand-in for the full package's
    /// <c>AddTo(this)</c> Component lifetime helper, which lives in <c>Onity.Unity.Reactive</c>
    /// and is not embedded in the Example Game. Subclasses add subscriptions with
    /// <c>.AddTo(Subscriptions)</c> and put no domain logic here.
    /// </summary>
    public abstract class ShowcaseBehaviour : MonoBehaviour
    {
        private readonly CompositeDisposable m_subscriptions = new CompositeDisposable();

        /// <summary>
        /// Lifetime bag for this behaviour's subscriptions; disposed automatically on destroy.
        /// </summary>
        protected CompositeDisposable Subscriptions => m_subscriptions;

        /// <summary>
        /// Called by <see cref="OnityShowcaseContext"/> after the container has injected this
        /// behaviour and finished building. Override to wire reactive subscriptions here, where
        /// every injected dependency is guaranteed to be available.
        /// </summary>
        public virtual void OnInjected()
        {
        }

        /// <summary>
        /// Disposes all subscriptions registered through <see cref="Subscriptions"/>.
        /// </summary>
        protected virtual void OnDestroy()
        {
            m_subscriptions.Dispose();
        }
    }
}
