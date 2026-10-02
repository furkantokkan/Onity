using System;
using Onity.Unity.Async;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskTimeoutAwakeProbe : MonoBehaviour
    {
        public bool RegisteredInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool ClosingRejected;
        public bool ProducersWerePending;
        public int CanceledCount;
        public string Error;

        private readonly OnityTaskCompletionSource<int>[] m_typed =
        {
            new OnityTaskCompletionSource<int>(), new OnityTaskCompletionSource<int>()
        };
        private readonly OnityTaskCompletionSource[] m_plain =
        {
            new OnityTaskCompletionSource(), new OnityTaskCompletionSource()
        };
        private OnityTask m_unawaitedPlain;
        private OnityTask<(bool isTimeout, int result)> m_unawaitedFlag;
#if UNITY_EDITOR
        private static bool s_enteredPlay;

        static OnityTaskTimeoutAwakeProbe()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    s_enteredPlay = true;
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    s_enteredPlay = false;
                }
            };
        }
#endif

        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }
#if UNITY_EDITOR
            AwakeBeforeEnteredPlay = !s_enteredPlay;
#else
            AwakeBeforeEnteredPlay = true;
#endif
            try
            {
                var observedTyped = m_typed[0].Task.Timeout(1000f);
                var observedFlag = m_plain[0].Task.TimeoutWithoutException(1000f);
                m_unawaitedPlain = m_plain[1].Task.Timeout(1000f);
                m_unawaitedFlag = m_typed[1].Task.TimeoutWithoutException(1000f);
                observedTyped.GetAwaiter().UnsafeOnCompleted(() => ObserveCancellation(() => observedTyped.GetAwaiter().GetResult()));
                observedFlag.GetAwaiter().UnsafeOnCompleted(() => ObserveCancellation(() => observedFlag.GetAwaiter().GetResult()));
                RegisteredInAwake = true;
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }

        private void ObserveCancellation(Action consume)
        {
            try
            {
                consume();
                Error = "Session-ended timeout wrapper succeeded.";
            }
            catch (OperationCanceledException)
            {
                CanceledCount++;
                try
                {
                    m_typed[1].Task.Timeout(1000f);
                    Error = "Closing timer session accepted a new timeout.";
                }
                catch (InvalidOperationException)
                {
                    ClosingRejected = true;
                }
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }

        public void ObserveAfterExit()
        {
            ObserveCancellation(() => m_unawaitedPlain.GetAwaiter().GetResult());
            ObserveCancellation(() => m_unawaitedFlag.GetAwaiter().GetResult());
            ProducersWerePending = !m_typed[0].Task.IsCompleted && !m_typed[1].Task.IsCompleted
                && !m_plain[0].Task.IsCompleted && !m_plain[1].Task.IsCompleted;
            CompleteProducers();
        }

        public void CompleteProducers()
        {
            foreach (var source in m_typed)
            {
                source.TrySetResult(42);
            }
            foreach (var source in m_plain)
            {
                source.TrySetResult();
            }
        }
    }
}
