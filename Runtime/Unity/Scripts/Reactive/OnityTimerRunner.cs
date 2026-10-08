using System;
using System.Collections.Generic;
using Onity.Reactive;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Internal update runner that advances active timers each frame.
    /// </summary>
    internal static class OnityTimerRunner
    {
        private static readonly List<OnityTimer> s_timers = new List<OnityTimer>(64);
        private static IDisposable s_updateSubscription;
        private static bool s_isUpdating;

        public static void Register(OnityTimer timer)
        {
            if (timer == null)
            {
                return;
            }

            if (s_timers.Contains(timer))
            {
                return;
            }

            s_timers.Add(timer);
            EnsureUpdateSubscription();
        }

        public static void Deregister(OnityTimer timer)
        {
            if (timer == null)
            {
                return;
            }

            int timerIndex = s_timers.IndexOf(timer);

            if (timerIndex < 0)
            {
                return;
            }

            if (s_isUpdating)
            {
                s_timers[timerIndex] = null;
                return;
            }

            RemoveAtSwapBack(timerIndex);

            if (s_timers.Count == 0)
            {
                DisposeUpdateSubscription();
            }
        }

        private static void EnsureUpdateSubscription()
        {
            if (s_updateSubscription != null)
            {
                return;
            }

            s_updateSubscription = OnityUnityObservable.EveryUpdate().Subscribe(_ => UpdateTimers());
        }

        private static void UpdateTimers()
        {
            UpdateTimers(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private static void UpdateTimers(float deltaTimeSeconds, float unscaledDeltaTimeSeconds)
        {
            if (s_isUpdating || s_timers.Count == 0)
            {
                return;
            }

            s_isUpdating = true;
            try
            {
                // Registrations made by callbacks begin on the next update.
                for (int i = s_timers.Count - 1; i >= 0; i--)
                {
                    OnityTimer timer = s_timers[i];
                    timer?.TickFromRunner(deltaTimeSeconds, unscaledDeltaTimeSeconds);
                }
            }
            finally
            {
                s_isUpdating = false;
                for (int i = s_timers.Count - 1; i >= 0; i--)
                {
                    if (s_timers[i] == null)
                    {
                        RemoveAtSwapBack(i);
                    }
                }

                if (s_timers.Count == 0)
                {
                    DisposeUpdateSubscription();
                }
            }
        }

        private static void RemoveAtSwapBack(int index)
        {
            int lastIndex = s_timers.Count - 1;
            s_timers[index] = s_timers[lastIndex];
            s_timers.RemoveAt(lastIndex);
        }

        private static void DisposeUpdateSubscription()
        {
            s_updateSubscription?.Dispose();
            s_updateSubscription = null;
        }
    }
}
