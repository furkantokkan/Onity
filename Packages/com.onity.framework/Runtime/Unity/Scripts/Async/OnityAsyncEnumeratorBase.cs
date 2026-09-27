using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal struct OnityAsyncStreamOutcome
    {
        internal OnityTaskSourceStatus Status;
        internal bool Value;
        internal Exception Fault;
        internal CancellationToken Token;

        internal static OnityAsyncStreamOutcome Success(bool value)
        {
            return new OnityAsyncStreamOutcome { Status = OnityTaskSourceStatus.Succeeded, Value = value };
        }

        internal static OnityAsyncStreamOutcome Failure(Exception fault)
        {
            return new OnityAsyncStreamOutcome { Status = OnityTaskSourceStatus.Faulted, Fault = fault };
        }

        internal static OnityAsyncStreamOutcome Read(OnityTask<bool> task)
        {
            OnityTaskSourceStatus status = task.ReadWhenAnyOutcome(
                out bool value, out Exception fault, out CancellationToken token);
            return new OnityAsyncStreamOutcome
            {
                Status = fault != null ? OnityTaskSourceStatus.Faulted : status,
                Value = value,
                Fault = fault,
                Token = token
            };
        }

        internal static OnityAsyncStreamOutcome Read(OnityTask task)
        {
            OnityTaskSourceStatus status = task.ReadWhenAnyOutcome(out Exception fault, out CancellationToken token);
            return new OnityAsyncStreamOutcome
            {
                Status = fault != null ? OnityTaskSourceStatus.Faulted : status,
                Fault = fault,
                Token = token
            };
        }

        internal void Publish<T>(OnityTaskCompletionSource<T> source, T result)
        {
            if (Status == OnityTaskSourceStatus.Faulted)
            {
                source.TrySetFault(Fault);
            }
            else if (Status == OnityTaskSourceStatus.Canceled)
            {
                source.TrySetCanceled(Token);
            }
            else
            {
                source.TrySetResult(result);
            }
        }
    }

    // Per-pending-operation observers retain a possibly accepted callback after a
    // registration throw. A late outcome is consumed, but never delivered twice.
    internal abstract class OnityAsyncStreamObserver
    {
        private Action<OnityAsyncStreamOutcome> m_callback;
        private readonly Action m_observe;
        private int m_observed;

        protected OnityAsyncStreamObserver(Action<OnityAsyncStreamOutcome> callback)
        {
            m_callback = callback;
            m_observe = Observe;
        }

        internal void Register()
        {
            try
            {
                RegisterCore(m_observe);
            }
            catch (Exception exception)
            {
                Report(OnityAsyncStreamOutcome.Failure(exception));
            }
        }

        private void Observe()
        {
            if (Interlocked.Exchange(ref m_observed, 1) != 0)
            {
                return;
            }
            OnityAsyncStreamOutcome outcome;
            try
            {
                outcome = ReadCore();
            }
            catch (Exception exception)
            {
                outcome = OnityAsyncStreamOutcome.Failure(exception);
            }
            ClearCore();
            Report(outcome);
        }

        private void Report(OnityAsyncStreamOutcome outcome)
        {
            Interlocked.Exchange(ref m_callback, null)?.Invoke(outcome);
        }

        protected abstract void RegisterCore(Action callback);
        protected abstract OnityAsyncStreamOutcome ReadCore();
        protected abstract void ClearCore();
    }

    internal sealed class OnityAsyncStreamMoveObserver : OnityAsyncStreamObserver
    {
        private OnityTask<bool> m_task;

        internal OnityAsyncStreamMoveObserver(OnityTask<bool> task,
            Action<OnityAsyncStreamOutcome> callback) : base(callback)
        {
            m_task = task;
        }

        protected override void RegisterCore(Action callback) => m_task.RegisterWhenAnyObserver(callback);
        protected override OnityAsyncStreamOutcome ReadCore() => OnityAsyncStreamOutcome.Read(m_task);
        protected override void ClearCore() => m_task = default;
    }

    internal sealed class OnityAsyncStreamCleanupObserver : OnityAsyncStreamObserver
    {
        private OnityTask m_task;

        internal OnityAsyncStreamCleanupObserver(OnityTask task,
            Action<OnityAsyncStreamOutcome> callback) : base(callback)
        {
            m_task = task;
        }

        protected override void RegisterCore(Action callback) => m_task.RegisterWhenAnyObserver(callback);
        protected override OnityAsyncStreamOutcome ReadCore() => OnityAsyncStreamOutcome.Read(m_task);
        protected override void ClearCore() => m_task = default;
    }

    internal abstract class OnityAsyncEnumeratorBase<T> : IOnityAsyncEnumerator<T>
    {
        private enum Step
        {
            Move,
            ReadItem,
            ReadEnd,
            Cleanup,
            FinishCleanup,
            PublishMove,
            PublishDispose
        }

        private readonly object m_gate = new object();
        private readonly Action<OnityAsyncStreamOutcome> m_onMove;
        private readonly Action<OnityAsyncStreamOutcome> m_onCleanup;
        private T m_current;
        private T m_proposedCurrent;
        private bool m_currentValid;
        private bool m_publicMoveCall;
        private bool m_moveBusy;
        private bool m_inputPending;
        private bool m_moveReady;
        private bool m_proposed;
        private bool m_lastItem;
        private bool m_terminal;
        private bool m_explicitDispose;
        private bool m_cleanupWanted;
        private bool m_cleanupStarted;
        private bool m_cleanupReady;
        private bool m_cleanupSettled;
        private bool m_cleanupFinished;
        private bool m_disposeCompleted;
        private bool m_driving;
        private bool m_requested;
        private int m_activeCalls;
        private OnityAsyncStreamOutcome m_readyOutcome;
        private OnityAsyncStreamOutcome m_proposal;
        private OnityAsyncStreamOutcome m_cleanupOutcome;
        private OnityTask<bool> m_lastMoveTask;
        private OnityTask<bool> m_terminalTask;
        private OnityTask m_disposeTask;
        private OnityTaskCompletionSource<bool> m_moveSource;
        private OnityTaskCompletionSource m_disposeSource;

        protected OnityAsyncEnumeratorBase()
        {
            m_onMove = OnMoveReady;
            m_onCleanup = OnCleanupReady;
        }

        public T Current
        {
            get
            {
                lock (m_gate)
                {
                    if (!m_currentValid)
                    {
                        throw new InvalidOperationException("The asynchronous enumerator has no current item.");
                    }
                    return m_current;
                }
            }
        }

        public OnityTask<bool> MoveNextAsync()
        {
            lock (m_gate)
            {
                if (m_publicMoveCall || m_moveBusy)
                {
                    throw new InvalidOperationException("An asynchronous move is already outstanding.");
                }
                m_current = default;
                m_currentValid = false;
                if (m_explicitDispose)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_terminal)
                {
                    return m_terminalTask;
                }
                m_publicMoveCall = true;
                m_moveBusy = true;
                m_proposed = false;
                m_lastItem = false;
                m_moveSource = null;
            }

            try
            {
                Drive();
                lock (m_gate)
                {
                    if (!m_moveBusy)
                    {
                        return m_lastMoveTask;
                    }
                    m_moveSource ??= new OnityTaskCompletionSource<bool>();
                    return m_moveSource.Task;
                }
            }
            finally
            {
                lock (m_gate)
                {
                    m_publicMoveCall = false;
                }
            }
        }

        public OnityTask DisposeAsync()
        {
            lock (m_gate)
            {
                m_explicitDispose = true;
                m_current = default;
                m_currentValid = false;
                m_cleanupWanted = true;
            }
            Drive();
            lock (m_gate)
            {
                if (m_disposeCompleted)
                {
                    return m_disposeTask;
                }
                m_disposeSource ??= new OnityTaskCompletionSource();
                return m_disposeSource.Task;
            }
        }

        protected abstract OnityTask<bool> MoveCore();
        protected abstract bool ReadCurrentCore(out T current);
        // The active-call pin protects cleanup; this query reserves acceptance for
        // each separate user invocation without calling that code under the gate.
        protected bool IsClosing
        {
            get
            {
                lock (m_gate)
                {
                    return m_explicitDispose;
                }
            }
        }

        protected virtual bool ReadEndCore(out T current)
        {
            current = default;
            return false;
        }
        protected virtual bool EndAfterItem => false;
        protected virtual OnityTask CleanupCore() => OnityTask.Completed;
        protected virtual void FinishCleanupCore()
        {
        }

        private void OnMoveReady(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_readyOutcome = outcome;
                m_moveReady = true;
            }
            Drive();
        }

        private void OnCleanupReady(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_cleanupOutcome = outcome;
                m_cleanupReady = true;
            }
            Drive();
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
                Step step;
                OnityTaskCompletionSource<bool> moveSource = null;
                OnityTaskCompletionSource disposeSource = null;
                OnityAsyncStreamOutcome publication = default;
                lock (m_gate)
                {
                    if (m_moveReady)
                    {
                        m_moveReady = false;
                        m_inputPending = false;
                        if (!m_explicitDispose && m_moveBusy)
                        {
                            if (m_readyOutcome.Status != OnityTaskSourceStatus.Succeeded)
                            {
                                m_proposal = m_readyOutcome;
                                m_proposed = true;
                                m_cleanupWanted = true;
                            }
                            else
                            {
                                step = m_readyOutcome.Value ? Step.ReadItem : Step.ReadEnd;
                                m_activeCalls++;
                                goto RunStep;
                            }
                        }
                    }
                    if (m_cleanupReady)
                    {
                        m_cleanupReady = false;
                        m_cleanupSettled = true;
                    }
                    if (m_explicitDispose && m_moveBusy && m_activeCalls == 0)
                    {
                        publication = OnityAsyncStreamOutcome.Success(false);
                        moveSource = CommitMove(publication, false);
                        step = Step.PublishMove;
                    }
                    else if (m_cleanupWanted && !m_cleanupStarted && m_activeCalls == 0)
                    {
                        m_cleanupStarted = true;
                        m_activeCalls++;
                        step = Step.Cleanup;
                    }
                    else if (m_cleanupSettled && !m_cleanupFinished && !m_inputPending && m_activeCalls == 0)
                    {
                        m_activeCalls++;
                        step = Step.FinishCleanup;
                    }
                    else if (!m_explicitDispose && m_moveBusy && m_proposed
                        && (!m_cleanupWanted || m_cleanupFinished))
                    {
                        publication = m_cleanupFinished && m_cleanupOutcome.Status != OnityTaskSourceStatus.Succeeded
                            ? m_cleanupOutcome : m_proposal;
                        moveSource = CommitMove(publication, m_lastItem);
                        step = Step.PublishMove;
                    }
                    else if (m_explicitDispose && m_cleanupFinished && !m_disposeCompleted)
                    {
                        publication = m_cleanupOutcome;
                        disposeSource = m_disposeSource;
                        if (publication.Status != OnityTaskSourceStatus.Succeeded)
                        {
                            disposeSource ??= new OnityTaskCompletionSource();
                        }
                        m_disposeTask = disposeSource != null ? disposeSource.Task : OnityTask.Completed;
                        m_disposeCompleted = true;
                        step = Step.PublishDispose;
                    }
                    else if (m_moveBusy && !m_explicitDispose && !m_proposed && !m_inputPending)
                    {
                        m_inputPending = true;
                        m_activeCalls++;
                        step = Step.Move;
                    }
                    else
                    {
                        if (m_requested)
                        {
                            m_requested = false;
                            continue;
                        }
                        m_driving = false;
                        return;
                    }
                RunStep:;
                }

                if (step == Step.Move)
                {
                    Move();
                }
                else if (step == Step.ReadItem || step == Step.ReadEnd)
                {
                    ReadItem(step == Step.ReadEnd);
                }
                else if (step == Step.Cleanup)
                {
                    Cleanup();
                }
                else if (step == Step.FinishCleanup)
                {
                    FinishCleanup();
                }
                else if (step == Step.PublishMove)
                {
                    if (moveSource != null)
                    {
                        publication.Publish(moveSource, publication.Value);
                    }
                }
                else if (disposeSource != null)
                {
                    publication.Publish<bool>(disposeSource, true);
                }
            }
        }

        // Called under the gate; the returned source is published after leaving it.
        private OnityTaskCompletionSource<bool> CommitMove(OnityAsyncStreamOutcome outcome, bool lastItem)
        {
            OnityTaskCompletionSource<bool> source = m_moveSource;
            if (outcome.Status != OnityTaskSourceStatus.Succeeded)
            {
                source ??= new OnityTaskCompletionSource<bool>();
            }
            m_lastMoveTask = source != null ? source.Task : OnityTask<bool>.FromResult(outcome.Value);
            m_moveBusy = false;
            m_currentValid = outcome.Status == OnityTaskSourceStatus.Succeeded && outcome.Value;
            m_current = m_currentValid ? m_proposedCurrent : default;
            m_proposedCurrent = default;
            if (outcome.Status != OnityTaskSourceStatus.Succeeded || !outcome.Value || lastItem)
            {
                m_terminal = true;
                m_terminalTask = outcome.Status == OnityTaskSourceStatus.Succeeded
                    ? OnityTask<bool>.FromResult(false) : m_lastMoveTask;
            }
            return source;
        }

        private void Move()
        {
            try
            {
                OnityTask<bool> task = MoveCore();
                if (task.IsCompleted)
                {
                    OnMoveReady(OnityAsyncStreamOutcome.Read(task));
                }
                else
                {
                    new OnityAsyncStreamMoveObserver(task, m_onMove).Register();
                }
            }
            catch (Exception exception)
            {
                OnMoveReady(OnityAsyncStreamOutcome.Failure(exception));
            }
            finally
            {
                lock (m_gate)
                {
                    m_activeCalls--;
                }
            }
        }

        private void ReadItem(bool end)
        {
            T current = default;
            bool accepted = false;
            bool last = end;
            Exception failure = null;
            try
            {
                accepted = end ? ReadEndCore(out current) : ReadCurrentCore(out current);
                last |= accepted && EndAfterItem;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            lock (m_gate)
            {
                m_activeCalls--;
                if (!m_explicitDispose)
                {
                    if (failure != null || end || accepted)
                    {
                        m_proposal = failure != null ? OnityAsyncStreamOutcome.Failure(failure)
                            : OnityAsyncStreamOutcome.Success(accepted);
                        m_proposedCurrent = current;
                        m_proposed = true;
                        m_lastItem = last;
                        m_cleanupWanted |= failure != null || last;
                    }
                }
            }
        }

        private void Cleanup()
        {
            try
            {
                OnityTask task = CleanupCore();
                if (task.IsCompleted)
                {
                    OnCleanupReady(OnityAsyncStreamOutcome.Read(task));
                }
                else
                {
                    new OnityAsyncStreamCleanupObserver(task, m_onCleanup).Register();
                }
            }
            catch (Exception exception)
            {
                OnCleanupReady(OnityAsyncStreamOutcome.Failure(exception));
            }
            finally
            {
                lock (m_gate)
                {
                    m_activeCalls--;
                }
            }
        }

        private void FinishCleanup()
        {
            Exception failure = null;
            try
            {
                FinishCleanupCore();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            lock (m_gate)
            {
                m_activeCalls--;
                if (failure != null)
                {
                    m_cleanupOutcome = OnityAsyncStreamOutcome.Failure(failure);
                }
                m_cleanupFinished = true;
            }
        }
    }
}
