using System;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.Reactive;
using UnityEngine;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Unity player loop phase used by Onity frame/time providers.
    /// </summary>
    public enum OnityUnityLoopPhase
    {
        Update = 0,
        FixedUpdate = 1,
        LateUpdate = 2
    }

    /// <summary>
    /// Unity time mode used by Onity time providers.
    /// </summary>
    public enum OnityUnityTimeMode
    {
        Scaled = 0,
        Unscaled = 1,
        Realtime = 2
    }

    /// <summary>
    /// Unity implementation of <see cref="OnityFrameProvider" />.
    /// </summary>
    public sealed class OnityUnityFrameProvider : OnityFrameProvider
    {
        private readonly Func<IOnityObservable<Unit>> m_streamFactory;

        /// <summary>
        /// Initializes a new frame provider.
        /// </summary>
        /// <param name="name">Provider name.</param>
        /// <param name="phase">Loop phase.</param>
        /// <param name="streamFactory">Underlying frame stream factory.</param>
        public OnityUnityFrameProvider(
            string name,
            OnityUnityLoopPhase phase,
            Func<IOnityObservable<Unit>> streamFactory)
        {
            Name = string.IsNullOrWhiteSpace(name) ? phase.ToString() : name;
            Phase = phase;
            m_streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
        }

        /// <inheritdoc />
        public override string Name { get; }

        /// <summary>
        /// Backing Unity loop phase.
        /// </summary>
        public OnityUnityLoopPhase Phase { get; }

        /// <inheritdoc />
        public override IOnityObservable<Unit> EveryFrame()
        {
            return m_streamFactory();
        }
    }

    /// <summary>
    /// Built-in Unity frame providers used by Onity reactive APIs.
    /// </summary>
    public static class OnityFrameProviders
    {
        /// <summary>
        /// Update loop frame provider.
        /// </summary>
        public static OnityUnityFrameProvider Update { get; } =
            new OnityUnityFrameProvider(
                "Update",
                OnityUnityLoopPhase.Update,
                OnityUnityObservable.EveryUpdate);

        /// <summary>
        /// Fixed update loop frame provider.
        /// </summary>
        public static OnityUnityFrameProvider FixedUpdate { get; } =
            new OnityUnityFrameProvider(
                "FixedUpdate",
                OnityUnityLoopPhase.FixedUpdate,
                OnityUnityObservable.EveryFixedUpdate);

        /// <summary>
        /// Late update loop frame provider.
        /// </summary>
        public static OnityUnityFrameProvider LateUpdate { get; } =
            new OnityUnityFrameProvider(
                "LateUpdate",
                OnityUnityLoopPhase.LateUpdate,
                OnityUnityObservable.EveryLateUpdate);
    }

    /// <summary>
    /// Unity implementation of <see cref="OnityTimeProvider" />.
    /// </summary>
    public sealed class OnityUnityTimeProvider : OnityTimeProvider
    {
        private readonly OnityUnityFrameProvider m_frameProvider;
        private readonly OnityUnityTimeMode m_timeMode;

        /// <summary>
        /// Initializes a new Unity time provider.
        /// </summary>
        /// <param name="frameProvider">Frame provider used for ticking.</param>
        /// <param name="timeMode">Time mode.</param>
        public OnityUnityTimeProvider(
            OnityUnityFrameProvider frameProvider,
            OnityUnityTimeMode timeMode)
        {
            m_frameProvider = frameProvider ?? throw new ArgumentNullException(nameof(frameProvider));
            m_timeMode = timeMode;
        }

        /// <summary>
        /// Backing Unity loop phase.
        /// </summary>
        public OnityUnityLoopPhase Phase => m_frameProvider.Phase;

        /// <summary>
        /// Backing Unity time mode.
        /// </summary>
        public OnityUnityTimeMode TimeMode => m_timeMode;

        /// <inheritdoc />
        public override async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await Task.FromCanceled(cancellationToken);
                return;
            }

            if (m_timeMode == OnityUnityTimeMode.Realtime)
            {
                await Task.Delay(delay, cancellationToken);
                return;
            }

            TaskCompletionSource<Unit> completionSource =
                new TaskCompletionSource<Unit>(TaskCreationOptions.RunContinuationsAsynchronously);

            float targetSeconds = (float)delay.TotalSeconds;
            float elapsedSeconds = 0f;
            IDisposable frameSubscription = DisposableAction.Empty;
            CancellationTokenRegistration registration = default;

            frameSubscription = m_frameProvider.EveryFrame().Subscribe(
                _ =>
                {
                    elapsedSeconds += GetDeltaTimeSeconds();

                    if (elapsedSeconds < targetSeconds)
                    {
                        return;
                    }

                    frameSubscription.Dispose();
                    registration.Dispose();
                    completionSource.TrySetResult(Unit.Default);
                });

            if (cancellationToken.CanBeCanceled)
            {
                registration =
                    cancellationToken.Register(
                        () =>
                        {
                            frameSubscription.Dispose();
                            completionSource.TrySetCanceled(cancellationToken);
                        });
            }

            await completionSource.Task;
        }

        private float GetDeltaTimeSeconds()
        {
            switch (m_timeMode)
            {
                case OnityUnityTimeMode.Scaled:
                    return Phase == OnityUnityLoopPhase.FixedUpdate
                        ? Time.fixedDeltaTime
                        : Time.deltaTime;

                case OnityUnityTimeMode.Unscaled:
                    return Phase == OnityUnityLoopPhase.FixedUpdate
                        ? Time.fixedUnscaledDeltaTime
                        : Time.unscaledDeltaTime;

                default:
                    return Time.unscaledDeltaTime;
            }
        }
    }

    /// <summary>
    /// Built-in Unity time providers used by Onity reactive APIs.
    /// </summary>
    public static class OnityTimeProviders
    {
        /// <summary>
        /// Update + scaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider UpdateScaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.Update, OnityUnityTimeMode.Scaled);

        /// <summary>
        /// Update + unscaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider UpdateUnscaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.Update, OnityUnityTimeMode.Unscaled);

        /// <summary>
        /// Update + realtime provider.
        /// </summary>
        public static OnityUnityTimeProvider UpdateRealtime { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.Update, OnityUnityTimeMode.Realtime);

        /// <summary>
        /// FixedUpdate + scaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider FixedScaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.FixedUpdate, OnityUnityTimeMode.Scaled);

        /// <summary>
        /// FixedUpdate + unscaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider FixedUnscaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.FixedUpdate, OnityUnityTimeMode.Unscaled);

        /// <summary>
        /// FixedUpdate + realtime provider.
        /// </summary>
        public static OnityUnityTimeProvider FixedRealtime { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.FixedUpdate, OnityUnityTimeMode.Realtime);

        /// <summary>
        /// LateUpdate + scaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider LateScaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.LateUpdate, OnityUnityTimeMode.Scaled);

        /// <summary>
        /// LateUpdate + unscaled time provider.
        /// </summary>
        public static OnityUnityTimeProvider LateUnscaled { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.LateUpdate, OnityUnityTimeMode.Unscaled);

        /// <summary>
        /// LateUpdate + realtime provider.
        /// </summary>
        public static OnityUnityTimeProvider LateRealtime { get; } =
            new OnityUnityTimeProvider(OnityFrameProviders.LateUpdate, OnityUnityTimeMode.Realtime);
    }
}
