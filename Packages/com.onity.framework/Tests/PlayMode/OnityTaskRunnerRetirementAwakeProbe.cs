using System;
using Onity.Unity.Async;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskRunnerRetirementAwakeProbe : MonoBehaviour
    {
        public bool RegisteredInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool ShortCompleted;
        public int CanceledCount;
        public bool UnawaitedCanceled;
        public bool ClosingRejected;
        public string Error;

        private OnityTask m_unawaited;
#if UNITY_EDITOR
        private static bool s_enteredPlay;

        static OnityTaskRunnerRetirementAwakeProbe()
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
                var shortWait = OnityTask.NextFrame();
                shortWait.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        shortWait.GetAwaiter().GetResult();
                        ShortCompleted = true;
                    }
                    catch (Exception exception)
                    {
                        Error = exception.ToString();
                    }
                });
                ObserveRetirement(OnityTask.DelayFrames(int.MaxValue));
                ObserveRetirement(OnityTask.WaitUntil(() => false));
                m_unawaited = OnityTask.WaitWhile(() => true);
                RegisteredInAwake = true;
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }

        private void ObserveRetirement(OnityTask wait)
        {
            wait.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    wait.GetAwaiter().GetResult();
                    Error = "Long legacy wait completed successfully.";
                }
                catch (OperationCanceledException)
                {
                    CanceledCount++;
                    try
                    {
                        OnityTask.WaitUntil(() => false);
                        Error = "Closing session accepted a replacement legacy wait.";
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
            });
        }

        public void ObserveUnawaitedAfterExit()
        {
            try
            {
                UnawaitedCanceled = m_unawaited.IsCanceled;
                m_unawaited.GetAwaiter().GetResult();
                Error = "Unawaited legacy wait completed successfully.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }
    }
}
