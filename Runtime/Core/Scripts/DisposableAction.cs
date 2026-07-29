using System;
using System.Threading;

namespace Onity.Core
{
    /// <summary>
    /// Disposable wrapper that executes a callback once.
    /// </summary>
    public sealed class DisposableAction : IDisposable
    {
        /// <summary>
        /// Empty disposable instance.
        /// </summary>
        public static readonly IDisposable Empty = new DisposableAction(null);

        private Action m_disposeAction;

        /// <summary>
        /// Initializes a new disposable callback wrapper.
        /// </summary>
        /// <param name="disposeAction">Callback invoked when disposed.</param>
        public DisposableAction(Action disposeAction)
        {
            m_disposeAction = disposeAction;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Action action = Interlocked.Exchange(ref m_disposeAction, null);
            action?.Invoke();
        }
    }
}
