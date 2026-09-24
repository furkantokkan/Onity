using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Global hook for exceptions thrown by reactive observers.
    /// </summary>
    /// <remarks>
    /// When a subscriber callback throws during notification, the Onity reactive core
    /// isolates the failure so sibling subscribers still receive the value, then routes
    /// the caught exception here. The default handler is a no-op; a host can assign
    /// <see cref="Handler" /> to log or report observer faults. Assigning <c>null</c>
    /// restores the no-op default.
    /// </remarks>
    public static class OnityObservableExceptionHandler
    {
        private static readonly Action<Exception> s_noOp = static _ => { };

        private static Action<Exception> s_handler = s_noOp;

        /// <summary>
        /// Gets or sets the handler invoked for caught observer exceptions.
        /// </summary>
        /// <remarks>Setting this to <c>null</c> restores the no-op default handler.</remarks>
        public static Action<Exception> Handler
        {
            get => s_handler;
            set => s_handler = value ?? s_noOp;
        }

        /// <summary>
        /// Routes a caught observer exception to the current handler.
        /// </summary>
        /// <param name="exception">Exception thrown by an observer callback.</param>
        /// <remarks>
        /// Exceptions thrown by the handler itself are swallowed so that one faulty
        /// handler cannot break notification of remaining subscribers.
        /// </remarks>
        public static void Publish(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                s_handler(exception);
            }
            catch
            {
                // A throwing handler must never break subscriber notification.
            }
        }
    }
}
