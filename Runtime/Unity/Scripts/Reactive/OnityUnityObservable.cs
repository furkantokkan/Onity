using System;
using System.Threading;
using Onity.Core;
using Onity.DOTS;
using Onity.Reactive;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Threading mode for Unity frame observables.
    /// </summary>
    public enum OnityUnityThreadMode
    {
        /// <summary>
        /// Emits directly on main thread without job scheduling.
        /// </summary>
        SingleThread = 0,

        /// <summary>
        /// Inserts a lightweight parallel job boundary before each emission.
        /// </summary>
        JobMultiThread = 1,

        /// <summary>
        /// Inserts a Burst-friendly parallel job boundary before each emission.
        /// </summary>
        BurstJobMultiThread = 2,

        /// <summary>
        /// Uses Burst job boundary and emits only when DOTS integer accumulator value changes.
        /// Falls back to per-frame emit when DOTS queue is not available.
        /// </summary>
        DotsEventDriven = 3
    }

    /// <summary>
    /// Unity-specific observable factories.
    /// </summary>
    public static class OnityUnityObservable
    {
        private const string k_updatePumpName = "OnityUpdatePump";
        private static readonly IOnityObservable<Unit> s_lazyUpdateStream = CreateLazyFrameStream(GetUpdateStreamUnsafe);
        private static readonly IOnityObservable<Unit> s_lazyFixedUpdateStream = CreateLazyFrameStream(GetFixedUpdateStreamUnsafe);
        private static readonly IOnityObservable<Unit> s_lazyLateUpdateStream = CreateLazyFrameStream(GetLateUpdateStreamUnsafe);
        private static OnityUpdatePump s_updatePump;

        /// <summary>
        /// Emits one <see cref="Unit" /> value every rendered frame.
        /// </summary>
        /// <returns>Shared update observable.</returns>
        public static IOnityObservable<Unit> EveryUpdate()
        {
            return s_lazyUpdateStream;
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value every rendered frame with selected threading mode.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Configured update observable.</returns>
        public static IOnityObservable<Unit> EveryUpdate(
            OnityUnityThreadMode threadMode,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return ResolveThreadedFrameStream(
                EveryUpdate(),
                threadMode,
                jobWorkItemCount,
                minCommandsPerJob);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value every rendered frame until token is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware update observable.</returns>
        public static IOnityObservable<Unit> EveryUpdate(CancellationToken cancellationToken)
        {
            return EveryUpdate().TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value every rendered frame with selected threading mode until token is canceled.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Cancellation-aware update observable.</returns>
        public static IOnityObservable<Unit> EveryUpdate(
            OnityUnityThreadMode threadMode,
            CancellationToken cancellationToken,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return EveryUpdate(threadMode, jobWorkItemCount, minCommandsPerJob)
                .TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each <see cref="MonoBehaviour.FixedUpdate" />.
        /// </summary>
        /// <returns>Shared fixed-update observable.</returns>
        public static IOnityObservable<Unit> EveryFixedUpdate()
        {
            return s_lazyFixedUpdateStream;
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each fixed update with selected threading mode.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Configured fixed-update observable.</returns>
        public static IOnityObservable<Unit> EveryFixedUpdate(
            OnityUnityThreadMode threadMode,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return ResolveThreadedFrameStream(
                EveryFixedUpdate(),
                threadMode,
                jobWorkItemCount,
                minCommandsPerJob);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each fixed update until token is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware fixed-update observable.</returns>
        public static IOnityObservable<Unit> EveryFixedUpdate(CancellationToken cancellationToken)
        {
            return EveryFixedUpdate().TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each fixed update with selected threading mode until token is canceled.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Cancellation-aware fixed-update observable.</returns>
        public static IOnityObservable<Unit> EveryFixedUpdate(
            OnityUnityThreadMode threadMode,
            CancellationToken cancellationToken,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return EveryFixedUpdate(threadMode, jobWorkItemCount, minCommandsPerJob)
                .TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each <see cref="MonoBehaviour.LateUpdate" />.
        /// </summary>
        /// <returns>Shared late-update observable.</returns>
        public static IOnityObservable<Unit> EveryLateUpdate()
        {
            return s_lazyLateUpdateStream;
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each late update with selected threading mode.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Configured late-update observable.</returns>
        public static IOnityObservable<Unit> EveryLateUpdate(
            OnityUnityThreadMode threadMode,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return ResolveThreadedFrameStream(
                EveryLateUpdate(),
                threadMode,
                jobWorkItemCount,
                minCommandsPerJob);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each late update until token is canceled.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cancellation-aware late-update observable.</returns>
        public static IOnityObservable<Unit> EveryLateUpdate(CancellationToken cancellationToken)
        {
            return EveryLateUpdate().TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits one <see cref="Unit" /> value each late update with selected threading mode until token is canceled.
        /// </summary>
        /// <param name="threadMode">Threading mode.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="jobWorkItemCount">Parallel work item count in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job in <see cref="OnityUnityThreadMode.JobMultiThread" />.</param>
        /// <returns>Cancellation-aware late-update observable.</returns>
        public static IOnityObservable<Unit> EveryLateUpdate(
            OnityUnityThreadMode threadMode,
            CancellationToken cancellationToken,
            int jobWorkItemCount = 64,
            int minCommandsPerJob = 32)
        {
            return EveryLateUpdate(threadMode, jobWorkItemCount, minCommandsPerJob)
                .TakeUntilCancellation(cancellationToken);
        }

        /// <summary>
        /// Emits once after the provided delay.
        /// </summary>
        /// <param name="dueTimeSeconds">Delay duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time for countdown.</param>
        /// <returns>Single-shot timer observable.</returns>
        public static IOnityObservable<Unit> Timer(float dueTimeSeconds, bool useUnscaledTime = false)
        {
            if (dueTimeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTimeSeconds));
            }

            if (dueTimeSeconds <= 0f)
            {
                return OnityObservable.Return(Unit.Default);
            }

            return new OnityObservable<Unit>(
                observer =>
                {
                    OnityCountdownTimer timer = new OnityCountdownTimer(dueTimeSeconds, useUnscaledTime);

                    void HandleTimerCompleted()
                    {
                        observer(Unit.Default);
                    }

                    timer.Completed += HandleTimerCompleted;
                    timer.Start();

                    return new DisposableAction(
                        () =>
                        {
                            timer.Completed -= HandleTimerCompleted;
                            timer.Dispose();
                        });
                });
        }

        /// <summary>
        /// Emits tick index repeatedly based on interval duration.
        /// </summary>
        /// <param name="intervalSeconds">Interval duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time for interval timer.</param>
        /// <returns>Interval observable emitting cumulative tick index.</returns>
        public static IOnityObservable<int> Interval(float intervalSeconds, bool useUnscaledTime = false)
        {
            if (intervalSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            }

            return new OnityObservable<int>(
                observer =>
                {
                    OnityIntervalTimer timer = new OnityIntervalTimer(intervalSeconds, useUnscaledTime);

                    void HandleIntervalElapsed(int tickIndex)
                    {
                        observer(tickIndex);
                    }

                    timer.IntervalElapsed += HandleIntervalElapsed;
                    timer.Start();

                    return new DisposableAction(
                        () =>
                        {
                            timer.IntervalElapsed -= HandleIntervalElapsed;
                            timer.Dispose();
                        });
                });
        }

        private static void EnsureUpdatePump()
        {
            if (s_updatePump != null)
            {
                return;
            }

            GameObject root = new GameObject(k_updatePumpName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(root);
            }

            s_updatePump = root.AddComponent<OnityUpdatePump>();
        }

        private static IOnityObservable<Unit> CreateLazyFrameStream(Func<IOnityObservable<Unit>> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }

            return new OnityObservable<Unit>(
                observer =>
                {
                    IOnityObservable<Unit> stream = getter();
                    return stream.Subscribe(observer);
                });
        }

        private static IOnityObservable<Unit> GetUpdateStreamUnsafe()
        {
            EnsureUpdatePump();
            return s_updatePump.UpdateStream;
        }

        private static IOnityObservable<Unit> GetFixedUpdateStreamUnsafe()
        {
            EnsureUpdatePump();
            return s_updatePump.FixedUpdateStream;
        }

        private static IOnityObservable<Unit> GetLateUpdateStreamUnsafe()
        {
            EnsureUpdatePump();
            return s_updatePump.LateUpdateStream;
        }

        private static IOnityObservable<Unit> ResolveThreadedFrameStream(
            IOnityObservable<Unit> source,
            OnityUnityThreadMode threadMode,
            int jobWorkItemCount,
            int minCommandsPerJob)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            switch (threadMode)
            {
                case OnityUnityThreadMode.SingleThread:
                    return source;

                case OnityUnityThreadMode.JobMultiThread:
                    return CreateJobThreadStream(
                        source,
                        jobWorkItemCount,
                        minCommandsPerJob,
                        false,
                        false);

                case OnityUnityThreadMode.BurstJobMultiThread:
                    return CreateJobThreadStream(
                        source,
                        jobWorkItemCount,
                        minCommandsPerJob,
                        true,
                        false);

                case OnityUnityThreadMode.DotsEventDriven:
                    return CreateJobThreadStream(
                        source,
                        jobWorkItemCount,
                        minCommandsPerJob,
                        true,
                        true);

                default:
                    throw new ArgumentOutOfRangeException(nameof(threadMode));
            }
        }

        private static IOnityObservable<Unit> CreateJobThreadStream(
            IOnityObservable<Unit> source,
            int jobWorkItemCount,
            int minCommandsPerJob,
            bool useBurstJob,
            bool emitOnlyOnDotsChange)
        {
            if (jobWorkItemCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(jobWorkItemCount));
            }

            if (minCommandsPerJob <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minCommandsPerJob));
            }

            return new OnityObservable<Unit>(
                observer =>
                {
                    NativeArray<int> workBuffer =
                        new NativeArray<int>(
                            jobWorkItemCount,
                            Allocator.Persistent,
                            NativeArrayOptions.ClearMemory);

                    JobHandle pendingHandle = default;
                    bool hasPendingHandle = false;
                    int previousDotsAccumulatorValue = 0;
                    bool hasPreviousDotsAccumulatorValue = false;
                    IDisposable sourceSubscription =
                        source.Subscribe(
                            _ =>
                            {
                                if (hasPendingHandle)
                                {
                                    pendingHandle.Complete();
                                }

                                if (emitOnlyOnDotsChange
                                    && TryGetDotsAccumulatorValue(out int dotsAccumulatorValue))
                                {
                                    if (hasPreviousDotsAccumulatorValue
                                        && previousDotsAccumulatorValue == dotsAccumulatorValue)
                                    {
                                        pendingHandle = ScheduleFrameMarkerJob(
                                            useBurstJob,
                                            workBuffer,
                                            jobWorkItemCount,
                                            minCommandsPerJob);
                                        hasPendingHandle = true;
                                        return;
                                    }

                                    previousDotsAccumulatorValue = dotsAccumulatorValue;
                                    hasPreviousDotsAccumulatorValue = true;
                                }

                                observer(Unit.Default);
                                pendingHandle = ScheduleFrameMarkerJob(
                                    useBurstJob,
                                    workBuffer,
                                    jobWorkItemCount,
                                    minCommandsPerJob);
                                hasPendingHandle = true;
                            });

                    return new DisposableAction(
                        () =>
                        {
                            sourceSubscription.Dispose();

                            if (hasPendingHandle)
                            {
                                pendingHandle.Complete();
                            }

                            if (workBuffer.IsCreated)
                            {
                                workBuffer.Dispose();
                            }
                        });
                });
        }

        private static JobHandle ScheduleFrameMarkerJob(
            bool useBurstJob,
            NativeArray<int> workBuffer,
            int jobWorkItemCount,
            int minCommandsPerJob)
        {
            if (useBurstJob)
            {
                return new OnityBurstFrameExecutionMarkerJob
                {
                    WorkBuffer = workBuffer
                }.Schedule(jobWorkItemCount, minCommandsPerJob);
            }

            return new OnityFrameExecutionMarkerJob
            {
                WorkBuffer = workBuffer
            }.Schedule(jobWorkItemCount, minCommandsPerJob);
        }

        private static bool TryGetDotsAccumulatorValue(out int value)
        {
            try
            {
                return OnityDotsIntEventBridge.TryGetAccumulatedValue(out value);
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private struct OnityFrameExecutionMarkerJob : IJobParallelFor
        {
            public NativeArray<int> WorkBuffer;

            public void Execute(int index)
            {
                WorkBuffer[index] = index;
            }
        }

#if ENABLE_BURST_AOT
        [global::Unity.Burst.BurstCompile]
#endif
        private struct OnityBurstFrameExecutionMarkerJob : IJobParallelFor
        {
            public NativeArray<int> WorkBuffer;

            public void Execute(int index)
            {
                WorkBuffer[index] = index;
            }
        }

        [DisallowMultipleComponent]
        private sealed class OnityUpdatePump : MonoBehaviour
        {
            private readonly Subject<Unit> m_updateStream = new Subject<Unit>();
            private readonly Subject<Unit> m_fixedUpdateStream = new Subject<Unit>();
            private readonly Subject<Unit> m_lateUpdateStream = new Subject<Unit>();

            public IOnityObservable<Unit> UpdateStream => m_updateStream;
            public IOnityObservable<Unit> FixedUpdateStream => m_fixedUpdateStream;
            public IOnityObservable<Unit> LateUpdateStream => m_lateUpdateStream;

            private void Update()
            {
                m_updateStream.OnNext(Unit.Default);
            }

            private void FixedUpdate()
            {
                m_fixedUpdateStream.OnNext(Unit.Default);
            }

            private void LateUpdate()
            {
                m_lateUpdateStream.OnNext(Unit.Default);
            }

            private void OnDestroy()
            {
                m_updateStream.Dispose();
                m_fixedUpdateStream.Dispose();
                m_lateUpdateStream.Dispose();
            }
        }
    }
}
