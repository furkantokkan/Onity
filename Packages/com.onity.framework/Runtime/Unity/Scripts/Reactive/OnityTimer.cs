using System;

namespace Onity.Unity.Reactive
{
    /// <summary>
    /// Shared timer contract for Onity timer implementations.
    /// </summary>
    public interface IOnityTimer : IDisposable
    {
        /// <summary>
        /// True while timer is running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// True when timer reached its completion condition.
        /// </summary>
        bool IsFinished { get; }

        /// <summary>
        /// Current timer time in seconds.
        /// </summary>
        float CurrentTimeSeconds { get; }

        /// <summary>
        /// Normalized progress between 0 and 1 when applicable.
        /// </summary>
        float Progress01 { get; }

        /// <summary>
        /// Starts the timer and resets internal state.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the timer.
        /// </summary>
        void Stop();

        /// <summary>
        /// Pauses the timer without resetting state.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes the timer from paused state.
        /// </summary>
        void Resume();

        /// <summary>
        /// Resets internal timer state.
        /// </summary>
        void Reset();

        /// <summary>
        /// Advances timer by explicit delta time.
        /// </summary>
        /// <param name="deltaTimeSeconds">Delta time in seconds.</param>
        void Tick(float deltaTimeSeconds);

        /// <summary>
        /// Invoked when timer starts.
        /// </summary>
        event Action Started;

        /// <summary>
        /// Invoked when timer stops.
        /// </summary>
        event Action Stopped;

        /// <summary>
        /// Invoked when timer completes.
        /// </summary>
        event Action Completed;
    }

    /// <summary>
    /// Base class for Onity timer implementations.
    /// </summary>
    public abstract class OnityTimer : IOnityTimer
    {
        private readonly bool m_useUnscaledTime;
        private readonly bool m_autoUpdate;
        private bool m_isRunning;
        private bool m_isDisposed;

        /// <inheritdoc />
        public event Action Started;

        /// <inheritdoc />
        public event Action Stopped;

        /// <inheritdoc />
        public event Action Completed;

        /// <inheritdoc />
        public bool IsRunning => m_isRunning;

        /// <summary>
        /// True when timer uses unscaled delta time in automatic update mode.
        /// </summary>
        public bool UseUnscaledTime => m_useUnscaledTime;

        /// <summary>
        /// Initializes timer base.
        /// </summary>
        /// <param name="useUnscaledTime">Use unscaled time in automatic update mode.</param>
        /// <param name="autoUpdate">Register to automatic update runner.</param>
        protected OnityTimer(bool useUnscaledTime = false, bool autoUpdate = true)
        {
            m_useUnscaledTime = useUnscaledTime;
            m_autoUpdate = autoUpdate;
            m_isRunning = false;
            m_isDisposed = false;
        }

        /// <inheritdoc />
        public abstract bool IsFinished { get; }

        /// <inheritdoc />
        public abstract float CurrentTimeSeconds { get; }

        /// <inheritdoc />
        public abstract float Progress01 { get; }

        /// <inheritdoc />
        public void Start()
        {
            EnsureNotDisposed();
            Reset();

            if (m_isRunning)
            {
                return;
            }

            m_isRunning = true;

            if (m_autoUpdate)
            {
                OnityTimerRunner.Register(this);
            }

            OnStarted();
            Started?.Invoke();

            if (IsFinished)
            {
                Complete();
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
            EnsureNotDisposed();
            StopInternal(true);
        }

        /// <inheritdoc />
        public void Pause()
        {
            EnsureNotDisposed();
            StopInternal(false);
        }

        /// <inheritdoc />
        public void Resume()
        {
            EnsureNotDisposed();

            if (m_isRunning)
            {
                return;
            }

            m_isRunning = true;

            if (m_autoUpdate)
            {
                OnityTimerRunner.Register(this);
            }
        }

        /// <inheritdoc />
        public abstract void Reset();

        /// <inheritdoc />
        public void Tick(float deltaTimeSeconds)
        {
            EnsureNotDisposed();

            if (m_isRunning == false || deltaTimeSeconds <= 0f)
            {
                return;
            }

            TickCore(deltaTimeSeconds);

            if (IsFinished)
            {
                Complete();
            }
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            StopInternal(false);
            m_isDisposed = true;
            Started = null;
            Stopped = null;
            Completed = null;
        }

        internal void TickFromRunner(float deltaTimeSeconds, float unscaledDeltaTimeSeconds)
        {
            float selectedDeltaTime = m_useUnscaledTime ? unscaledDeltaTimeSeconds : deltaTimeSeconds;
            Tick(selectedDeltaTime);
        }

        /// <summary>
        /// Throws when timer has already been disposed.
        /// </summary>
        protected void EnsureNotDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        /// <summary>
        /// Timer update implementation.
        /// </summary>
        /// <param name="deltaTimeSeconds">Delta time in seconds.</param>
        protected abstract void TickCore(float deltaTimeSeconds);

        /// <summary>
        /// Optional callback invoked when timer starts.
        /// </summary>
        protected virtual void OnStarted()
        {
        }

        /// <summary>
        /// Optional callback invoked when timer stops.
        /// </summary>
        protected virtual void OnStopped()
        {
        }

        /// <summary>
        /// Completes timer and emits completion event once.
        /// </summary>
        protected void Complete()
        {
            if (m_isRunning == false)
            {
                return;
            }

            StopInternal(true);
            Completed?.Invoke();
        }

        private void StopInternal(bool notifyStopped)
        {
            if (m_isRunning == false)
            {
                return;
            }

            m_isRunning = false;

            if (m_autoUpdate)
            {
                OnityTimerRunner.Deregister(this);
            }

            OnStopped();

            if (notifyStopped)
            {
                Stopped?.Invoke();
            }
        }
    }
}
