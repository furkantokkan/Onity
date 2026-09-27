using System;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    /// <summary>Runtime lifecycle probe; no scene asset or native allocation is required.</summary>
    public sealed class OnityTaskPlayerLoopAwakeProbe : MonoBehaviour
    {
        public bool RegisteredInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool NodesPresentInAwake;
        public bool ShortCompleted;
        public bool LongCanceled;
        public bool UnawaitedCanceled;
        public string Error;

        private OnityTask m_unawaited;
#if UNITY_EDITOR
        private static bool s_enteredPlay;

        static OnityTaskPlayerLoopAwakeProbe()
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
                NodesPresentInAwake = CountOwned(PlayerLoop.GetCurrentPlayerLoop()) == 3;
                var shortWait = OnityTask.Yield(OnityPlayerLoopTiming.Update);
                var longWait = OnityTask.DelayFrames(int.MaxValue, OnityPlayerLoopTiming.Update, default);
                m_unawaited = OnityTask.DelayFrames(int.MaxValue, OnityPlayerLoopTiming.LateUpdate, default);
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
                longWait.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        longWait.GetAwaiter().GetResult();
                        Error = "Long session wait completed successfully.";
                    }
                    catch (OperationCanceledException)
                    {
                        LongCanceled = true;
                    }
                    catch (Exception exception)
                    {
                        Error = exception.ToString();
                    }
                });
                RegisteredInAwake = true;
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }

        public void ObserveUnawaitedAfterExit()
        {
            try
            {
                UnawaitedCanceled = m_unawaited.IsCanceled;
                m_unawaited.GetAwaiter().GetResult();
                Error = "Unawaited session wait completed successfully.";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }

        public static int CountOwned(PlayerLoopSystem loop)
        {
            int count = loop.type != null && loop.type.DeclaringType == typeof(OnityTaskPlayerLoop) ? 1 : 0;
            if (loop.subSystemList != null)
            {
                foreach (var child in loop.subSystemList)
                {
                    count += CountOwned(child);
                }
            }
            return count;
        }
    }
}
