using System;
using System.Collections.Generic;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.LowLevel;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    /// <summary>Runtime-only probe for a real reload-disabled EveryUpdate session.</summary>
    public sealed class OnityAsyncEnumerableAdapterSessionProbe : MonoBehaviour
    {
        public bool RegisteredInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool FirstCompleted;
        public bool PendingBeforeExit;
        public bool ExitCanceled;
        public string Error;
        private IOnityAsyncEnumerator<Unit> m_iterator;
        private bool m_removedUpdate;
#if UNITY_EDITOR
        private static bool s_enteredPlay;
        static OnityAsyncEnumerableAdapterSessionProbe()
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
                m_iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator();
                var first = m_iterator.MoveNextAsync();
                first.GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        FirstCompleted = first.GetAwaiter().GetResult();
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

        public void ArmExitWait()
        {
            var wait = m_iterator.MoveNextAsync();
            PendingBeforeExit = !wait.IsCompleted;
            wait.GetAwaiter().UnsafeOnCompleted(() =>
            {
                try
                {
                    wait.GetAwaiter().GetResult();
                    Error = "Exit wait completed instead of being canceled by the session.";
                }
                catch (OperationCanceledException)
                {
                    ExitCanceled = wait.IsCanceled;
                }
                catch (Exception exception)
                {
                    Error = exception.ToString();
                }
            });
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            if (RemoveUpdate(ref loop) != 1)
            {
                throw new InvalidOperationException("Expected exactly one current Onity Update marker.");
            }
            PlayerLoop.SetPlayerLoop(loop);
            m_removedUpdate = true;
        }

        public void RepairAfterFailure()
        {
            if (m_removedUpdate && Application.isPlaying)
            {
                OnityTaskPlayerLoop.Initialize();
                m_removedUpdate = false;
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (m_iterator != null)
                {
                    var disposal = m_iterator.DisposeAsync();
                    if (disposal.IsCompleted)
                    {
                        disposal.GetAwaiter().GetResult();
                    }
                }
            }
            catch (Exception exception)
            {
                Error = Error ?? exception.ToString();
            }
        }

        private static int RemoveUpdate(ref PlayerLoopSystem loop)
        {
            if (loop.subSystemList == null)
            {
                return 0;
            }
            int removed = 0;
            var kept = new List<PlayerLoopSystem>();
            foreach (PlayerLoopSystem original in loop.subSystemList)
            {
                if (original.type?.FullName == "Onity.Unity.Async.OnityTaskPlayerLoop+UpdateMarker")
                {
                    removed++;
                    continue;
                }
                var child = original;
                removed += RemoveUpdate(ref child);
                kept.Add(child);
            }
            loop.subSystemList = kept.ToArray();
            return removed;
        }
    }
}
