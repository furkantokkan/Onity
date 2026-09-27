using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal sealed class OnityAwaitStreamObserver<T>
    {
        private readonly OnityTask<T> m_task;
        private readonly Action<OnityAsyncStreamOutcome, T> m_callback;
        private int m_observed;
        private int m_reported;

        internal OnityAwaitStreamObserver(OnityTask<T> task, Action<OnityAsyncStreamOutcome, T> callback)
        {
            m_task = task;
            m_callback = callback;
        }

        internal void Register()
        {
            try
            {
                m_task.RegisterWhenAnyObserver(Observe);
            }
            catch (Exception exception)
            {
                Report(OnityAsyncStreamOutcome.Failure(exception), default);
            }
        }

        private void Observe()
        {
            if (Interlocked.Exchange(ref m_observed, 1) != 0)
            {
                return;
            }
            T value = default;
            OnityAsyncStreamOutcome outcome;
            try
            {
                outcome = Read(m_task, out value);
            }
            catch (Exception exception)
            {
                outcome = OnityAsyncStreamOutcome.Failure(exception);
            }
            Report(outcome, value);
        }

        private void Report(OnityAsyncStreamOutcome outcome, T value)
        {
            if (Interlocked.Exchange(ref m_reported, 1) == 0)
            {
                m_callback(outcome, value);
            }
        }

        internal static OnityAsyncStreamOutcome Read(OnityTask<T> task, out T value)
        {
            OnityTaskSourceStatus status = task.ReadWhenAnyOutcome(
                out value, out Exception fault, out CancellationToken token);
            return new OnityAsyncStreamOutcome
            {
                Status = fault != null ? OnityTaskSourceStatus.Faulted : status,
                Fault = fault,
                Token = token
            };
        }
    }

    // Owns upstream and delegate observations separately from the finite base.
    // The base still commits public Current/end/disposal and iterates rejected items.
    internal abstract class OnityAwaitAsyncEnumerator<T, TDelegate, TOutput> : OnityAsyncEnumeratorBase<TOutput>
    {
        private enum Phase
        {
            Idle,
            Acquire,
            Move,
            WaitMove,
            Current,
            Token,
            Delegate,
            WaitDelegate
        }

        private enum Step
        {
            None,
            Acquire,
            Move,
            Current,
            Token,
            Delegate,
            PublishMove,
            CloseToken,
            DisposeUpstream,
            FinishCleanup
        }

        private readonly object m_gate = new object();
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly CancellationToken m_token;
        private readonly Action<OnityAsyncStreamOutcome> m_onMove;
        private readonly Action<OnityAsyncStreamOutcome, TDelegate> m_onDelegate;
        private readonly Action<OnityAsyncStreamOutcome> m_onCleanup;
        private readonly Action<OnityAsyncStreamOutcome> m_onQuiescence;
        private IOnityAsyncEnumerator<T> m_upstream;
        private OnityAsyncStreamTokenLifetime m_lifetime;
        private OnityTaskCompletionSource<bool> m_moveOutput;
        private OnityTaskCompletionSource m_cleanupOutput;
        private Phase m_phase;
        private int m_activeCalls;
        private bool m_driving;
        private bool m_requested;
        private bool m_working;
        private bool m_ready;
        private bool m_upstreamPending;
        private bool m_upstreamReady;
        private bool m_delegatePending;
        private bool m_delegateReady;
        private bool m_cleanupWanted;
        private bool m_tokenCloseStarted;
        private bool m_quiescent;
        private bool m_disposeStarted;
        private bool m_disposeSettled;
        private bool m_finished;
        private bool m_accepted;
        private T m_item;
        private TDelegate m_delegateValue;
        private TOutput m_selected;
        private OnityAsyncStreamOutcome m_moveOutcome;
        private OnityAsyncStreamOutcome m_upstreamOutcome;
        private OnityAsyncStreamOutcome m_delegateOutcome;
        private OnityAsyncStreamOutcome m_cleanupOutcome;
        private OnityAsyncStreamOutcome m_quiescenceOutcome;

        protected OnityAwaitAsyncEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
        {
            m_source = source;
            m_token = token;
            m_onMove = OnMove;
            m_onDelegate = OnDelegate;
            m_onCleanup = OnCleanup;
            m_onQuiescence = OnQuiescence;
        }

        protected abstract OnityTask<TDelegate> Invoke(T item, CancellationToken token);
        protected abstract bool Select(T item, TDelegate result, out TOutput selected);

        protected override OnityTask<bool> MoveCore()
        {
            lock (m_gate)
            {
                if (m_cleanupWanted)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                m_working = true;
                m_ready = false;
                m_moveOutput = null;
                m_phase = m_upstream == null ? Phase.Acquire : Phase.Move;
            }
            Drive();
            OnityAsyncStreamOutcome outcome;
            lock (m_gate)
            {
                if (m_moveOutput != null)
                {
                    return m_moveOutput.Task;
                }
                if (!m_ready)
                {
                    m_moveOutput = new OnityTaskCompletionSource<bool>();
                    return m_moveOutput.Task;
                }
                outcome = m_moveOutcome;
            }
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                return OnityTask<bool>.FromResult(outcome.Value);
            }
            var failed = new OnityTaskCompletionSource<bool>();
            outcome.Publish(failed, false);
            return failed.Task;
        }

        protected override bool ReadCurrentCore(out TOutput current)
        {
            lock (m_gate)
            {
                current = m_selected;
                m_selected = default;
                return m_accepted;
            }
        }

        protected override OnityTask CleanupCore()
        {
            OnityTask task;
            lock (m_gate)
            {
                m_cleanupWanted = true;
                m_cleanupOutput ??= new OnityTaskCompletionSource();
                task = m_cleanupOutput.Task;
            }
            Drive();
            return task;
        }

        private void OnMove(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_upstreamPending = false;
                m_upstreamReady = true;
                m_upstreamOutcome = outcome;
            }
            Drive();
        }

        private void OnDelegate(OnityAsyncStreamOutcome outcome, TDelegate value)
        {
            lock (m_gate)
            {
                m_delegatePending = false;
                m_delegateReady = true;
                m_delegateOutcome = outcome;
                m_delegateValue = value;
            }
            Drive();
        }

        private void OnCleanup(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_disposeSettled = true;
                m_cleanupOutcome = outcome;
            }
            Drive();
        }

        private void OnQuiescence(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_quiescent = true;
                m_quiescenceOutcome = outcome;
            }
            Drive();
        }

        private void CompleteMove(OnityAsyncStreamOutcome outcome)
        {
            m_working = false;
            m_ready = true;
            m_phase = Phase.Idle;
            m_moveOutcome = outcome;
        }

        private void Drive()
        {
            lock (m_gate)
            {
                if (m_driving)
                {
                    m_requested = true;
                    return;
                }
                m_driving = true;
                m_requested = false;
            }
            while (true)
            {
                Step step = Step.None;
                OnityTaskCompletionSource<bool> publication = null;
                OnityAsyncStreamOutcome outcome = default;
                lock (m_gate)
                {
                    if (m_activeCalls == 0 && m_cleanupWanted)
                    {
                        if (!m_tokenCloseStarted)
                        {
                            m_tokenCloseStarted = true;
                            if (m_lifetime == null)
                            {
                                m_quiescent = true;
                                m_quiescenceOutcome = OnityAsyncStreamOutcome.Success(false);
                            }
                            else
                            {
                                step = Step.CloseToken;
                            }
                        }
                        if (step == Step.None && !m_disposeStarted)
                        {
                            m_disposeStarted = true;
                            m_activeCalls++;
                            step = Step.DisposeUpstream;
                        }
                        else if (step == Step.None && m_working && !m_upstreamPending && !m_delegatePending)
                        {
                            CompleteMove(OnityAsyncStreamOutcome.Success(false));
                        }
                        else if (step == Step.None && m_moveOutput == null && !m_finished && m_disposeSettled && m_quiescent &&
                            !m_working && !m_upstreamPending && !m_delegatePending)
                        {
                            m_finished = true;
                            step = Step.FinishCleanup;
                        }
                    }
                    else if (m_activeCalls == 0 && m_working)
                    {
                        if (m_phase == Phase.WaitMove && m_upstreamReady)
                        {
                            m_upstreamReady = false;
                            if (m_upstreamOutcome.Status != OnityTaskSourceStatus.Succeeded || !m_upstreamOutcome.Value)
                            {
                                CompleteMove(m_upstreamOutcome);
                            }
                            else
                            {
                                m_phase = Phase.Current;
                            }
                        }
                        if (m_phase == Phase.WaitDelegate && m_delegateReady)
                        {
                            m_delegateReady = false;
                            if (m_delegateOutcome.Status != OnityTaskSourceStatus.Succeeded)
                            {
                                CompleteMove(m_delegateOutcome);
                            }
                            else
                            {
                                m_accepted = Select(m_item, m_delegateValue, out m_selected);
                                m_item = default;
                                m_delegateValue = default;
                                CompleteMove(OnityAsyncStreamOutcome.Success(true));
                            }
                        }
                        if (m_working)
                        {
                            step = m_phase == Phase.Acquire ? Step.Acquire
                                : m_phase == Phase.Move ? Step.Move
                                : m_phase == Phase.Current ? Step.Current
                                : m_phase == Phase.Token ? Step.Token
                                : m_phase == Phase.Delegate ? Step.Delegate : Step.None;
                            if (step != Step.None)
                            {
                                m_activeCalls++;
                            }
                        }
                    }
                    if (step == Step.None && m_ready && m_moveOutput != null)
                    {
                        publication = m_moveOutput;
                        m_moveOutput = null;
                        outcome = m_moveOutcome;
                        // Do not hold driving ownership through a base callback
                        // that can reenter Dispose and synchronously cancel work.
                        m_driving = false;
                        step = Step.PublishMove;
                    }
                    else if (step == Step.CloseToken || step == Step.FinishCleanup)
                    {
                        m_driving = false;
                    }
                    else if (step == Step.None)
                    {
                        if (m_requested)
                        {
                            m_requested = false;
                            continue;
                        }
                        m_driving = false;
                        return;
                    }
                }
                if (step == Step.PublishMove)
                {
                    outcome.Publish(publication, outcome.Value);
                    Drive();
                    return;
                }
                if (step == Step.CloseToken)
                {
                    CloseToken();
                    Drive();
                    return;
                }
                if (step == Step.FinishCleanup)
                {
                    FinishCleanup();
                    return;
                }
                Run(step);
            }
        }

        private void Run(Step step)
        {
            try
            {
                if (step != Step.DisposeUpstream && IsClosing)
                {
                    lock (m_gate)
                    {
                        CompleteMove(OnityAsyncStreamOutcome.Success(false));
                    }
                    return;
                }
                if (step == Step.Acquire)
                {
                    IOnityAsyncEnumerator<T> upstream = m_source.GetAsyncEnumerator(m_token);
                    if (upstream == null)
                    {
                        throw new InvalidOperationException("The upstream description returned a null enumerator.");
                    }
                    lock (m_gate)
                    {
                        m_upstream = upstream;
                        m_phase = Phase.Move;
                    }
                }
                else if (step == Step.Move)
                {
                    lock (m_gate)
                    {
                        m_phase = Phase.WaitMove;
                        m_upstreamPending = true;
                    }
                    OnityTask<bool> task = m_upstream.MoveNextAsync();
                    if (task.IsCompleted)
                    {
                        OnMove(OnityAsyncStreamOutcome.Read(task));
                    }
                    else
                    {
                        new OnityAsyncStreamMoveObserver(task, m_onMove).Register();
                    }
                }
                else if (step == Step.Current)
                {
                    T item = m_upstream.Current;
                    lock (m_gate)
                    {
                        m_item = item;
                        m_phase = Phase.Token;
                    }
                }
                else if (step == Step.Token)
                {
                    if (m_lifetime == null)
                    {
                        var lifetime = new OnityAsyncStreamTokenLifetime();
                        lock (m_gate)
                        {
                            m_lifetime = lifetime;
                        }
                        lifetime.Initialize(m_token);
                    }
                    lock (m_gate)
                    {
                        m_phase = Phase.Delegate;
                    }
                }
                else if (step == Step.Delegate)
                {
                    CancellationToken token = m_lifetime.Token;
                    if (token.IsCancellationRequested)
                    {
                        lock (m_gate)
                        {
                            CompleteMove(new OnityAsyncStreamOutcome
                            {
                                Status = OnityTaskSourceStatus.Canceled,
                                Token = token
                            });
                        }
                        return;
                    }
                    lock (m_gate)
                    {
                        m_phase = Phase.WaitDelegate;
                        m_delegatePending = true;
                    }
                    OnityTask<TDelegate> task = Invoke(m_item, token);
                    if (task.IsCompleted)
                    {
                        OnityAsyncStreamOutcome outcome = OnityAwaitStreamObserver<TDelegate>.Read(task, out TDelegate value);
                        OnDelegate(outcome, value);
                    }
                    else
                    {
                        new OnityAwaitStreamObserver<TDelegate>(task, m_onDelegate).Register();
                    }
                }
                else if (step == Step.DisposeUpstream)
                {
                    OnityTask task = m_upstream != null ? m_upstream.DisposeAsync() : OnityTask.Completed;
                    if (task.IsCompleted)
                    {
                        OnCleanup(OnityAsyncStreamOutcome.Read(task));
                    }
                    else
                    {
                        new OnityAsyncStreamCleanupObserver(task, m_onCleanup).Register();
                    }
                }
            }
            catch (Exception exception)
            {
                if (step == Step.DisposeUpstream)
                {
                    OnCleanup(OnityAsyncStreamOutcome.Failure(exception));
                }
                else
                {
                    lock (m_gate)
                    {
                        m_upstreamPending = false;
                        m_delegatePending = false;
                        CompleteMove(OnityAsyncStreamOutcome.Failure(exception));
                    }
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_activeCalls--;
                }
            }
        }

        private void CloseToken()
        {
            bool pinned = false;
            try
            {
                OnityTask task = m_lifetime.Close();
                lock (m_gate)
                {
                    m_activeCalls++;
                    pinned = true;
                }
                if (task.IsCompleted)
                {
                    OnQuiescence(OnityAsyncStreamOutcome.Read(task));
                }
                else
                {
                    new OnityAsyncStreamCleanupObserver(task, m_onQuiescence).Register();
                }
            }
            catch (Exception exception)
            {
                OnQuiescence(OnityAsyncStreamOutcome.Failure(exception));
            }
            finally
            {
                if (pinned)
                {
                    lock (m_gate)
                    {
                        m_activeCalls--;
                    }
                }
            }
        }

        private void FinishCleanup()
        {
            OnityAsyncStreamOutcome outcome;
            OnityTaskCompletionSource output;
            lock (m_gate)
            {
                outcome = m_cleanupOutcome.Status == OnityTaskSourceStatus.Succeeded
                    ? m_quiescenceOutcome : m_cleanupOutcome;
                output = m_cleanupOutput;
                m_upstream = null;
                m_item = default;
                m_delegateValue = default;
                m_selected = default;
            }
            try
            {
                m_lifetime?.Dispose();
            }
            catch (Exception exception)
            {
                outcome = OnityAsyncStreamOutcome.Failure(exception);
            }
            outcome.Publish<bool>(output, true);
        }
    }

    internal sealed class OnitySelectAwaitEnumerable<T, TResult> : IOnityAsyncEnumerable<TResult>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Func<T, CancellationToken, OnityTask<TResult>> m_selector;

        internal OnitySelectAwaitEnumerable(IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<TResult>> selector)
        {
            m_source = source;
            m_selector = selector;
        }

        public IOnityAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(m_source, m_selector, cancellationToken);

        private sealed class Enumerator : OnityAwaitAsyncEnumerator<T, TResult, TResult>
        {
            private readonly Func<T, CancellationToken, OnityTask<TResult>> m_selector;

            internal Enumerator(IOnityAsyncEnumerable<T> source,
                Func<T, CancellationToken, OnityTask<TResult>> selector, CancellationToken token) : base(source, token)
            {
                m_selector = selector;
            }

            protected override OnityTask<TResult> Invoke(T item, CancellationToken token) => m_selector(item, token);
            protected override bool Select(T item, TResult result, out TResult selected)
            {
                selected = result;
                return true;
            }
        }
    }

    internal sealed class OnityWhereAwaitEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Func<T, CancellationToken, OnityTask<bool>> m_predicate;

        internal OnityWhereAwaitEnumerable(IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask<bool>> predicate)
        {
            m_source = source;
            m_predicate = predicate;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(m_source, m_predicate, cancellationToken);

        private sealed class Enumerator : OnityAwaitAsyncEnumerator<T, bool, T>
        {
            private readonly Func<T, CancellationToken, OnityTask<bool>> m_predicate;

            internal Enumerator(IOnityAsyncEnumerable<T> source,
                Func<T, CancellationToken, OnityTask<bool>> predicate, CancellationToken token) : base(source, token)
            {
                m_predicate = predicate;
            }

            protected override OnityTask<bool> Invoke(T item, CancellationToken token) => m_predicate(item, token);
            protected override bool Select(T item, bool result, out T selected)
            {
                selected = result ? item : default;
                return result;
            }
        }
    }

    internal sealed class OnityForEachAsyncEnumerator<T> : OnityAwaitAsyncEnumerator<T, bool, bool>
    {
        private readonly Func<T, CancellationToken, OnityTask> m_action;

        internal OnityForEachAsyncEnumerator(IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask> action, CancellationToken token) : base(source, token)
        {
            m_action = action;
        }

        protected override OnityTask<bool> Invoke(T item, CancellationToken token)
        {
            return OnityAwaitStreamCompletion.Map(m_action(item, token));
        }

        protected override bool Select(T item, bool result, out bool selected)
        {
            selected = false;
            return false;
        }

        protected override bool ReadEndCore(out bool current)
        {
            current = true;
            return true;
        }
    }

    internal static class OnityAwaitStreamCompletion
    {
        internal static OnityTask<bool> Map(OnityTask task)
        {
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = OnityAsyncStreamOutcome.Read(task);
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask<bool>.FromResult(true);
                }
                var failed = new OnityTaskCompletionSource<bool>();
                outcome.Publish(failed, true);
                return failed.Task;
            }
            var output = new OnityTaskCompletionSource<bool>();
            new OnityAsyncStreamCleanupObserver(task, outcome => outcome.Publish(output, true)).Register();
            return output.Task;
        }

        internal static OnityTask Read(OnityTask<bool> task)
        {
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = OnityAsyncStreamOutcome.Read(task);
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask.Completed;
                }
                var failed = new OnityTaskCompletionSource();
                outcome.Publish<bool>(failed, true);
                return failed.Task;
            }
            var output = new OnityTaskCompletionSource();
            new OnityAsyncStreamMoveObserver(task, outcome => outcome.Publish<bool>(output, true)).Register();
            return output.Task;
        }
    }
}
