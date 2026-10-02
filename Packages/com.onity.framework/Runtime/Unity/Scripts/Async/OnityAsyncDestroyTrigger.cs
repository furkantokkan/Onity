using System;
using System.Threading;
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Hidden component that cancels a lazily created token exactly once when its GameObject is
    /// destroyed (UniTask <c>AsyncDestroyTrigger</c> parity). One instance per GameObject backs both
    /// <c>GetCancellationTokenOnDestroy()</c> (GameObject and non-MonoBehaviour component) and
    /// <c>OnDestroyAsync()</c>. Get it with <c>GetAsyncDestroyTrigger()</c> from
    /// <c>Onity.Unity.Async.Triggers</c>. Main thread only. Unity sends <c>OnDestroy</c> only in
    /// Play mode for this component; in Edit mode neither the token nor <see cref="OnDestroyAsync"/>
    /// is raised by a destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityAsyncDestroyTrigger : MonoBehaviour
    {
        private static readonly Action<object> s_completeOnDestroy = CompleteOnDestroy;

        private bool m_awakeCalled;
        private bool m_called;
        private CancellationTokenSource m_cancellationTokenSource;
        private IDisposable m_awakeMonitor;

        /// <summary>
        /// Gets the token that is canceled when this component is destroyed. After destruction the
        /// returned token is already canceled. Main thread only.
        /// </summary>
        public CancellationToken CancellationToken
        {
            get
            {
                if (m_called)
                {
                    return new CancellationToken(true);
                }

                if (m_cancellationTokenSource == null)
                {
                    m_cancellationTokenSource = new CancellationTokenSource();

                    if (!m_awakeCalled && Application.isPlaying)
                    {
                        StartAwakeMonitor();
                    }
                }

                return m_cancellationTokenSource.Token;
            }
        }

        /// <summary>
        /// Waits until this component is destroyed. Returns a completed task when it was already
        /// destroyed. The wait shares the destroy <see cref="CancellationToken"/>, so it also completes
        /// for a never-awakened component destroyed in Play mode. Main thread only.
        /// </summary>
        /// <returns>A single-consumption task that completes successfully on destroy.</returns>
        public OnityTask OnDestroyAsync()
        {
            if (m_called)
            {
                return OnityTask.CompletedTask;
            }

            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;

            // OnDestroy cancels the token; registration without ExecutionContext capture as UniTask.
            CancellationToken token = CancellationToken;
            if (ExecutionContext.IsFlowSuppressed())
            {
                token.Register(s_completeOnDestroy, source);
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    token.Register(s_completeOnDestroy, source);
                }
            }

            return task;
        }

        /// <summary>
        /// Gets or adds the trigger on the given live GameObject.
        /// </summary>
        /// <param name="gameObject">Live GameObject.</param>
        /// <returns>The single trigger on the GameObject.</returns>
        internal static OnityAsyncDestroyTrigger GetOrAdd(GameObject gameObject)
        {
            if (!gameObject.TryGetComponent(out OnityAsyncDestroyTrigger trigger))
            {
                trigger = gameObject.AddComponent<OnityAsyncDestroyTrigger>();
                trigger.hideFlags |= HideFlags.HideInInspector;
            }

            return trigger;
        }

        private static void CompleteOnDestroy(object state)
        {
            ((OnityAutoResetTaskCompletionSource)state).TrySetResult();
        }

        private void Awake()
        {
            m_awakeCalled = true;
        }

        private void OnDestroy()
        {
            RaiseDestroyed();
        }

        private void StartAwakeMonitor()
        {
            // Unity never calls OnDestroy on a component that was never awakened (inactive object
            // destroyed before activation), so poll for the destroyed instance until Awake runs.
            m_awakeMonitor = OnityUnityObservable.EveryUpdate().Subscribe(_ => MonitorAwake());
        }

        private void MonitorAwake()
        {
            if (m_called || m_awakeCalled)
            {
                StopAwakeMonitor();
                return;
            }

            if (this == null)
            {
                RaiseDestroyed();
            }
        }

        private void StopAwakeMonitor()
        {
            IDisposable monitor = m_awakeMonitor;
            m_awakeMonitor = null;
            monitor?.Dispose();
        }

        private void RaiseDestroyed()
        {
            if (m_called)
            {
                return;
            }

            m_called = true;
            StopAwakeMonitor();

            CancellationTokenSource source = m_cancellationTokenSource;
            m_cancellationTokenSource = null;

            if (source == null)
            {
                return;
            }

            try
            {
                source.Cancel();
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
