using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    // A registration failure is a proposed fault, not evidence that a held
    // ValueTask was consumed. A malformed pending source can retain this owner.
    internal abstract class OnityBclStreamOperation
    {
        private readonly object m_gate = new object();
        private readonly Action m_consumed;
        private readonly Action<bool> m_registration;
        private readonly Action m_callback;
        private bool m_reading;
        private bool m_observeRequested;
        private bool m_isConsumed;
        private bool m_reported;

        protected OnityBclStreamOperation(Action consumed, Action<bool> registration)
        {
            m_consumed = consumed;
            m_registration = registration;
            m_callback = ObserveTerminal;
        }

        internal bool IsConsumed
        {
            get
            {
                lock (m_gate)
                {
                    return m_isConsumed;
                }
            }
        }

        internal void Start()
        {
            bool completed;
            try
            {
                completed = IsCompletedCore();
            }
            catch (Exception exception)
            {
                Report(OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (completed)
            {
                ObserveTerminal();
                return;
            }
            m_registration(true);
            try
            {
                RegisterCore(m_callback);
            }
            catch (Exception exception)
            {
                Report(OnityAsyncStreamOutcome.Failure(exception));
                ObserveTerminal();
            }
            finally
            {
                m_registration(false);
            }
        }

        internal void ObserveTerminal()
        {
            lock (m_gate)
            {
                if (m_isConsumed)
                {
                    return;
                }
                if (m_reading)
                {
                    m_observeRequested = true;
                    return;
                }
                m_reading = true;
            }
            while (true)
            {
                OnityAsyncStreamOutcome outcome = default;
                bool consumed = false;
                bool report = false;
                try
                {
                    if (IsCompletedCore())
                    {
                        outcome = ReadCore();
                        consumed = true;
                        report = true;
                    }
                }
                catch (Exception exception)
                {
                    // A status failure cannot certify consumption of a pending VT.
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                    report = true;
                }
                bool repeat;
                lock (m_gate)
                {
                    m_isConsumed |= consumed;
                    repeat = !m_isConsumed && m_observeRequested;
                    m_observeRequested = false;
                    if (!repeat)
                    {
                        m_reading = false;
                    }
                }
                if (consumed)
                {
                    m_consumed();
                }
                if (report)
                {
                    Report(outcome);
                }
                if (!repeat)
                {
                    return;
                }
            }
        }

        private void Report(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (m_reported)
                {
                    return;
                }
                m_reported = true;
            }
            PublishCore(outcome);
        }

        protected abstract void PublishCore(OnityAsyncStreamOutcome outcome);
        protected abstract bool IsCompletedCore();
        protected abstract void RegisterCore(Action callback);
        protected abstract OnityAsyncStreamOutcome ReadCore();
    }

    internal sealed class OnityBclStreamMove : OnityBclStreamOperation
    {
        private readonly object m_resultGate = new object();
        private ValueTask<bool> m_task;
        private OnityTaskCompletionSource<bool> m_output;
        private OnityAsyncStreamOutcome m_outcome;
        private bool m_ready;

        internal OnityBclStreamMove(ValueTask<bool> task,
            Action consumed, Action<bool> registration) : base(consumed, registration)
        {
            m_task = task;
        }

        internal OnityTask<bool> GetTask()
        {
            OnityTaskCompletionSource<bool> output;
            OnityAsyncStreamOutcome outcome;
            bool ready;
            lock (m_resultGate)
            {
                if (m_ready && m_outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask<bool>.FromResult(m_outcome.Value);
                }
                m_output ??= new OnityTaskCompletionSource<bool>();
                output = m_output;
                outcome = m_outcome;
                ready = m_ready;
            }
            if (ready)
            {
                outcome.Publish(output, outcome.Value);
            }
            return output.Task;
        }

        protected override void PublishCore(OnityAsyncStreamOutcome outcome)
        {
            OnityTaskCompletionSource<bool> output;
            lock (m_resultGate)
            {
                m_ready = true;
                m_outcome = outcome;
                output = m_output;
            }
            if (output != null)
            {
                outcome.Publish(output, outcome.Value);
            }
        }

        protected override bool IsCompletedCore() => m_task.IsCompleted;
        protected override void RegisterCore(Action callback) =>
            m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(callback);

        protected override OnityAsyncStreamOutcome ReadCore()
        {
            // Snapshot status before GetResult: source-backed VT tokens can be
            // invalidated by consumption, including canceled/faulted GetResult.
            bool canceled = m_task.IsCanceled;
            try
            {
                return OnityAsyncStreamOutcome.Success(m_task.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException exception) when (canceled)
            {
                return new OnityAsyncStreamOutcome
                {
                    Status = OnityTaskSourceStatus.Canceled,
                    Token = exception.CancellationToken
                };
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
            finally
            {
                m_task = default;
            }
        }
    }

    internal sealed class OnityBclStreamCleanup : OnityBclStreamOperation
    {
        private ValueTask m_task;
        private readonly Action<OnityAsyncStreamOutcome> m_report;

        internal OnityBclStreamCleanup(ValueTask task, Action<OnityAsyncStreamOutcome> report,
            Action consumed, Action<bool> registration) : base(consumed, registration)
        {
            m_task = task;
            m_report = report;
        }

        protected override void PublishCore(OnityAsyncStreamOutcome outcome) => m_report(outcome);

        protected override bool IsCompletedCore() => m_task.IsCompleted;
        protected override void RegisterCore(Action callback) =>
            m_task.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(callback);

        protected override OnityAsyncStreamOutcome ReadCore()
        {
            bool canceled = m_task.IsCanceled;
            try
            {
                m_task.GetAwaiter().GetResult();
                return OnityAsyncStreamOutcome.Success(false);
            }
            catch (OperationCanceledException exception) when (canceled)
            {
                return new OnityAsyncStreamOutcome
                {
                    Status = OnityTaskSourceStatus.Canceled,
                    Token = exception.CancellationToken
                };
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
            finally
            {
                m_task = default;
            }
        }
    }

    internal sealed class OnityBclAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IAsyncEnumerable<T> m_source;

        internal OnityBclAsyncEnumerable(IAsyncEnumerable<T> source)
        {
            m_source = source;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, cancellationToken);
        }

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private readonly object m_gate = new object();
            private readonly IAsyncEnumerable<T> m_source;
            private readonly CancellationToken m_callerToken;
            private readonly Action m_onConsumed;
            private readonly Action<bool> m_onRegistration;
            private readonly Action<OnityAsyncStreamOutcome> m_onQuiescence;
            private readonly Action<OnityAsyncStreamOutcome> m_onCleanup;
            private OnityAsyncStreamTokenLifetime m_lifetime;
            private IAsyncEnumerator<T> m_upstream;
            private OnityBclStreamMove m_move;
            private OnityBclStreamCleanup m_dispose;
            private OnityTaskCompletionSource m_cleanup;
            private OnityAsyncStreamOutcome m_quiescenceOutcome;
            private OnityAsyncStreamOutcome m_cleanupOutcome;
            private int m_registrations;
            private bool m_cleanupStarted;
            private bool m_cleanupReported;
            private bool m_quiescent;
            private bool m_driving;
            private bool m_driveRequested;
            private bool m_finished;

            internal Enumerator(IAsyncEnumerable<T> source, CancellationToken token)
            {
                m_source = source;
                m_callerToken = token;
                m_onConsumed = DriveCleanup;
                m_onRegistration = OnRegistration;
                m_onQuiescence = OnQuiescence;
                m_onCleanup = OnCleanup;
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_lifetime == null)
                {
                    m_lifetime = new OnityAsyncStreamTokenLifetime();
                    m_lifetime.Initialize(m_callerToken);
                }
                if (m_upstream == null)
                {
                    m_upstream = m_source.GetAsyncEnumerator(m_lifetime.Token);
                    if (m_upstream == null)
                    {
                        throw new InvalidOperationException("The BCL source returned a null enumerator.");
                    }
                }
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                ValueTask<bool> task = m_upstream.MoveNextAsync();
                var move = new OnityBclStreamMove(task, m_onConsumed, m_onRegistration);
                lock (m_gate)
                {
                    m_move = move;
                }
                move.Start();
                return move.GetTask();
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = m_upstream.Current;
                return true;
            }

            protected override OnityTask CleanupCore()
            {
                if (m_lifetime == null)
                {
                    return OnityTask.Completed;
                }
                OnityTask result;
                lock (m_gate)
                {
                    if (m_cleanup != null)
                    {
                        return m_cleanup.Task;
                    }
                    m_cleanup = new OnityTaskCompletionSource();
                    result = m_cleanup.Task;
                }
                try
                {
                    OnityTask quiescence = m_lifetime.Close();
                    // One bounded recheck after owned Cancel, not a poll or a
                    // registration retry. It can rescue synchronous cancellation.
                    m_move?.ObserveTerminal();
                    if (quiescence.IsCompleted)
                    {
                        OnQuiescence(OnityAsyncStreamOutcome.Read(quiescence));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(quiescence, m_onQuiescence).Register();
                    }
                }
                catch (Exception exception)
                {
                    OnQuiescence(OnityAsyncStreamOutcome.Failure(exception));
                }
                DriveCleanup();
                return result;
            }

            private void OnRegistration(bool entering)
            {
                lock (m_gate)
                {
                    m_registrations += entering ? 1 : -1;
                }
                if (!entering)
                {
                    DriveCleanup();
                }
            }

            private void OnQuiescence(OnityAsyncStreamOutcome outcome)
            {
                lock (m_gate)
                {
                    m_quiescent = true;
                    m_quiescenceOutcome = outcome;
                }
                DriveCleanup();
            }

            private void OnCleanup(OnityAsyncStreamOutcome outcome)
            {
                lock (m_gate)
                {
                    m_cleanupReported = true;
                    m_cleanupOutcome = outcome;
                }
                DriveCleanup();
            }

            private void DriveCleanup()
            {
                lock (m_gate)
                {
                    if (m_driving)
                    {
                        m_driveRequested = true;
                        return;
                    }
                    m_driving = true;
                }
                while (true)
                {
                    bool start = false;
                    bool finish = false;
                    OnityAsyncStreamOutcome outcome = default;
                    lock (m_gate)
                    {
                        m_driveRequested = false;
                        if (m_cleanup != null && !m_finished && m_registrations == 0 &&
                            (m_move == null || m_move.IsConsumed))
                        {
                            if (!m_cleanupStarted)
                            {
                                m_cleanupStarted = true;
                                start = true;
                            }
                            else if (m_cleanupReported && (m_dispose == null || m_dispose.IsConsumed) &&
                                m_quiescent)
                            {
                                m_finished = true;
                                finish = true;
                                outcome = m_cleanupOutcome.Status == OnityTaskSourceStatus.Succeeded
                                    ? m_quiescenceOutcome : m_cleanupOutcome;
                            }
                        }
                    }
                    if (start)
                    {
                        StartCleanup();
                    }
                    if (finish)
                    {
                        m_upstream = null;
                        try
                        {
                            m_lifetime.Dispose();
                        }
                        catch (Exception exception)
                        {
                            outcome = OnityAsyncStreamOutcome.Failure(exception);
                        }
                        outcome.Publish<bool>(m_cleanup, true);
                    }
                    lock (m_gate)
                    {
                        if (!m_driveRequested)
                        {
                            m_driving = false;
                            return;
                        }
                    }
                }
            }

            private void StartCleanup()
            {
                if (m_upstream == null)
                {
                    OnCleanup(OnityAsyncStreamOutcome.Success(false));
                    return;
                }
                try
                {
                    ValueTask task = m_upstream.DisposeAsync();
                    var operation = new OnityBclStreamCleanup(task, m_onCleanup, m_onConsumed, m_onRegistration);
                    lock (m_gate)
                    {
                        m_dispose = operation;
                    }
                    operation.Start();
                }
                catch (Exception exception)
                {
                    OnCleanup(OnityAsyncStreamOutcome.Failure(exception));
                }
            }
        }
    }

    internal sealed class OnityNativeAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;

        internal OnityNativeAsyncEnumerable(IOnityAsyncEnumerable<T> source)
        {
            m_source = source;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, cancellationToken);
        }

        private sealed class IdentityEnumerator : OnitySourceAsyncEnumerator<T, T>
        {
            internal IdentityEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
                : base(source, token)
            {
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = UpstreamCurrent;
                return true;
            }
        }

        private sealed class Enumerator : IAsyncEnumerator<T>
        {
            private readonly object m_gate = new object();
            private readonly IdentityEnumerator m_native;
            private TaskCompletionSource<bool> m_dispose;
            private bool m_closed;

            internal Enumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
            {
                m_native = new IdentityEnumerator(source, token);
            }

            public T Current => m_native.Current;

            public ValueTask<bool> MoveNextAsync()
            {
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return new ValueTask<bool>(false);
                    }
                }
                try
                {
                    OnityTask<bool> task = m_native.MoveNextAsync();
                    if (task.IsCompleted)
                    {
                        OnityAsyncStreamOutcome outcome = OnityAsyncStreamOutcome.Read(task);
                        if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                        {
                            return new ValueTask<bool>(outcome.Value);
                        }
                        var failed = CreateBridge();
                        Publish(failed, outcome);
                        return new ValueTask<bool>(failed.Task);
                    }
                    var bridge = CreateBridge();
                    new OnityAsyncStreamMoveObserver(task, outcome => Publish(bridge, outcome)).Register();
                    return new ValueTask<bool>(bridge.Task);
                }
                catch (Exception exception)
                {
                    var failed = CreateBridge();
                    failed.TrySetException(exception);
                    return new ValueTask<bool>(failed.Task);
                }
            }

            public ValueTask DisposeAsync()
            {
                TaskCompletionSource<bool> bridge;
                lock (m_gate)
                {
                    if (m_dispose != null)
                    {
                        return new ValueTask(m_dispose.Task);
                    }
                    m_closed = true;
                    m_dispose = CreateBridge();
                    bridge = m_dispose;
                }
                try
                {
                    OnityTask task = m_native.DisposeAsync();
                    if (task.IsCompleted)
                    {
                        Publish(bridge, OnityAsyncStreamOutcome.Read(task));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(task, outcome => Publish(bridge, outcome)).Register();
                    }
                }
                catch (Exception exception)
                {
                    bridge.TrySetException(exception);
                }
                return new ValueTask(bridge.Task);
            }

            private static TaskCompletionSource<bool> CreateBridge()
            {
                return OnityAsyncExecutionContext.CreateTaskBridge<bool>();
            }

            private static void Publish(TaskCompletionSource<bool> bridge, OnityAsyncStreamOutcome outcome)
            {
                if (outcome.Status == OnityTaskSourceStatus.Faulted)
                {
                    bridge.TrySetException(outcome.Fault);
                }
                else if (outcome.Status == OnityTaskSourceStatus.Canceled)
                {
                    bridge.TrySetCanceled(outcome.Token);
                }
                else
                {
                    bridge.TrySetResult(outcome.Value);
                }
            }
        }
    }
}
