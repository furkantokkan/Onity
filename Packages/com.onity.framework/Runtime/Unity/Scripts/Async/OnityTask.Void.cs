using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Factories that turn <c>async OnityTaskVoid</c> methods into fire-and-forget calls and
    /// delegates. The async method starts synchronously on the calling thread, so call these from
    /// the Unity main thread when the method touches Unity objects.
    /// </summary>
    public readonly partial struct OnityTask
    {
        /// <summary>
        /// Runs a fire-and-forget async method.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        public static void Void(Func<OnityTaskVoid> asyncAction)
        {
            asyncAction().Forget();
        }

        /// <summary>
        /// Runs a fire-and-forget async method that takes a cancellation token.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        public static void Void(Func<CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            asyncAction(cancellationToken).Forget();
        }

        /// <summary>
        /// Runs a fire-and-forget async method that takes a state argument.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="state">State passed to the method.</param>
        public static void Void<T>(Func<T, OnityTaskVoid> asyncAction, T state)
        {
            asyncAction(state).Forget();
        }

        /// <summary>
        /// Creates an <see cref="System.Action"/> that starts an async method each time it is invoked.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static Action Action(Func<OnityTaskVoid> asyncAction)
        {
            return () => asyncAction().Forget();
        }

        /// <summary>
        /// Creates an <see cref="System.Action"/> that starts an async method with a cancellation token.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static Action Action(Func<CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return () => asyncAction(cancellationToken).Forget();
        }

        /// <summary>
        /// Creates an <see cref="System.Action"/> that starts an async method with a state argument.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="state">State passed to the method.</param>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static Action Action<T>(T state, Func<T, OnityTaskVoid> asyncAction)
        {
            return () => asyncAction(state).Forget();
        }

        /// <summary>
        /// Creates a <see cref="UnityEngine.Events.UnityAction"/> that starts an async method each time it is invoked.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction UnityAction(Func<OnityTaskVoid> asyncAction)
        {
            return () => asyncAction().Forget();
        }

        /// <summary>
        /// Creates a <see cref="UnityEngine.Events.UnityAction"/> that starts an async method with a cancellation token.
        /// </summary>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction UnityAction(Func<CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return () => asyncAction(cancellationToken).Forget();
        }

        /// <summary>
        /// Creates a <see cref="UnityEngine.Events.UnityAction"/> that starts an async method with a state argument.
        /// </summary>
        /// <typeparam name="T">State type.</typeparam>
        /// <param name="state">State passed to the method.</param>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction UnityAction<T>(T state, Func<T, OnityTaskVoid> asyncAction)
        {
            return () => asyncAction(state).Forget();
        }

        /// <summary>
        /// Creates a one-argument <see cref="UnityEngine.Events.UnityAction{T0}"/> that starts an async method.
        /// </summary>
        /// <typeparam name="T">Argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T> UnityAction<T>(Func<T, OnityTaskVoid> asyncAction)
        {
            return arg => asyncAction(arg).Forget();
        }

        /// <summary>
        /// Creates a two-argument <see cref="UnityEngine.Events.UnityAction{T0, T1}"/> that starts an async method.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1> UnityAction<T0, T1>(Func<T0, T1, OnityTaskVoid> asyncAction)
        {
            return (arg0, arg1) => asyncAction(arg0, arg1).Forget();
        }

        /// <summary>
        /// Creates a three-argument <see cref="UnityEngine.Events.UnityAction{T0, T1, T2}"/> that starts an async method.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <typeparam name="T2">Third argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1, T2> UnityAction<T0, T1, T2>(Func<T0, T1, T2, OnityTaskVoid> asyncAction)
        {
            return (arg0, arg1, arg2) => asyncAction(arg0, arg1, arg2).Forget();
        }

        /// <summary>
        /// Creates a four-argument <see cref="UnityEngine.Events.UnityAction{T0, T1, T2, T3}"/> that starts an async method.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <typeparam name="T2">Third argument type.</typeparam>
        /// <typeparam name="T3">Fourth argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1, T2, T3> UnityAction<T0, T1, T2, T3>(Func<T0, T1, T2, T3, OnityTaskVoid> asyncAction)
        {
            return (arg0, arg1, arg2, arg3) => asyncAction(arg0, arg1, arg2, arg3).Forget();
        }

        /// <summary>
        /// Creates a one-argument <see cref="UnityEngine.Events.UnityAction{T0}"/> that starts an async method
        /// with a cancellation token.
        /// </summary>
        /// <typeparam name="T">Argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T> UnityAction<T>(Func<T, CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return arg => asyncAction(arg, cancellationToken).Forget();
        }

        /// <summary>
        /// Creates a two-argument <see cref="UnityEngine.Events.UnityAction{T0, T1}"/> that starts an async method
        /// with a cancellation token.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1> UnityAction<T0, T1>(Func<T0, T1, CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return (arg0, arg1) => asyncAction(arg0, arg1, cancellationToken).Forget();
        }

        /// <summary>
        /// Creates a three-argument <see cref="UnityEngine.Events.UnityAction{T0, T1, T2}"/> that starts an async method
        /// with a cancellation token.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <typeparam name="T2">Third argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1, T2> UnityAction<T0, T1, T2>(Func<T0, T1, T2, CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return (arg0, arg1, arg2) => asyncAction(arg0, arg1, arg2, cancellationToken).Forget();
        }

        /// <summary>
        /// Creates a four-argument <see cref="UnityEngine.Events.UnityAction{T0, T1, T2, T3}"/> that starts an async method
        /// with a cancellation token.
        /// </summary>
        /// <typeparam name="T0">First argument type.</typeparam>
        /// <typeparam name="T1">Second argument type.</typeparam>
        /// <typeparam name="T2">Third argument type.</typeparam>
        /// <typeparam name="T3">Fourth argument type.</typeparam>
        /// <param name="asyncAction">Async method to start.</param>
        /// <param name="cancellationToken">Token passed to the method.</param>
        /// <returns>Delegate that starts the method.</returns>
        public static UnityEngine.Events.UnityAction<T0, T1, T2, T3> UnityAction<T0, T1, T2, T3>(Func<T0, T1, T2, T3, CancellationToken, OnityTaskVoid> asyncAction, CancellationToken cancellationToken)
        {
            return (arg0, arg1, arg2, arg3) => asyncAction(arg0, arg1, arg2, arg3, cancellationToken).Forget();
        }
    }
}
