using System;
using System.Threading;
using Onity.Reactive;

namespace Onity.Unity.Async
{
    internal sealed class OnityPushAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityObservable<T> m_source;
        private readonly int m_capacity;

        internal OnityPushAsyncEnumerable(IOnityObservable<T> source, int capacity)
        {
            m_source = source;
            m_capacity = capacity;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new Enumerator(m_source, m_capacity, cancellationToken);

        private sealed class Enumerator : OnityAsyncEnumeratorBase<T>
        {
            private sealed class Observer : OnityObserver<T>
            {
                private readonly Enumerator m_owner;

                internal Observer(Enumerator owner)
                {
                    m_owner = owner;
                }

                protected override void OnNextCore(T value) => m_owner.OnValue(value);
                protected override void OnErrorCore(Exception exception) => m_owner.OnTerminal(exception);
                protected override void OnCompletedCore(OnityResult result) =>
                    m_owner.OnTerminal(result.IsFailure ? result.Exception : null);
            }

            private static readonly Action<object> s_cancel = state => ((Enumerator)state).OnCancel();
            private readonly object m_gate = new object();
            private readonly IOnityObservable<T> m_source;
            private readonly CancellationToken m_token;
            private readonly Observer m_observer;
            private readonly T[] m_items;
            private int m_head;
            private int m_count;
            private int m_callbacks;
            private bool m_started;
            private bool m_initializing;
            private bool m_accepting;
            private bool m_canceled;
            private bool m_terminalReserved;
            private bool m_detachWanted;
            private bool m_handleDetached;
            private bool m_tokenDetached;
            private bool m_cleanupWanted;
            private bool m_cleanupFinished;
            private bool m_cleanupDriving;
            private bool m_cleanupRequested;
            private IDisposable m_subscription;
            private CancellationTokenRegistration m_registration;
            private OnityTaskCompletionSource<bool> m_waiter;
            private OnityTaskCompletionSource m_cleanup;
            private Exception m_error;
            private Exception m_cleanupFailure;
            private OnityAsyncStreamOutcome m_terminal;

            internal Enumerator(IOnityObservable<T> source, int capacity, CancellationToken token)
            {
                m_source = source;
                m_token = token;
                m_items = new T[capacity];
                m_observer = new Observer(this);
            }

            protected override OnityTask<bool> MoveCore()
            {
                if (IsClosing)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                bool initialize = false;
                lock (m_gate)
                {
                    if (!m_started && !m_cleanupWanted)
                    {
                        m_started = true;
                        if (m_token.IsCancellationRequested)
                        {
                            m_canceled = true;
                            m_detachWanted = true;
                        }
                        else
                        {
                            m_initializing = true;
                            m_accepting = true;
                            initialize = true;
                        }
                    }
                }
                if (initialize)
                {
                    Initialize();
                }
                OnityAsyncStreamOutcome outcome;
                lock (m_gate)
                {
                    if (m_cleanupWanted)
                    {
                        return OnityTask<bool>.FromResult(false);
                    }
                    if (m_terminalReserved)
                    {
                        outcome = m_terminal;
                    }
                    else if (m_canceled)
                    {
                        outcome = ReserveTerminal(Canceled());
                    }
                    else if (m_count != 0)
                    {
                        return OnityTask<bool>.FromResult(true);
                    }
                    else if (!m_accepting)
                    {
                        outcome = ReserveTerminal(m_error == null ? OnityAsyncStreamOutcome.Success(false)
                            : OnityAsyncStreamOutcome.Failure(m_error));
                    }
                    else
                    {
                        m_waiter = new OnityTaskCompletionSource<bool>();
                        return m_waiter.Task;
                    }
                }
                DriveCleanup();
                return OutcomeTask(outcome);
            }

            private OnityAsyncStreamOutcome Canceled() => new OnityAsyncStreamOutcome
            {
                Status = OnityTaskSourceStatus.Canceled,
                Token = m_token
            };

            private OnityAsyncStreamOutcome ReserveTerminal(OnityAsyncStreamOutcome outcome)
            {
                m_terminalReserved = true;
                m_terminal = outcome;
                return outcome;
            }

            private static OnityTask<bool> OutcomeTask(OnityAsyncStreamOutcome outcome)
            {
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    return OnityTask<bool>.FromResult(outcome.Value);
                }
                var source = new OnityTaskCompletionSource<bool>();
                outcome.Publish(source, false);
                return source.Task;
            }

            private void Initialize()
            {
                CancellationTokenRegistration registration = default;
                IDisposable subscription = null;
                try
                {
                    if (m_token.CanBeCanceled)
                    {
                        if (ExecutionContext.IsFlowSuppressed())
                        {
                            registration = m_token.Register(s_cancel, this, false);
                        }
                        else
                        {
                            AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                            try
                            {
                                registration = m_token.Register(s_cancel, this, false);
                            }
                            finally
                            {
                                flow.Undo();
                            }
                        }
                    }
                    bool subscribe;
                    lock (m_gate)
                    {
                        subscribe = m_accepting && !m_cleanupWanted;
                    }
                    if (subscribe && !IsClosing)
                    {
                        // Initialization stays pinned through the user call AND
                        // handle assignment. A null handle intentionally means no-op.
                        subscription = m_source.Subscribe(m_observer);
                    }
                }
                catch (Exception exception)
                {
                    lock (m_gate)
                    {
                        if (!m_canceled && !m_cleanupWanted && !m_terminalReserved)
                        {
                            m_error = exception;
                            m_accepting = false;
                            m_detachWanted = true;
                        }
                    }
                    Wake();
                }
                finally
                {
                    lock (m_gate)
                    {
                        m_registration = registration;
                        m_subscription = subscription;
                        m_initializing = false;
                    }
                    DriveCleanup();
                }
            }

            private bool EnterCallback()
            {
                lock (m_gate)
                {
                    if (!m_accepting)
                    {
                        return false;
                    }
                    m_callbacks++;
                    return true;
                }
            }

            private void ExitCallback()
            {
                lock (m_gate)
                {
                    m_callbacks--;
                }
                DriveCleanup();
            }

            private void OnValue(T value)
            {
                if (!EnterCallback())
                {
                    return;
                }
                try
                {
                    lock (m_gate)
                    {
                        if (!m_accepting)
                        {
                            return;
                        }
                        if (m_count == m_items.Length)
                        {
                            m_error = new InvalidOperationException("The observable-to-stream buffer overflowed.");
                            m_accepting = false;
                            m_detachWanted = true;
                        }
                        else
                        {
                            m_items[(m_head + m_count) % m_items.Length] = value;
                            m_count++;
                        }
                    }
                    Wake();
                }
                finally
                {
                    ExitCallback();
                }
            }

            private void OnTerminal(Exception exception)
            {
                if (!EnterCallback())
                {
                    return;
                }
                try
                {
                    lock (m_gate)
                    {
                        if (!m_accepting)
                        {
                            return;
                        }
                        m_accepting = false;
                        m_error = exception;
                        m_detachWanted = true;
                    }
                    Wake();
                }
                finally
                {
                    ExitCallback();
                }
            }

            private void OnCancel()
            {
                lock (m_gate)
                {
                    if (m_tokenDetached)
                    {
                        return;
                    }
                    m_callbacks++;
                    if (!m_terminalReserved && !m_cleanupWanted)
                    {
                        m_canceled = true;
                        m_accepting = false;
                        m_detachWanted = true;
                        ClearItems();
                    }
                }
                try
                {
                    Wake();
                }
                finally
                {
                    ExitCallback();
                }
            }

            private void Wake()
            {
                OnityTaskCompletionSource<bool> source;
                OnityAsyncStreamOutcome outcome;
                lock (m_gate)
                {
                    source = m_waiter;
                    if (source == null)
                    {
                        return;
                    }
                    if (m_cleanupWanted)
                    {
                        outcome = OnityAsyncStreamOutcome.Success(false);
                    }
                    else if (m_canceled && !m_terminalReserved)
                    {
                        outcome = ReserveTerminal(Canceled());
                    }
                    else if (m_count != 0)
                    {
                        outcome = OnityAsyncStreamOutcome.Success(true);
                    }
                    else if (!m_accepting)
                    {
                        outcome = ReserveTerminal(m_error == null ? OnityAsyncStreamOutcome.Success(false)
                            : OnityAsyncStreamOutcome.Failure(m_error));
                    }
                    else
                    {
                        return;
                    }
                    m_waiter = null;
                }
                outcome.Publish(source, outcome.Value);
            }

            protected override bool ReadCurrentCore(out T current)
            {
                lock (m_gate)
                {
                    if (m_canceled || m_cleanupWanted || m_count == 0)
                    {
                        current = default;
                        return false;
                    }
                    current = m_items[m_head];
                    m_items[m_head] = default;
                    m_head = (m_head + 1) % m_items.Length;
                    m_count--;
                    return true;
                }
            }

            protected override OnityTask CleanupCore()
            {
                OnityTask task;
                lock (m_gate)
                {
                    m_cleanupWanted = true;
                    m_accepting = false;
                    m_detachWanted = true;
                    ClearItems();
                    m_cleanup ??= new OnityTaskCompletionSource();
                    task = m_cleanup.Task;
                }
                Wake();
                DriveCleanup();
                return task;
            }

            private void ClearItems()
            {
                while (m_count != 0)
                {
                    m_items[m_head] = default;
                    m_head = (m_head + 1) % m_items.Length;
                    m_count--;
                }
            }

            private void DriveCleanup()
            {
                lock (m_gate)
                {
                    if (m_cleanupDriving)
                    {
                        m_cleanupRequested = true;
                        return;
                    }
                    m_cleanupDriving = true;
                }
                while (true)
                {
                    IDisposable subscription = null;
                    CancellationTokenRegistration registration = default;
                    OnityTaskCompletionSource output = null;
                    Exception failure = null;
                    int step;
                    lock (m_gate)
                    {
                        if (m_initializing || m_callbacks != 0)
                        {
                            m_cleanupDriving = false;
                            return;
                        }
                        if (m_detachWanted && !m_handleDetached)
                        {
                            m_handleDetached = true;
                            subscription = m_subscription;
                            m_subscription = null;
                            step = 1;
                        }
                        else if ((m_cleanupWanted || m_canceled || m_terminalReserved) && !m_tokenDetached)
                        {
                            m_tokenDetached = true;
                            registration = m_registration;
                            m_registration = default;
                            step = 2;
                        }
                        else if (m_cleanupWanted && m_handleDetached && m_tokenDetached && !m_cleanupFinished)
                        {
                            m_cleanupFinished = true;
                            output = m_cleanup;
                            failure = m_cleanupFailure;
                            m_cleanupDriving = false;
                            step = 3;
                        }
                        else
                        {
                            if (m_cleanupRequested)
                            {
                                m_cleanupRequested = false;
                                continue;
                            }
                            m_cleanupDriving = false;
                            return;
                        }
                    }
                    if (step == 3)
                    {
                        if (failure != null)
                        {
                            output.TrySetFault(failure);
                        }
                        else
                        {
                            output.TrySetResult();
                        }
                        return;
                    }
                    try
                    {
                        if (step == 1)
                        {
                            m_observer.Dispose();
                            subscription?.Dispose();
                        }
                        else
                        {
                            registration.Dispose();
                        }
                    }
                    catch (Exception exception)
                    {
                        lock (m_gate)
                        {
                            m_cleanupFailure ??= exception;
                        }
                    }
                }
            }
        }
    }

    internal sealed class OnityPullObservable<T> : IOnityObservable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;

        internal OnityPullObservable(IOnityAsyncEnumerable<T> source)
        {
            m_source = source;
        }

        public IDisposable Subscribe(Observer<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            return Subscribe(new ValueObserver(observer));
        }

        public IDisposable Subscribe(OnityObserver<T> observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }
            var subscription = new Subscription(m_source, observer);
            subscription.Start();
            return subscription;
        }

        private sealed class ValueObserver : OnityObserver<T>
        {
            private readonly Observer<T> m_observer;

            internal ValueObserver(Observer<T> observer)
            {
                m_observer = observer;
            }

            protected override void OnNextCore(T value) => m_observer(value);
            protected override void OnErrorCore(Exception exception) => OnityObservableExceptionHandler.Publish(exception);
        }

        private sealed class Subscription : IDisposable
        {
            private enum Phase
            {
                Acquire,
                Move,
                WaitMove,
                Current,
                Notify
            }

            private enum Step
            {
                None,
                Acquire,
                Move,
                Current,
                Notify,
                CloseToken,
                DisposeUpstream,
                Terminal,
                DisposeObserver,
                Finish
            }

            private readonly object m_gate = new object();
            private readonly IOnityAsyncEnumerable<T> m_source;
            private readonly Action<OnityAsyncStreamOutcome> m_onMove;
            private readonly Action<OnityAsyncStreamOutcome> m_onCleanup;
            private readonly Action<OnityAsyncStreamOutcome> m_onQuiescence;
            private OnityObserver<T> m_observer;
            private IOnityAsyncEnumerator<T> m_upstream;
            private OnityAsyncStreamTokenLifetime m_lifetime;
            private Phase m_phase;
            private int m_activeCalls;
            private bool m_driving;
            private bool m_requested;
            private bool m_closing;
            private bool m_explicitDispose;
            private bool m_movePending;
            private bool m_moveReady;
            private bool m_closeStarted;
            private bool m_quiescent;
            private bool m_disposeStarted;
            private bool m_disposeSettled;
            private bool m_terminalStarted;
            private bool m_cleanupNotified;
            private bool m_observerDisposed;
            private bool m_finished;
            private T m_item;
            private OnityAsyncStreamOutcome m_moveOutcome;
            private OnityAsyncStreamOutcome m_terminalOutcome;
            private OnityAsyncStreamOutcome m_cleanupOutcome;
            private OnityAsyncStreamOutcome m_quiescenceOutcome;

            internal Subscription(IOnityAsyncEnumerable<T> source, OnityObserver<T> observer)
            {
                m_source = source;
                m_observer = observer;
                m_onMove = OnMove;
                m_onCleanup = OnCleanup;
                m_onQuiescence = OnQuiescence;
            }

            internal void Start() => Drive();

            public void Dispose()
            {
                lock (m_gate)
                {
                    m_closing = true;
                    m_explicitDispose = true;
                    m_item = default;
                }
                Drive();
            }

            private void OnMove(OnityAsyncStreamOutcome outcome)
            {
                lock (m_gate)
                {
                    m_movePending = false;
                    m_moveReady = true;
                    m_moveOutcome = outcome;
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
                    OnityObserver<T> observer = null;
                    T item = default;
                    OnityAsyncStreamOutcome outcome = default;
                    lock (m_gate)
                    {
                        if (!m_closing && m_moveReady)
                        {
                            m_moveReady = false;
                            if (m_moveOutcome.Status != OnityTaskSourceStatus.Succeeded || !m_moveOutcome.Value)
                            {
                                m_terminalOutcome = m_moveOutcome;
                                m_closing = true;
                            }
                            else
                            {
                                m_phase = Phase.Current;
                            }
                        }
                        if (m_activeCalls == 0 && m_closing)
                        {
                            if (m_explicitDispose && !m_observerDisposed)
                            {
                                m_observerDisposed = true;
                                observer = m_observer;
                                m_observer = null;
                                m_activeCalls++;
                                step = Step.DisposeObserver;
                            }
                            else if (!m_closeStarted)
                            {
                                m_closeStarted = true;
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
                            else if (step == Step.None && m_disposeSettled && m_quiescent && !m_movePending)
                            {
                                outcome = m_cleanupOutcome.Status != OnityTaskSourceStatus.Succeeded
                                    ? m_cleanupOutcome : m_quiescenceOutcome;
                                if (!m_explicitDispose && !m_terminalStarted)
                                {
                                    m_terminalStarted = true;
                                    if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                                    {
                                        outcome = m_terminalOutcome;
                                    }
                                    observer = m_observer;
                                    m_activeCalls++;
                                    step = Step.Terminal;
                                }
                                else if (!m_observerDisposed)
                                {
                                    m_observerDisposed = true;
                                    observer = m_observer;
                                    m_observer = null;
                                    m_activeCalls++;
                                    step = Step.DisposeObserver;
                                }
                                else if (!m_finished)
                                {
                                    m_finished = true;
                                    step = Step.Finish;
                                }
                            }
                        }
                        else if (m_activeCalls == 0 && !m_closing)
                        {
                            step = m_phase == Phase.Acquire ? Step.Acquire
                                : m_phase == Phase.Move ? Step.Move
                                : m_phase == Phase.Current ? Step.Current
                                : m_phase == Phase.Notify ? Step.Notify : Step.None;
                            if (step != Step.None)
                            {
                                m_activeCalls++;
                                if (step == Step.Notify)
                                {
                                    observer = m_observer;
                                    item = m_item;
                                    m_item = default;
                                    m_phase = Phase.Move;
                                }
                            }
                        }
                        if (step == Step.None)
                        {
                            if (m_requested)
                            {
                                m_requested = false;
                                continue;
                            }
                            m_driving = false;
                            return;
                        }
                        if (step == Step.CloseToken || step == Step.Finish)
                        {
                            m_driving = false;
                        }
                    }
                    if (step == Step.CloseToken)
                    {
                        CloseToken();
                        Drive();
                        return;
                    }
                    if (step == Step.Finish)
                    {
                        Finish(outcome);
                        return;
                    }
                    Run(step, observer, item, outcome);
                }
            }

            private void Run(Step step, OnityObserver<T> observer, T item, OnityAsyncStreamOutcome outcome)
            {
                try
                {
                    if (step == Step.Acquire)
                    {
                        var lifetime = new OnityAsyncStreamTokenLifetime();
                        lock (m_gate)
                        {
                            m_lifetime = lifetime;
                        }
                        lifetime.Initialize(default);
                        bool acquire;
                        lock (m_gate)
                        {
                            acquire = !m_closing;
                        }
                        if (acquire)
                        {
                            IOnityAsyncEnumerator<T> upstream = m_source.GetAsyncEnumerator(lifetime.Token);
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
                    }
                    else if (step == Step.Move)
                    {
                        lock (m_gate)
                        {
                            m_movePending = true;
                            m_phase = Phase.WaitMove;
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
                        T value = m_upstream.Current;
                        lock (m_gate)
                        {
                            m_item = value;
                            m_phase = Phase.Notify;
                        }
                    }
                    else if (step == Step.Notify)
                    {
                        observer.OnNext(item);
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
                    else if (step == Step.Terminal)
                    {
                        if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                        {
                            observer.OnCompleted(OnityResult.Success());
                        }
                        else
                        {
                            observer.OnError(Error(outcome));
                        }
                        lock (m_gate)
                        {
                            m_cleanupNotified = m_cleanupOutcome.Status != OnityTaskSourceStatus.Succeeded ||
                                m_quiescenceOutcome.Status != OnityTaskSourceStatus.Succeeded;
                        }
                    }
                    else if (step == Step.DisposeObserver)
                    {
                        observer?.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    if (step == Step.DisposeUpstream)
                    {
                        OnCleanup(OnityAsyncStreamOutcome.Failure(exception));
                    }
                    else if (step == Step.Notify || step == Step.Terminal || step == Step.DisposeObserver)
                    {
                        lock (m_gate)
                        {
                            m_closing = true;
                            m_explicitDispose = true;
                        }
                        OnityObservableExceptionHandler.Publish(exception);
                    }
                    else
                    {
                        lock (m_gate)
                        {
                            m_movePending = false;
                            m_closing = true;
                            m_terminalOutcome = OnityAsyncStreamOutcome.Failure(exception);
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

            private void Finish(OnityAsyncStreamOutcome cleanup)
            {
                bool report;
                lock (m_gate)
                {
                    report = m_explicitDispose && !m_cleanupNotified && cleanup.Status != OnityTaskSourceStatus.Succeeded;
                    m_upstream = null;
                    m_item = default;
                }
                try
                {
                    m_lifetime?.Dispose();
                }
                catch (Exception exception)
                {
                    OnityObservableExceptionHandler.Publish(exception);
                }
                if (report)
                {
                    OnityObservableExceptionHandler.Publish(Error(cleanup));
                }
            }

            private static Exception Error(OnityAsyncStreamOutcome outcome) =>
                outcome.Status == OnityTaskSourceStatus.Canceled
                    ? new OperationCanceledException(outcome.Token) : outcome.Fault;
        }
    }
}
