using System;
using System.Collections.Generic;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Internal helper component for disposal on Unity lifecycle events.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class OnityLifetimeNotifier : MonoBehaviour
    {
        private readonly List<IDisposable> m_onDisableDisposables = new List<IDisposable>(8);
        private readonly List<IDisposable> m_onDestroyDisposables = new List<IDisposable>(8);

        public void RegisterOnDisable(IDisposable disposable)
        {
            if (disposable != null)
            {
                m_onDisableDisposables.Add(disposable);
            }
        }

        public void RegisterOnDestroy(IDisposable disposable)
        {
            if (disposable != null)
            {
                m_onDestroyDisposables.Add(disposable);
            }
        }

        private void OnDisable()
        {
            DisposeAndClear(m_onDisableDisposables);
        }

        private void OnDestroy()
        {
            DisposeAndClear(m_onDisableDisposables);
            DisposeAndClear(m_onDestroyDisposables);
        }

        private static void DisposeAndClear(List<IDisposable> disposables)
        {
            for (int i = disposables.Count - 1; i >= 0; i--)
            {
                disposables[i].Dispose();
            }

            disposables.Clear();
        }
    }
}
