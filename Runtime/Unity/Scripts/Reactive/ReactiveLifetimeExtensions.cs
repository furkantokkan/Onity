using System;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Unity lifecycle helpers for disposable subscriptions.
    /// </summary>
    public static class ReactiveLifetimeExtensions
    {
        /// <summary>
        /// Disposes the subscription when owner is disabled.
        /// </summary>
        /// <param name="disposable">Subscription token.</param>
        /// <param name="owner">Owner behaviour.</param>
        /// <returns>Same disposable for fluent usage.</returns>
        public static IDisposable TakeUntilDisable(this IDisposable disposable, Behaviour owner)
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            OnityLifetimeNotifier notifier = GetOrAddNotifier(owner.gameObject);
            notifier.RegisterOnDisable(disposable);
            return disposable;
        }

        /// <summary>
        /// Disposes the subscription when owner is destroyed.
        /// </summary>
        /// <param name="disposable">Subscription token.</param>
        /// <param name="owner">Owner component.</param>
        /// <returns>Same disposable for fluent usage.</returns>
        public static IDisposable TakeUntilDestroy(this IDisposable disposable, Component owner)
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            OnityLifetimeNotifier notifier = GetOrAddNotifier(owner.gameObject);
            notifier.RegisterOnDestroy(disposable);
            return disposable;
        }

        /// <summary>
        /// Adds disposable to owner lifetime and disposes it on destroy.
        /// </summary>
        /// <param name="disposable">Subscription token.</param>
        /// <param name="owner">Owner component.</param>
        /// <returns>Same disposable for fluent usage.</returns>
        public static IDisposable AddTo(this IDisposable disposable, Component owner)
        {
            return disposable.TakeUntilDestroy(owner);
        }

        private static OnityLifetimeNotifier GetOrAddNotifier(GameObject owner)
        {
            if (owner.TryGetComponent(out OnityLifetimeNotifier notifier))
            {
                return notifier;
            }

            return owner.AddComponent<OnityLifetimeNotifier>();
        }
    }
}
