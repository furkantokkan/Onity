using System;
using System.Collections.Generic;

namespace Onity.Reactive
{
    /// <summary>
    /// Disposable collection utility.
    /// </summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> m_disposables;
        private bool m_isDisposed;

        /// <summary>
        /// Initializes a new collection.
        /// </summary>
        public CompositeDisposable()
        {
            m_disposables = new List<IDisposable>(16);
            m_isDisposed = false;
        }

        /// <summary>
        /// Gets the current item count.
        /// </summary>
        public int Count => m_disposables.Count;

        /// <summary>
        /// Adds a disposable.
        /// </summary>
        /// <param name="disposable">Disposable instance.</param>
        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (m_isDisposed)
            {
                disposable.Dispose();
                return;
            }

            m_disposables.Add(disposable);
        }

        /// <summary>
        /// Removes and disposes a disposable if present.
        /// </summary>
        /// <param name="disposable">Disposable instance.</param>
        /// <returns>True when removed; otherwise false.</returns>
        public bool Remove(IDisposable disposable)
        {
            if (disposable == null)
            {
                return false;
            }

            int index = m_disposables.IndexOf(disposable);

            if (index < 0)
            {
                return false;
            }

            m_disposables.RemoveAt(index);
            disposable.Dispose();
            return true;
        }

        /// <summary>
        /// Disposes all current items and clears collection.
        /// </summary>
        public void Clear()
        {
            for (int i = m_disposables.Count - 1; i >= 0; i--)
            {
                m_disposables[i].Dispose();
            }

            m_disposables.Clear();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            m_isDisposed = true;
            Clear();
        }
    }
}
