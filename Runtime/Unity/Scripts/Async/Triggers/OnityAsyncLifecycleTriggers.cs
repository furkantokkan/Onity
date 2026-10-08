using System.Threading;
using Onity.Core;
using UnityEngine;

namespace Onity.Unity.Async.Triggers
{
    /// <summary>Waits for the next <c>OnEnable</c> (UniTask <c>IAsyncOnEnableHandler</c> parity).</summary>
    public interface IOnityAsyncOnEnableHandler
    {
        /// <summary>Waits for the next <c>OnEnable</c> message. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the trigger is destroyed.</returns>
        OnityTask OnEnableAsync();
    }

    /// <summary>Waits for the next <c>OnDisable</c> (UniTask <c>IAsyncOnDisableHandler</c> parity).</summary>
    public interface IOnityAsyncOnDisableHandler
    {
        /// <summary>Waits for the next <c>OnDisable</c> message. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the trigger is destroyed.</returns>
        OnityTask OnDisableAsync();
    }

    public sealed partial class OnityAsyncTriggerHandler<T> : IOnityAsyncOnEnableHandler, IOnityAsyncOnDisableHandler
    {
        OnityTask IOnityAsyncOnEnableHandler.OnEnableAsync()
        {
            return WaitAsync();
        }

        OnityTask IOnityAsyncOnDisableHandler.OnDisableAsync()
        {
            return WaitAsync();
        }
    }

    /// <summary>
    /// Trigger for this GameObject's <c>Awake</c> (UniTask <c>AsyncAwakeTrigger</c> parity).
    /// Main thread only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityAsyncAwakeTrigger : OnityAsyncTriggerBase<Unit>
    {
        /// <summary>
        /// Waits until this component has been awakened. Returns a completed task when
        /// <c>Awake</c> already ran (adding the trigger to an active GameObject awakes it at once).
        /// On an inactive GameObject the task completes when the object is activated (deviation
        /// from UniTask 2.5.11, which only cancels it on destroy), and is canceled when the object is
        /// destroyed first. Main thread only.
        /// </summary>
        /// <returns>A single-consumption task.</returns>
        public OnityTask AwakeAsync()
        {
            if (CalledAwake)
            {
                return OnityTask.CompletedTask;
            }

            return ((IOnityAsyncOneShotTrigger)new OnityAsyncTriggerHandler<Unit>(this, true)).OneShotAsync();
        }

        private protected override void OnAwakeCalled()
        {
            RaiseEvent(Unit.Default);
        }
    }

    /// <summary>
    /// Trigger for this GameObject's <c>Start</c> (UniTask <c>AsyncStartTrigger</c> parity).
    /// Main thread only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityAsyncStartTrigger : OnityAsyncTriggerBase<Unit>
    {
        private bool m_called;

        /// <summary>
        /// Waits for this component's <c>Start</c>. Returns a completed task when <c>Start</c>
        /// already ran; canceled when the object is destroyed before <c>Start</c>. Main thread only.
        /// </summary>
        /// <returns>A single-consumption task.</returns>
        public OnityTask StartAsync()
        {
            if (m_called)
            {
                return OnityTask.CompletedTask;
            }

            return ((IOnityAsyncOneShotTrigger)new OnityAsyncTriggerHandler<Unit>(this, true)).OneShotAsync();
        }

        private void Start()
        {
            m_called = true;
            RaiseEvent(Unit.Default);
        }
    }

    /// <summary>
    /// Trigger for <c>OnEnable</c> messages (UniTask <c>AsyncEnableTrigger</c> parity). The
    /// component itself is an <see cref="IOnityAsyncEnumerable{T}"/> of enable events.
    /// Main thread only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityAsyncEnableTrigger : OnityAsyncTriggerBase<Unit>
    {
        /// <summary>Creates a reusable handler; dispose it to unregister. Main thread only.</summary>
        /// <returns>A handler whose each <c>OnEnableAsync()</c> waits for the next enable.</returns>
        public IOnityAsyncOnEnableHandler GetOnEnableAsyncHandler()
        {
            return new OnityAsyncTriggerHandler<Unit>(this, false);
        }

        /// <summary>Creates a reusable handler bound to a token. Main thread only.</summary>
        /// <param name="cancellationToken">Cancels the pending wait and unregisters the handler.</param>
        /// <returns>A handler whose each <c>OnEnableAsync()</c> waits for the next enable.</returns>
        public IOnityAsyncOnEnableHandler GetOnEnableAsyncHandler(CancellationToken cancellationToken)
        {
            return new OnityAsyncTriggerHandler<Unit>(this, cancellationToken, false);
        }

        /// <summary>Waits for the next <c>OnEnable</c>. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the trigger is destroyed.</returns>
        public OnityTask OnEnableAsync()
        {
            return ((IOnityAsyncOnEnableHandler)new OnityAsyncTriggerHandler<Unit>(this, true)).OnEnableAsync();
        }

        /// <summary>Waits for the next <c>OnEnable</c>. Main thread only.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A single-consumption task; canceled by the token or when the trigger is destroyed.</returns>
        public OnityTask OnEnableAsync(CancellationToken cancellationToken)
        {
            return ((IOnityAsyncOnEnableHandler)new OnityAsyncTriggerHandler<Unit>(this, cancellationToken, true))
                .OnEnableAsync();
        }

        private void OnEnable()
        {
            RaiseEvent(Unit.Default);
        }
    }

    /// <summary>
    /// Trigger for <c>OnDisable</c> messages (UniTask <c>AsyncDisableTrigger</c> parity). The
    /// component itself is an <see cref="IOnityAsyncEnumerable{T}"/> of disable events.
    /// Main thread only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityAsyncDisableTrigger : OnityAsyncTriggerBase<Unit>
    {
        /// <summary>Creates a reusable handler; dispose it to unregister. Main thread only.</summary>
        /// <returns>A handler whose each <c>OnDisableAsync()</c> waits for the next disable.</returns>
        public IOnityAsyncOnDisableHandler GetOnDisableAsyncHandler()
        {
            return new OnityAsyncTriggerHandler<Unit>(this, false);
        }

        /// <summary>Creates a reusable handler bound to a token. Main thread only.</summary>
        /// <param name="cancellationToken">Cancels the pending wait and unregisters the handler.</param>
        /// <returns>A handler whose each <c>OnDisableAsync()</c> waits for the next disable.</returns>
        public IOnityAsyncOnDisableHandler GetOnDisableAsyncHandler(CancellationToken cancellationToken)
        {
            return new OnityAsyncTriggerHandler<Unit>(this, cancellationToken, false);
        }

        /// <summary>Waits for the next <c>OnDisable</c>. Main thread only.</summary>
        /// <returns>A single-consumption task; canceled when the trigger is destroyed.</returns>
        public OnityTask OnDisableAsync()
        {
            return ((IOnityAsyncOnDisableHandler)new OnityAsyncTriggerHandler<Unit>(this, true)).OnDisableAsync();
        }

        /// <summary>Waits for the next <c>OnDisable</c>. Main thread only.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A single-consumption task; canceled by the token or when the trigger is destroyed.</returns>
        public OnityTask OnDisableAsync(CancellationToken cancellationToken)
        {
            return ((IOnityAsyncOnDisableHandler)new OnityAsyncTriggerHandler<Unit>(this, cancellationToken, true))
                .OnDisableAsync();
        }

        private void OnDisable()
        {
            RaiseEvent(Unit.Default);
        }
    }
}
