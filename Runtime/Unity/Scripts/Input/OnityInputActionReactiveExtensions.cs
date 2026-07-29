#if ENABLE_INPUT_SYSTEM
using System;
using System.Threading;
using Onity.Reactive;
using UnityEngine.InputSystem;

namespace Onity.Unity.Input
{
    /// <summary>
    /// Reactive extensions for InputAction event streams.
    /// </summary>
    public static class OnityInputActionReactiveExtensions
    {
        /// <summary>
        /// Returns a stream for InputAction started callbacks.
        /// </summary>
        /// <param name="action">Target input action.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Started callback stream.</returns>
        public static IOnityObservable<InputAction.CallbackContext> StartedAsObservable(
            this InputAction action,
            CancellationToken cancellationToken = default)
        {
            return CreateObservable(
                action,
                handler => action.started += handler,
                handler => action.started -= handler,
                cancellationToken);
        }

        /// <summary>
        /// Returns a stream for InputAction performed callbacks.
        /// </summary>
        /// <param name="action">Target input action.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Performed callback stream.</returns>
        public static IOnityObservable<InputAction.CallbackContext> PerformedAsObservable(
            this InputAction action,
            CancellationToken cancellationToken = default)
        {
            return CreateObservable(
                action,
                handler => action.performed += handler,
                handler => action.performed -= handler,
                cancellationToken);
        }

        /// <summary>
        /// Returns a stream for InputAction canceled callbacks.
        /// </summary>
        /// <param name="action">Target input action.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Canceled callback stream.</returns>
        public static IOnityObservable<InputAction.CallbackContext> CanceledAsObservable(
            this InputAction action,
            CancellationToken cancellationToken = default)
        {
            return CreateObservable(
                action,
                handler => action.canceled += handler,
                handler => action.canceled -= handler,
                cancellationToken);
        }

        private static IOnityObservable<InputAction.CallbackContext> CreateObservable(
            InputAction action,
            Action<Action<InputAction.CallbackContext>> addHandler,
            Action<Action<InputAction.CallbackContext>> removeHandler,
            CancellationToken cancellationToken)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            IOnityObservable<InputAction.CallbackContext> stream = OnityObservable.FromEvent(
                addHandler,
                removeHandler);

            if (cancellationToken.CanBeCanceled == false)
            {
                return stream;
            }

            return stream.TakeUntilCancellation(cancellationToken);
        }
    }
}
#endif
