using System;
using System.Collections.Generic;
using System.Threading;

namespace Onity.Unity.Async
{
    // Normalized one-argument delegate of the stream operators: synchronous, awaited, or awaited
    // with the operator's owned cooperative token.
    internal readonly struct OnityStreamFunc<T, TResult>
    {
        private readonly Func<T, TResult> m_sync;
        private readonly Func<T, OnityTask<TResult>> m_await;
        private readonly Func<T, CancellationToken, OnityTask<TResult>> m_cancel;

        private OnityStreamFunc(Func<T, TResult> sync, Func<T, OnityTask<TResult>> awaiting,
            Func<T, CancellationToken, OnityTask<TResult>> cancel)
        {
            m_sync = sync;
            m_await = awaiting;
            m_cancel = cancel;
        }

        internal bool IsSync => m_sync != null;
        internal bool UsesToken => m_cancel != null;

        internal static OnityStreamFunc<T, TResult> Sync(Func<T, TResult> func) =>
            new OnityStreamFunc<T, TResult>(func, null, null);

        internal static OnityStreamFunc<T, TResult> Await(Func<T, OnityTask<TResult>> func) =>
            new OnityStreamFunc<T, TResult>(null, func, null);

        internal static OnityStreamFunc<T, TResult> Cancel(Func<T, CancellationToken, OnityTask<TResult>> func) =>
            new OnityStreamFunc<T, TResult>(null, null, func);

        internal TResult Invoke(T argument) => m_sync(argument);

        internal OnityTask<TResult> InvokeAsync(T argument, CancellationToken token) =>
            m_await != null ? m_await(argument) : m_cancel(argument, token);
    }

    // Normalized two-argument delegate of the stream operators.
    internal readonly struct OnityStreamFunc<T1, T2, TResult>
    {
        private readonly Func<T1, T2, TResult> m_sync;
        private readonly Func<T1, T2, OnityTask<TResult>> m_await;
        private readonly Func<T1, T2, CancellationToken, OnityTask<TResult>> m_cancel;

        private OnityStreamFunc(Func<T1, T2, TResult> sync, Func<T1, T2, OnityTask<TResult>> awaiting,
            Func<T1, T2, CancellationToken, OnityTask<TResult>> cancel)
        {
            m_sync = sync;
            m_await = awaiting;
            m_cancel = cancel;
        }

        internal bool IsSync => m_sync != null;
        internal bool UsesToken => m_cancel != null;

        internal static OnityStreamFunc<T1, T2, TResult> Sync(Func<T1, T2, TResult> func) =>
            new OnityStreamFunc<T1, T2, TResult>(func, null, null);

        internal static OnityStreamFunc<T1, T2, TResult> Await(Func<T1, T2, OnityTask<TResult>> func) =>
            new OnityStreamFunc<T1, T2, TResult>(null, func, null);

        internal static OnityStreamFunc<T1, T2, TResult> Cancel(
            Func<T1, T2, CancellationToken, OnityTask<TResult>> func) =>
            new OnityStreamFunc<T1, T2, TResult>(null, null, func);

        internal TResult Invoke(T1 first, T2 second) => m_sync(first, second);

        internal OnityTask<TResult> InvokeAsync(T1 first, T2 second, CancellationToken token) =>
            m_await != null ? m_await(first, second) : m_cancel(first, second, token);
    }

    internal abstract class OnityStageResource
    {
        internal abstract OnityTask DisposeAsync();
    }

    // An upstream enumerator owned by a staged enumerator.
    internal sealed class OnityStageEnumerator<T> : OnityStageResource
    {
        private readonly IOnityAsyncEnumerator<T> m_enumerator;

        internal OnityStageEnumerator(IOnityAsyncEnumerator<T> enumerator)
        {
            m_enumerator = enumerator;
        }

        internal T Current => m_enumerator.Current;
        internal OnityTask<bool> MoveNextAsync() => m_enumerator.MoveNextAsync();
        internal override OnityTask DisposeAsync() => m_enumerator.DisposeAsync();
    }

    // Sequential operators over several upstreams, written as a stage machine on top of
    // OnityAsyncEnumeratorBase: every upstream call and delegate invocation happens inside StepCore
    // or AdvanceCore, which the base never runs concurrently with each other or with cleanup.
    // StepCore returns the next asynchronous step (an upstream move, an awaited delegate or an
    // upstream release mapped to true); AdvanceCore consumes its result and either yields an item or
    // asks for another step. An upstream move returned unmapped ends the whole stream on false.
    // Cleanup cancels the owned delegate token, waits for an in-flight release, then disposes the
    // remaining upstreams in reverse acquisition order (or acquisition order when requested) and
    // waits for the token to become quiet. The first failure takes precedence.
    internal abstract class OnityStagedAsyncEnumerator<TResult> : OnityAsyncEnumeratorBase<TResult>
    {
        private readonly object m_gate = new object();
        private readonly CancellationToken m_token;
        private readonly List<OnityStageResource> m_owned = new List<OnityStageResource>(2);
        private OnityAsyncStreamTokenLifetime m_lifetime;
        private OnityTask m_releasing;
        private bool m_hasReleasing;

        protected OnityStagedAsyncEnumerator(CancellationToken token)
        {
            m_token = token;
        }

        protected CancellationToken Token => m_token;

        protected virtual bool DisposeInAcquisitionOrder => false;

        protected abstract OnityTask<bool> StepCore();
        protected abstract bool AdvanceCore(out TResult current);

        protected virtual bool EndCore(out TResult current)
        {
            current = default;
            return false;
        }

        protected virtual void OnFinish()
        {
        }

        protected OnityStageEnumerator<T> Acquire<T>(IOnityAsyncEnumerable<T> source)
        {
            IOnityAsyncEnumerator<T> enumerator = source.GetAsyncEnumerator(m_token);
            if (enumerator == null)
            {
                throw new InvalidOperationException("The upstream description returned a null enumerator.");
            }
            var owned = new OnityStageEnumerator<T>(enumerator);
            lock (m_gate)
            {
                m_owned.Add(owned);
            }
            return owned;
        }

        // Disposes an owned upstream as the next step; call CompleteRelease when it succeeded.
        protected OnityTask<bool> Release(OnityStageResource resource)
        {
            lock (m_gate)
            {
                m_owned.Remove(resource);
            }
            OnityTask task;
            try
            {
                task = resource.DisposeAsync().Preserve();
            }
            catch (Exception exception)
            {
                return OnityStageTasks.Failed(OnityAsyncStreamOutcome.Failure(exception));
            }
            lock (m_gate)
            {
                m_releasing = task;
                m_hasReleasing = true;
            }
            return OnityStageTasks.Continue(task);
        }

        protected void CompleteRelease()
        {
            lock (m_gate)
            {
                m_releasing = default;
                m_hasReleasing = false;
            }
        }

        // The cooperative token handed to token-aware delegates: it follows the enumeration token
        // and is canceled when cleanup starts.
        protected CancellationToken DelegateToken
        {
            get
            {
                OnityAsyncStreamTokenLifetime lifetime;
                lock (m_gate)
                {
                    lifetime = m_lifetime;
                }
                if (lifetime == null)
                {
                    lifetime = new OnityAsyncStreamTokenLifetime();
                    lock (m_gate)
                    {
                        m_lifetime = lifetime;
                    }
                    lifetime.Initialize(m_token);
                }
                return lifetime.Token;
            }
        }

        // Pre-checks cancellation, then invokes the delegate as one step.
        protected OnityTask<bool> Await<TArgument, TValue>(OnityStreamFunc<TArgument, TValue> func,
            TArgument argument, Action<TValue> store)
        {
            CancellationToken token = func.UsesToken ? DelegateToken : m_token;
            if (token.IsCancellationRequested)
            {
                return OnityStageTasks.Failed(OnityStageTasks.Canceled(token));
            }
            return OnityStageTasks.Continue(func.InvokeAsync(argument, token), store);
        }

        protected OnityTask<bool> Await<TFirst, TSecond, TValue>(OnityStreamFunc<TFirst, TSecond, TValue> func,
            TFirst first, TSecond second, Action<TValue> store)
        {
            CancellationToken token = func.UsesToken ? DelegateToken : m_token;
            if (token.IsCancellationRequested)
            {
                return OnityStageTasks.Failed(OnityStageTasks.Canceled(token));
            }
            return OnityStageTasks.Continue(func.InvokeAsync(first, second, token), store);
        }

        protected sealed override OnityTask<bool> MoveCore()
        {
            return IsClosing ? OnityStageTasks.False : StepCore();
        }

        protected sealed override bool ReadCurrentCore(out TResult current)
        {
            if (IsClosing)
            {
                current = default;
                return false;
            }
            return AdvanceCore(out current);
        }

        protected sealed override bool ReadEndCore(out TResult current)
        {
            return EndCore(out current);
        }

        protected sealed override OnityTask CleanupCore()
        {
            return OnityStageTasks.FromRun(CleanupAsync());
        }

        protected sealed override void FinishCleanupCore()
        {
            OnityAsyncStreamTokenLifetime lifetime;
            lock (m_gate)
            {
                lifetime = m_lifetime;
                m_lifetime = null;
                m_owned.Clear();
                m_releasing = default;
                m_hasReleasing = false;
            }
            try
            {
                lifetime?.Dispose();
            }
            finally
            {
                OnFinish();
            }
        }

        private async OnityTask<OnityAsyncStreamOutcome> CleanupAsync()
        {
            OnityAsyncStreamOutcome result = OnityAsyncStreamOutcome.Success(false);
            OnityAsyncStreamTokenLifetime lifetime;
            OnityTask releasing;
            bool hasReleasing;
            OnityStageResource[] owned;
            lock (m_gate)
            {
                lifetime = m_lifetime;
                releasing = m_releasing;
                hasReleasing = m_hasReleasing;
                owned = m_owned.ToArray();
            }

            OnityTask quiescence = OnityTask.Completed;
            if (lifetime != null)
            {
                try
                {
                    quiescence = lifetime.Close();
                }
                catch (Exception exception)
                {
                    quiescence = OnityStageTasks.Fail(OnityAsyncStreamOutcome.Failure(exception));
                }
            }
            if (hasReleasing)
            {
                Keep(ref result, await new OnityStreamOutcomeAwaiter(releasing));
            }
            for (int i = 0; i < owned.Length; i++)
            {
                OnityStageResource resource = owned[DisposeInAcquisitionOrder ? i : owned.Length - 1 - i];
                OnityTask disposal;
                try
                {
                    disposal = resource.DisposeAsync();
                }
                catch (Exception exception)
                {
                    Keep(ref result, OnityAsyncStreamOutcome.Failure(exception));
                    continue;
                }
                Keep(ref result, await new OnityStreamOutcomeAwaiter(disposal));
            }
            Keep(ref result, await new OnityStreamOutcomeAwaiter(quiescence));
            return result;
        }

        private static void Keep(ref OnityAsyncStreamOutcome result, OnityAsyncStreamOutcome outcome)
        {
            if (result.Status == OnityTaskSourceStatus.Succeeded && outcome.Status != OnityTaskSourceStatus.Succeeded)
            {
                result = outcome;
            }
        }
    }
}
