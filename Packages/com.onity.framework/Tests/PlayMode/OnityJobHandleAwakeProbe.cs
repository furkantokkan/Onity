using System;
using Onity.Unity.Async;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    /// <summary>Runtime test component that registers finite native work before EnteredPlayMode.</summary>
    public sealed class OnityJobHandleAwakeProbe : MonoBehaviour
    {
        public bool RegisteredInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool Completed;
        public string Error;
        public int Result;
        public int Expected;

        private NativeArray<int> m_output;
        private JobHandle m_handle;
        private OnityTask m_task;
#if UNITY_EDITOR
        private static bool s_enteredPlay;

        static OnityJobHandleAwakeProbe()
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
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    int iterations = 200000 << (attempt * 2);
                    m_output = new NativeArray<int>(1, Allocator.Persistent);
#if UNITY_EDITOR
                    AssemblyReloadEvents.beforeAssemblyReload += DisposeWork;
#endif
                    Expected = Calculate(iterations);
                    m_handle = new ComputeJob { Output = m_output, Iterations = iterations }.Schedule();
                    m_task = m_handle.AsOnityTask();
                    if (!m_task.IsCompleted)
                    {
                        RegisteredInAwake = true;
                        m_task.GetAwaiter().OnCompleted(Complete);
                        return;
                    }
                    m_task.GetAwaiter().GetResult();
                    DisposeWork();
                }
                throw new InvalidOperationException("Awake could not observe a pending finite job.");
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
                try
                {
                    DisposeWork();
                }
                catch (Exception cleanupError)
                {
                    Error += "\nCleanup: " + cleanupError;
                }
                Completed = true;
            }
        }

        private void Complete()
        {
            try
            {
                m_task.GetAwaiter().GetResult();
                Result = m_output[0];
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
            finally
            {
                try
                {
                    DisposeWork();
                }
                catch (Exception cleanupError)
                {
                    Error = (Error ?? string.Empty) + "\nCleanup: " + cleanupError;
                }
                Completed = true;
            }
        }

        private void OnDisable() => DisposeWork();
        private void OnDestroy() => DisposeWork();

        private void DisposeWork()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= DisposeWork;
#endif
            m_handle.Complete();
            m_handle = default;
            if (m_output.IsCreated)
            {
                m_output.Dispose();
            }
        }

        private struct ComputeJob : IJob
        {
            public NativeArray<int> Output;
            public int Iterations;
            public void Execute() => Output[0] = Calculate(Iterations);
        }

        private static int Calculate(int iterations)
        {
            int value = 17;
            for (int i = 0; i < iterations; i++)
            {
                value = unchecked(value * 1664525 + 1013904223);
            }
            return value;
        }
    }
}
