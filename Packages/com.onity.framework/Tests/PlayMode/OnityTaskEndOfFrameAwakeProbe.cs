using System;
using System.Threading;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskEndOfFrameAwakeProbe : MonoBehaviour
    {
        public bool ValidatedInAwake;
        public bool AwakeBeforeEnteredPlay;
        public bool EditRejected;
        public string Error;
#if UNITY_EDITOR
        private static bool s_enteredPlay;

        static OnityTaskEndOfFrameAwakeProbe()
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
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool unsupported = (Application.isEditor && Application.isBatchMode)
                    || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
                try
                {
                    OnityTask task = OnityTask.WaitForEndOfFrame(cancellation.Token);
                    if (unsupported)
                    {
                        Error = "Unsupported rendering accepted EOF in Awake.";
                        return;
                    }
                    try
                    {
                        task.GetAwaiter().GetResult();
                        Error = "Precanceled EOF unexpectedly succeeded.";
                    }
                    catch (OperationCanceledException exception)
                    {
                        ValidatedInAwake = exception.CancellationToken == cancellation.Token;
                    }
                }
                catch (PlatformNotSupportedException)
                {
                    ValidatedInAwake = unsupported;
                }
                catch (Exception exception)
                {
                    Error = exception.ToString();
                }
            }
        }

        public void CheckAfterExit()
        {
            try
            {
                OnityTask.WaitForEndOfFrame();
                Error = "Edit session accepted EOF.";
            }
            catch (InvalidOperationException)
            {
                EditRejected = true;
            }
            catch (Exception exception)
            {
                Error = exception.ToString();
            }
        }
    }
}
