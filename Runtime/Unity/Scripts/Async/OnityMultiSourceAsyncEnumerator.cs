using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // One upstream of a multi-source enumeration. Only the coordinator's driver calls into the
    // upstream enumerator, one call at a time, so upstream calls never overlap with each other or
    // with cleanup; observers only record outcomes.
    internal abstract class OnityStreamSlot
    {
        internal int Index;
        internal bool Acquired;
        internal bool Moving;
        internal bool Ended;
        internal bool HasValue;
        internal bool WantMove;
        internal bool HasOutcome;
        internal bool DisposeStarted;
        internal bool Disposed;
        internal OnityAsyncStreamOutcome Outcome;
        internal OnityAsyncStreamOutcome DisposeOutcome;
        internal Action<OnityAsyncStreamOutcome> OnMove;
        internal Action<OnityAsyncStreamOutcome> OnDispose;

        internal abstract void Acquire(CancellationToken token);
        internal abstract OnityTask<bool> Move();
        internal abstract void Read();
        internal abstract OnityTask Dispose();
        internal abstract void Release();
    }

    internal sealed class OnityStreamSlot<T> : OnityStreamSlot
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private IOnityAsyncEnumerator<T> m_enumerator;
        internal T Value;

        internal OnityStreamSlot(IOnityAsyncEnumerable<T> source)
        {
            m_source = source;
        }

        internal override void Acquire(CancellationToken token)
        {
            IOnityAsyncEnumerator<T> enumerator = m_source.GetAsyncEnumerator(token);
            m_enumerator = enumerator ?? throw new InvalidOperationException(
                "The upstream description returned a null enumerator.");
        }

        internal override OnityTask<bool> Move() => m_enumerator.MoveNextAsync();

        internal override void Read()
        {
            Value = m_enumerator.Current;
            HasValue = true;
        }

        internal override OnityTask Dispose() => m_enumerator.DisposeAsync();

        internal override void Release()
        {
            m_enumerator = null;
            Value = default;
        }
    }

    // Coordinates several upstream enumerators (and optional external signals) behind one native
    // stream. A single driver loop serializes every upstream call, operator hook and publication:
    // observers and token callbacks only record outcomes and wake the driver, so synchronous
    // sources iterate without recursion. Outputs are queued in arrival order and handed to the
    // consumer one move at a time. Cleanup first settles a pending consumer move, then disposes
    // every acquired upstream in index order (all disposals start before any is awaited) and
    // waits until every upstream move, disposal and owned signal has settled. Every upstream
    // outcome is read once, so no fault is left unobserved; the first disposal failure in index
    // order takes precedence.
    internal abstract class OnityMultiSourceAsyncEnumerator<TResult> : OnityAsyncEnumeratorBase<TResult>
    {
        private enum EntryKind
        {
            Item,
            End,
            Failure
        }

        private struct Entry
        {
            internal EntryKind Kind;
            internal TResult Value;
            internal OnityAsyncStreamOutcome Outcome;
        }

        private struct Signal
        {
            internal bool Watched;
            internal bool Observed;
            internal bool Recorded;
            internal bool WaitAtCleanup;
            internal OnityAsyncStreamOutcome Outcome;
            internal CancellationTokenRegistration Registration;
        }

        private enum Step
        {
            None,
            Handoff,
            Start,
            Signal,
            Item,
            End,
            Fault,
            Request,
            Move,
            CloseSignals,
            Dispose,
            PublishCleanup
        }

        private sealed class SignalCallback
        {
            internal readonly OnityMultiSourceAsyncEnumerator<TResult> Owner;
            internal readonly int Id;
            internal readonly CancellationToken Token;

            internal SignalCallback(OnityMultiSourceAsyncEnumerator<TResult> owner, int id, CancellationToken token)
            {
                Owner = owner;
                Id = id;
                Token = token;
            }

            internal void OnTask(OnityAsyncStreamOutcome outcome) => Owner.RecordSignal(Id, outcome);
        }

        private static readonly Action<object> s_onToken = state =>
        {
            var callback = (SignalCallback)state;
            callback.Owner.RecordSignal(callback.Id, OnityStageTasks.Canceled(callback.Token));
        };

        private readonly object m_gate = new object();
        private readonly OnityStreamSlot[] m_slots;
        private readonly Signal[] m_signals;
        private readonly CancellationToken m_token;
        private Queue<Entry> m_queue;
        private TResult m_current;
        private OnityTaskCompletionSource<bool> m_output;
        private OnityTaskCompletionSource m_cleanupSource;
        private OnityAsyncStreamTokenLifetime m_lifetime;
        private OnityAsyncStreamOutcome m_lifetimeOutcome;
        private bool m_lifetimeClosing;
        private bool m_lifetimeClosed;
        private bool m_driving;
        private bool m_again;
        private bool m_started;
        private bool m_startPending;
        private bool m_requestPending;
        private bool m_stopped;
        private bool m_closing;
        private bool m_signalsClosed;
        private bool m_cleanupDone;
        private OnityAsyncStreamOutcome m_cleanupOutcome;

        protected OnityMultiSourceAsyncEnumerator(OnityStreamSlot[] slots, int signalCount, CancellationToken token)
        {
            m_slots = slots;
            m_signals = signalCount > 0 ? new Signal[signalCount] : Array.Empty<Signal>();
            m_token = token;
            for (int i = 0; i < slots.Length; i++)
            {
                OnityStreamSlot slot = slots[i];
                slot.Index = i;
                slot.OnMove = outcome => RecordMove(slot, outcome);
                slot.OnDispose = outcome => RecordDispose(slot, outcome);
            }
        }

        protected CancellationToken Token => m_token;
        protected int SlotCount => m_slots.Length;
        protected OnityStreamSlot GetSlot(int index) => m_slots[index];

        // ---- operator hooks (driver only, outside the gate) ---------------------------------

        protected virtual void OnStart()
        {
        }

        protected abstract void OnRequest();
        protected abstract void OnItem(OnityStreamSlot slot);
        protected abstract void OnEnd(OnityStreamSlot slot);

        protected virtual void OnFault(OnityStreamSlot slot, OnityAsyncStreamOutcome outcome)
        {
            Fail(outcome);
        }

        protected virtual void OnSignal(int id, OnityAsyncStreamOutcome outcome)
        {
        }

        protected virtual bool CheckTokenOnMove => true;

        // ---- operator commands (driver only) ------------------------------------------------

        protected void RequestMove(OnityStreamSlot slot)
        {
            lock (m_gate)
            {
                if (!slot.Ended && !m_stopped)
                {
                    slot.WantMove = true;
                }
            }
        }

        protected bool IsStopped
        {
            get
            {
                lock (m_gate)
                {
                    return m_stopped;
                }
            }
        }

        protected void Emit(TResult value)
        {
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return;
                }
                Enqueue(new Entry { Kind = EntryKind.Item, Value = value });
            }
        }

        // Ends after every already emitted item.
        protected void Complete()
        {
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                Enqueue(new Entry { Kind = EntryKind.End });
            }
        }

        // Fails after every already emitted item.
        protected void Fail(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                Enqueue(new Entry { Kind = EntryKind.Failure, Outcome = outcome });
            }
        }

        // Discards undelivered items and fails (or ends) at the next move.
        protected void Abort(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (m_stopped)
                {
                    return;
                }
                m_stopped = true;
                m_queue?.Clear();
                Enqueue(outcome.Status == OnityTaskSourceStatus.Succeeded
                    ? new Entry { Kind = EntryKind.End }
                    : new Entry { Kind = EntryKind.Failure, Outcome = outcome });
            }
        }

        protected void WatchTask(int id, OnityTask task, bool waitAtCleanup)
        {
            var callback = new SignalCallback(this, id, default);
            lock (m_gate)
            {
                m_signals[id].Watched = true;
                m_signals[id].WaitAtCleanup = waitAtCleanup;
            }
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome;
                try
                {
                    outcome = OnityAsyncStreamOutcome.Read(task);
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                }
                RecordSignal(id, outcome);
            }
            else
            {
                new OnityAsyncStreamCleanupObserver(task, callback.OnTask).Register();
            }
        }

        protected void WatchToken(int id, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                return;
            }
            lock (m_gate)
            {
                m_signals[id].Watched = true;
            }
            if (token.IsCancellationRequested)
            {
                RecordSignal(id, OnityStageTasks.Canceled(token));
                return;
            }
            CancellationTokenRegistration registration = OnityStreamTokens.Register(
                token, s_onToken, new SignalCallback(this, id, token));
            bool dispose;
            lock (m_gate)
            {
                dispose = m_signalsClosed;
                if (!dispose)
                {
                    m_signals[id].Registration = registration;
                }
            }
            if (dispose)
            {
                registration.Dispose();
            }
        }

        // A cooperative token owned by this enumeration: it follows the enumeration token and is
        // canceled when cleanup starts; cleanup waits for its quiescence.
        protected CancellationToken CreateOwnedToken()
        {
            var lifetime = new OnityAsyncStreamTokenLifetime();
            lock (m_gate)
            {
                m_lifetime = lifetime;
            }
            lifetime.Initialize(m_token);
            return lifetime.Token;
        }

        // ---- base contract ------------------------------------------------------------------

        protected override OnityTask<bool> MoveCore()
        {
            if (IsClosing)
            {
                return OnityStageTasks.False;
            }
            if (CheckTokenOnMove && m_token.IsCancellationRequested)
            {
                return OnityStageTasks.Failed(OnityStageTasks.Canceled(m_token));
            }
            Entry entry;
            lock (m_gate)
            {
                if (TryDequeue(out entry))
                {
                    return ToTask(entry);
                }
                if (!m_started)
                {
                    m_started = true;
                    m_startPending = true;
                }
                m_requestPending = true;
            }
            Drive();
            lock (m_gate)
            {
                if (TryDequeue(out entry))
                {
                    return ToTask(entry);
                }
                m_output = new OnityTaskCompletionSource<bool>();
                return m_output.Task;
            }
        }

        protected override bool ReadCurrentCore(out TResult current)
        {
            lock (m_gate)
            {
                current = m_current;
                m_current = default;
                return true;
            }
        }

        protected override OnityTask CleanupCore()
        {
            lock (m_gate)
            {
                m_closing = true;
            }
            Drive();
            lock (m_gate)
            {
                if (m_cleanupDone)
                {
                    return m_cleanupOutcome.Status == OnityTaskSourceStatus.Succeeded
                        ? OnityTask.Completed : OnityStageTasks.Fail(m_cleanupOutcome);
                }
                m_cleanupSource = new OnityTaskCompletionSource();
                return m_cleanupSource.Task;
            }
        }

        protected override void FinishCleanupCore()
        {
            OnityAsyncStreamTokenLifetime lifetime;
            lock (m_gate)
            {
                for (int i = 0; i < m_slots.Length; i++)
                {
                    m_slots[i].Release();
                }
                m_queue?.Clear();
                m_current = default;
                lifetime = m_lifetime;
                m_lifetime = null;
            }
            lifetime?.Dispose();
        }

        // ---- recording (any thread) ---------------------------------------------------------

        private void RecordMove(OnityStreamSlot slot, OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                slot.HasOutcome = true;
                slot.Outcome = outcome;
            }
            Drive();
        }

        private void RecordDispose(OnityStreamSlot slot, OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                slot.Disposed = true;
                slot.DisposeOutcome = outcome;
            }
            Drive();
        }

        private void RecordSignal(int id, OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                if (m_signals[id].Observed)
                {
                    return;
                }
                m_signals[id].Observed = true;
                m_signals[id].Recorded = true;
                m_signals[id].Outcome = outcome;
            }
            Drive();
        }

        private void RecordLifetime(OnityAsyncStreamOutcome outcome)
        {
            lock (m_gate)
            {
                m_lifetimeClosed = true;
                m_lifetimeOutcome = outcome;
            }
            Drive();
        }

        // ---- driver -------------------------------------------------------------------------

        private void Drive()
        {
            lock (m_gate)
            {
                if (m_driving)
                {
                    m_again = true;
                    return;
                }
                m_driving = true;
                m_again = false;
            }

            while (true)
            {
                Step action = Step.None;
                OnityStreamSlot slot = null;
                int signal = -1;
                OnityAsyncStreamOutcome outcome = default;
                OnityTaskCompletionSource<bool> output = null;
                OnityTaskCompletionSource cleanup = null;
                Entry entry = default;
                lock (m_gate)
                {
                    if (m_output != null && (m_queue != null && m_queue.Count != 0 || m_closing))
                    {
                        output = m_output;
                        m_output = null;
                        if (!m_closing && TryDequeue(out entry))
                        {
                            action = Step.Handoff;
                        }
                        else
                        {
                            entry = new Entry { Kind = EntryKind.End };
                            action = Step.Handoff;
                        }
                    }
                    else if (m_closing)
                    {
                        action = NextCleanupAction(out slot, out cleanup);
                    }
                    else
                    {
                        action = NextAction(out slot, out signal, out outcome);
                    }

                    if (action == Step.None)
                    {
                        if (m_again)
                        {
                            m_again = false;
                            continue;
                        }
                        m_driving = false;
                        return;
                    }
                }

                Run(action, slot, signal, outcome, output, cleanup, entry);
            }
        }

        // Called under the gate.
        private Step NextAction(out OnityStreamSlot slot, out int signal, out OnityAsyncStreamOutcome outcome)
        {
            slot = null;
            signal = -1;
            outcome = default;
            if (m_startPending)
            {
                m_startPending = false;
                return Step.Start;
            }
            for (int i = 0; i < m_signals.Length; i++)
            {
                if (m_signals[i].Recorded)
                {
                    m_signals[i].Recorded = false;
                    if (m_stopped)
                    {
                        continue;
                    }
                    signal = i;
                    outcome = m_signals[i].Outcome;
                    return Step.Signal;
                }
            }
            for (int i = 0; i < m_slots.Length; i++)
            {
                OnityStreamSlot candidate = m_slots[i];
                if (!candidate.HasOutcome)
                {
                    continue;
                }
                candidate.HasOutcome = false;
                candidate.Moving = false;
                outcome = candidate.Outcome;
                candidate.Outcome = default;
                if (m_stopped)
                {
                    continue;
                }
                slot = candidate;
                if (outcome.Status != OnityTaskSourceStatus.Succeeded)
                {
                    return Step.Fault;
                }
                return outcome.Value ? Step.Item : Step.End;
            }
            if (m_requestPending)
            {
                m_requestPending = false;
                if (!m_stopped)
                {
                    return Step.Request;
                }
            }
            if (!m_stopped)
            {
                for (int i = 0; i < m_slots.Length; i++)
                {
                    OnityStreamSlot candidate = m_slots[i];
                    if (candidate.WantMove)
                    {
                        candidate.WantMove = false;
                        if (candidate.Moving || candidate.Ended)
                        {
                            continue;
                        }
                        candidate.Moving = true;
                        slot = candidate;
                        return Step.Move;
                    }
                }
            }
            return Step.None;
        }

        // Called under the gate.
        private Step NextCleanupAction(out OnityStreamSlot slot, out OnityTaskCompletionSource cleanup)
        {
            slot = null;
            cleanup = null;
            for (int i = 0; i < m_slots.Length; i++)
            {
                OnityStreamSlot candidate = m_slots[i];
                if (candidate.HasOutcome)
                {
                    // Already read (and so observed) by the recording observer; discarded.
                    candidate.HasOutcome = false;
                    candidate.Moving = false;
                    candidate.Outcome = default;
                }
                candidate.WantMove = false;
            }
            for (int i = 0; i < m_signals.Length; i++)
            {
                m_signals[i].Recorded = false;
            }
            if (!m_signalsClosed)
            {
                m_signalsClosed = true;
                return Step.CloseSignals;
            }
            for (int i = 0; i < m_slots.Length; i++)
            {
                OnityStreamSlot candidate = m_slots[i];
                if (candidate.Acquired && !candidate.DisposeStarted)
                {
                    candidate.DisposeStarted = true;
                    slot = candidate;
                    return Step.Dispose;
                }
            }
            if (m_cleanupDone || !IsQuiet())
            {
                return Step.None;
            }
            m_cleanupDone = true;
            m_cleanupOutcome = OnityAsyncStreamOutcome.Success(false);
            for (int i = 0; i < m_slots.Length; i++)
            {
                OnityStreamSlot candidate = m_slots[i];
                if (candidate.Disposed && candidate.DisposeOutcome.Status != OnityTaskSourceStatus.Succeeded)
                {
                    m_cleanupOutcome = candidate.DisposeOutcome;
                    break;
                }
            }
            if (m_cleanupOutcome.Status == OnityTaskSourceStatus.Succeeded && m_lifetimeClosed
                && m_lifetimeOutcome.Status != OnityTaskSourceStatus.Succeeded)
            {
                m_cleanupOutcome = m_lifetimeOutcome;
            }
            cleanup = m_cleanupSource;
            m_cleanupSource = null;
            return Step.PublishCleanup;
        }

        // Called under the gate.
        private bool IsQuiet()
        {
            for (int i = 0; i < m_slots.Length; i++)
            {
                OnityStreamSlot candidate = m_slots[i];
                if (candidate.Moving || (candidate.Acquired && !candidate.Disposed))
                {
                    return false;
                }
            }
            for (int i = 0; i < m_signals.Length; i++)
            {
                if (m_signals[i].Watched && m_signals[i].WaitAtCleanup && !m_signals[i].Observed)
                {
                    return false;
                }
            }
            return !m_lifetimeClosing || m_lifetimeClosed;
        }

        private void Run(Step action, OnityStreamSlot slot, int signal, OnityAsyncStreamOutcome outcome,
            OnityTaskCompletionSource<bool> output, OnityTaskCompletionSource cleanup, Entry entry)
        {
            switch (action)
            {
                case Step.Handoff:
                    Publish(output, entry);
                    break;
                case Step.Start:
                    Invoke(OnStart);
                    break;
                case Step.Signal:
                    try
                    {
                        OnSignal(signal, outcome);
                    }
                    catch (Exception exception)
                    {
                        Fail(OnityAsyncStreamOutcome.Failure(exception));
                    }
                    break;
                case Step.Item:
                    try
                    {
                        slot.Read();
                    }
                    catch (Exception exception)
                    {
                        slot.Ended = true;
                        Fail(OnityAsyncStreamOutcome.Failure(exception));
                        break;
                    }
                    try
                    {
                        OnItem(slot);
                    }
                    catch (Exception exception)
                    {
                        Fail(OnityAsyncStreamOutcome.Failure(exception));
                    }
                    break;
                case Step.End:
                    slot.Ended = true;
                    try
                    {
                        OnEnd(slot);
                    }
                    catch (Exception exception)
                    {
                        Fail(OnityAsyncStreamOutcome.Failure(exception));
                    }
                    break;
                case Step.Fault:
                    slot.Ended = true;
                    try
                    {
                        OnFault(slot, outcome);
                    }
                    catch (Exception exception)
                    {
                        Fail(OnityAsyncStreamOutcome.Failure(exception));
                    }
                    break;
                case Step.Request:
                    Invoke(OnRequest);
                    break;
                case Step.Move:
                    StartMove(slot);
                    break;
                case Step.CloseSignals:
                    CloseSignals();
                    break;
                case Step.Dispose:
                    StartDispose(slot);
                    break;
                case Step.PublishCleanup:
                    if (cleanup != null)
                    {
                        OnityAsyncStreamOutcome result;
                        lock (m_gate)
                        {
                            result = m_cleanupOutcome;
                        }
                        result.Publish<bool>(cleanup, true);
                    }
                    break;
            }
        }

        private void Invoke(Action hook)
        {
            try
            {
                hook();
            }
            catch (Exception exception)
            {
                Fail(OnityAsyncStreamOutcome.Failure(exception));
            }
        }

        private void StartMove(OnityStreamSlot slot)
        {
            OnityTask<bool> task;
            try
            {
                if (!slot.Acquired)
                {
                    slot.Acquire(m_token);
                    lock (m_gate)
                    {
                        slot.Acquired = true;
                    }
                }
                task = slot.Move();
            }
            catch (Exception exception)
            {
                RecordMove(slot, OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome;
                try
                {
                    outcome = OnityAsyncStreamOutcome.Read(task);
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                }
                RecordMove(slot, outcome);
            }
            else
            {
                new OnityAsyncStreamMoveObserver(task, slot.OnMove).Register();
            }
        }

        private void StartDispose(OnityStreamSlot slot)
        {
            OnityTask task;
            try
            {
                task = slot.Dispose();
            }
            catch (Exception exception)
            {
                RecordDispose(slot, OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome;
                try
                {
                    outcome = OnityAsyncStreamOutcome.Read(task);
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                }
                RecordDispose(slot, outcome);
            }
            else
            {
                new OnityAsyncStreamCleanupObserver(task, slot.OnDispose).Register();
            }
        }

        private void CloseSignals()
        {
            OnityAsyncStreamTokenLifetime lifetime;
            for (int i = 0; i < m_signals.Length; i++)
            {
                CancellationTokenRegistration registration;
                lock (m_gate)
                {
                    registration = m_signals[i].Registration;
                    m_signals[i].Registration = default;
                }
                registration.Dispose();
            }
            lock (m_gate)
            {
                lifetime = m_lifetime;
                m_lifetimeClosing = lifetime != null;
            }
            if (lifetime == null)
            {
                return;
            }
            OnityTask quiescence;
            try
            {
                quiescence = lifetime.Close();
            }
            catch (Exception exception)
            {
                RecordLifetime(OnityAsyncStreamOutcome.Failure(exception));
                return;
            }
            if (quiescence.IsCompleted)
            {
                RecordLifetime(OnityAsyncStreamOutcome.Read(quiescence));
            }
            else
            {
                new OnityAsyncStreamCleanupObserver(quiescence, RecordLifetime).Register();
            }
        }

        private void Publish(OnityTaskCompletionSource<bool> output, Entry entry)
        {
            if (entry.Kind == EntryKind.Item)
            {
                output.TrySetResult(true);
            }
            else if (entry.Kind == EntryKind.End)
            {
                output.TrySetResult(false);
            }
            else
            {
                entry.Outcome.Publish(output, false);
            }
        }

        // Called under the gate.
        private void Enqueue(Entry entry)
        {
            m_queue ??= new Queue<Entry>();
            m_queue.Enqueue(entry);
        }

        // Called under the gate. An item becomes the current value as it leaves the queue.
        private bool TryDequeue(out Entry entry)
        {
            if (m_queue == null || m_queue.Count == 0)
            {
                entry = default;
                return false;
            }
            entry = m_queue.Dequeue();
            m_current = entry.Kind == EntryKind.Item ? entry.Value : default;
            return true;
        }

        private static OnityTask<bool> ToTask(Entry entry)
        {
            if (entry.Kind == EntryKind.Item)
            {
                return OnityStageTasks.True;
            }
            if (entry.Kind == EntryKind.End)
            {
                return OnityStageTasks.False;
            }
            return OnityStageTasks.Failed(entry.Outcome);
        }
    }
}
