using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // Races the source against a completion signal: success ends the stream, a fault or
    // cancellation of the signal ends it the same way, and the enumeration token cancels it.
    internal sealed class OnityTakeUntilEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private const int k_cancel = 0;
        private const int k_other = 1;

        private readonly OnityStreamSlot<T> m_slot;
        private readonly OnityTask m_other;
        private readonly Func<CancellationToken, OnityTask> m_factory;

        private OnityTakeUntilEnumerator(OnityStreamSlot<T> slot, OnityTask other,
            Func<CancellationToken, OnityTask> factory, CancellationToken token)
            : base(new OnityStreamSlot[] { slot }, 2, token)
        {
            m_slot = slot;
            m_other = other;
            m_factory = factory;
        }

        internal static OnityTakeUntilEnumerator<T> Create(IOnityAsyncEnumerable<T> source, OnityTask other,
            Func<CancellationToken, OnityTask> factory, CancellationToken token)
        {
            return new OnityTakeUntilEnumerator<T>(new OnityStreamSlot<T>(source), other, factory, token);
        }

        protected override void OnStart()
        {
            WatchToken(k_cancel, Token);
            if (m_factory != null)
            {
                OnityTask other = m_factory(CreateOwnedToken());
                WatchTask(k_other, other, true);
            }
            else
            {
                WatchTask(k_other, m_other, false);
            }
        }

        protected override void OnRequest() => RequestMove(m_slot);
        protected override void OnItem(OnityStreamSlot slot) => Emit(m_slot.Value);
        protected override void OnEnd(OnityStreamSlot slot) => Complete();

        protected override void OnSignal(int id, OnityAsyncStreamOutcome outcome)
        {
            if (id == k_cancel)
            {
                Abort(outcome);
            }
            else if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                Complete();
            }
            else
            {
                Fail(outcome);
            }
        }
    }

    // Holds back the source until a completion signal succeeds, then relays it unchanged. The
    // source is acquired lazily and never moved before the signal.
    internal sealed class OnitySkipUntilEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private const int k_cancel = 0;
        private const int k_other = 1;

        private readonly OnityStreamSlot<T> m_slot;
        private readonly OnityTask m_other;
        private readonly Func<CancellationToken, OnityTask> m_factory;
        private bool m_open;
        private bool m_deferred;

        private OnitySkipUntilEnumerator(OnityStreamSlot<T> slot, OnityTask other,
            Func<CancellationToken, OnityTask> factory, CancellationToken token)
            : base(new OnityStreamSlot[] { slot }, 2, token)
        {
            m_slot = slot;
            m_other = other;
            m_factory = factory;
        }

        internal static OnitySkipUntilEnumerator<T> Create(IOnityAsyncEnumerable<T> source, OnityTask other,
            Func<CancellationToken, OnityTask> factory, CancellationToken token)
        {
            return new OnitySkipUntilEnumerator<T>(new OnityStreamSlot<T>(source), other, factory, token);
        }

        protected override void OnStart()
        {
            WatchToken(k_cancel, Token);
            if (m_factory != null)
            {
                OnityTask other = m_factory(CreateOwnedToken());
                WatchTask(k_other, other, true);
            }
            else
            {
                WatchTask(k_other, m_other, false);
            }
        }

        protected override void OnRequest()
        {
            if (m_open)
            {
                RequestMove(m_slot);
            }
            else
            {
                m_deferred = true;
            }
        }

        protected override void OnItem(OnityStreamSlot slot) => Emit(m_slot.Value);
        protected override void OnEnd(OnityStreamSlot slot) => Complete();

        protected override void OnSignal(int id, OnityAsyncStreamOutcome outcome)
        {
            if (id == k_cancel)
            {
                Abort(outcome);
                return;
            }
            if (outcome.Status != OnityTaskSourceStatus.Succeeded)
            {
                Fail(outcome);
                return;
            }
            m_open = true;
            if (m_deferred)
            {
                m_deferred = false;
                RequestMove(m_slot);
            }
        }
    }

    // Ends normally, instead of failing, once the operator token or the enumeration token is
    // canceled. Only the enumeration token is forwarded upstream.
    internal sealed class OnityTakeUntilCanceledEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private const int k_operator = 0;
        private const int k_enumeration = 1;

        private readonly OnityStreamSlot<T> m_slot;
        private readonly CancellationToken m_operatorToken;

        private OnityTakeUntilCanceledEnumerator(OnityStreamSlot<T> slot, CancellationToken operatorToken,
            CancellationToken token) : base(new OnityStreamSlot[] { slot }, 2, token)
        {
            m_slot = slot;
            m_operatorToken = operatorToken;
        }

        internal static OnityTakeUntilCanceledEnumerator<T> Create(IOnityAsyncEnumerable<T> source,
            CancellationToken operatorToken, CancellationToken token)
        {
            return new OnityTakeUntilCanceledEnumerator<T>(new OnityStreamSlot<T>(source), operatorToken, token);
        }

        protected override bool CheckTokenOnMove => false;

        protected override void OnStart()
        {
            WatchToken(k_operator, m_operatorToken);
            if (Token != m_operatorToken)
            {
                WatchToken(k_enumeration, Token);
            }
        }

        protected override void OnRequest() => RequestMove(m_slot);
        protected override void OnItem(OnityStreamSlot slot) => Emit(m_slot.Value);
        protected override void OnEnd(OnityStreamSlot slot) => Complete();
        protected override void OnSignal(int id, OnityAsyncStreamOutcome outcome) => Complete();

        protected override void OnFault(OnityStreamSlot slot, OnityAsyncStreamOutcome outcome)
        {
            if (outcome.Status == OnityTaskSourceStatus.Canceled
                && (m_operatorToken.IsCancellationRequested || Token.IsCancellationRequested))
            {
                Complete();
            }
            else
            {
                Fail(outcome);
            }
        }
    }

    // Holds back the source until the operator token is canceled; the enumeration token cancels.
    internal sealed class OnitySkipUntilCanceledEnumerator<T> : OnityMultiSourceAsyncEnumerator<T>
    {
        private const int k_enumeration = 0;
        private const int k_operator = 1;

        private readonly OnityStreamSlot<T> m_slot;
        private readonly CancellationToken m_operatorToken;
        private bool m_open;
        private bool m_deferred;

        private OnitySkipUntilCanceledEnumerator(OnityStreamSlot<T> slot, CancellationToken operatorToken,
            CancellationToken token) : base(new OnityStreamSlot[] { slot }, 2, token)
        {
            m_slot = slot;
            m_operatorToken = operatorToken;
        }

        internal static OnitySkipUntilCanceledEnumerator<T> Create(IOnityAsyncEnumerable<T> source,
            CancellationToken operatorToken, CancellationToken token)
        {
            return new OnitySkipUntilCanceledEnumerator<T>(new OnityStreamSlot<T>(source), operatorToken, token);
        }

        protected override void OnStart()
        {
            WatchToken(k_enumeration, Token);
            if (m_operatorToken != Token)
            {
                WatchToken(k_operator, m_operatorToken);
            }
        }

        protected override void OnRequest()
        {
            if (m_open)
            {
                RequestMove(m_slot);
            }
            else
            {
                m_deferred = true;
            }
        }

        protected override void OnItem(OnityStreamSlot slot) => Emit(m_slot.Value);
        protected override void OnEnd(OnityStreamSlot slot) => Complete();

        protected override void OnSignal(int id, OnityAsyncStreamOutcome outcome)
        {
            if (id == k_enumeration)
            {
                Abort(outcome);
                return;
            }
            m_open = true;
            if (m_deferred)
            {
                m_deferred = false;
                RequestMove(m_slot);
            }
        }
    }

    public static partial class OnityAsyncEnumerableLinq
    {
        // ---- TakeUntil / SkipUntil ---------------------------------------------------------

        /// <summary>Relays items until a signal task completes.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="other">Signal task. It is claimed (preserved) by this call so every enumeration
        /// can observe it; do not await the original task elsewhere.</param>
        /// <returns>A lazy description that ends when the signal succeeds, fails with the signal's fault or
        /// cancellation, and is canceled by its enumeration token.</returns>
        /// <remarks>The signal wins over an item that arrives at the same time; an already completed
        /// signal ends the stream before the source is acquired. A pending move ends as soon as the
        /// signal is observed, after the source is disposed. Every enumeration keeps observing the
        /// signal until it completes, so a signal that never completes retains one small observer per
        /// enumeration. No thread hop is added.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeUntil<T>(this IOnityAsyncEnumerable<T> source, OnityTask other)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityTask shared = other.Preserve();
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnityTakeUntilEnumerator<T>.Create(upstream, shared, null, token));
        }

        /// <summary>Relays items until a signal task created for each enumeration completes.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="other">Creates the signal at the first move. It receives a token owned by the
        /// enumeration: it follows the enumeration token and is canceled when the enumeration is
        /// disposed.</param>
        /// <returns>A lazy description that ends when the signal succeeds and fails with the signal's
        /// fault or cancellation.</returns>
        /// <remarks>Disposal cancels the signal's token and waits for the signal task to settle, so an
        /// uncooperative signal can retain cleanup. A throwing factory faults the stream.</remarks>
        /// <exception cref="ArgumentNullException">Source or factory is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeUntil<T>(this IOnityAsyncEnumerable<T> source,
            Func<CancellationToken, OnityTask> other)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(other, nameof(other));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnityTakeUntilEnumerator<T>.Create(upstream, default, other, token));
        }

        /// <summary>Ignores the source until a signal task succeeds, then relays it.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="other">Signal task. It is claimed (preserved) by this call so every enumeration
        /// can observe it; do not await the original task elsewhere.</param>
        /// <returns>A lazy description that fails with the signal's fault or cancellation.</returns>
        /// <remarks>Like UniTask, the source is not moved before the signal succeeds, so a buffered
        /// source keeps its earlier items and a pull-based source starts at its first item. A pending
        /// move waits for the signal and is canceled by the enumeration token.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipUntil<T>(this IOnityAsyncEnumerable<T> source, OnityTask other)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityTask shared = other.Preserve();
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnitySkipUntilEnumerator<T>.Create(upstream, shared, null, token));
        }

        /// <summary>Ignores the source until a signal task created for each enumeration succeeds.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="other">Creates the signal at the first move; it receives a token owned by the
        /// enumeration that is canceled when the enumeration is disposed.</param>
        /// <returns>A lazy description that fails with the signal's fault or cancellation.</returns>
        /// <remarks>Disposal cancels the signal's token and waits for the signal task to settle.</remarks>
        /// <exception cref="ArgumentNullException">Source or factory is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipUntil<T>(this IOnityAsyncEnumerable<T> source,
            Func<CancellationToken, OnityTask> other)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            OnityLinqCore.NotNull(other, nameof(other));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnitySkipUntilEnumerator<T>.Create(upstream, default, other, token));
        }

        /// <summary>Relays items until a token is canceled, then ends normally.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Token that ends the stream when canceled.</param>
        /// <returns>A lazy description that ends without an error when this token or the enumeration
        /// token is canceled, as UniTask does.</returns>
        /// <remarks>Only the enumeration token is passed upstream. A pending move ends with false; an
        /// upstream move canceled by one of these tokens also ends the stream normally. A token that is
        /// already canceled ends the stream before the source is acquired.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> TakeUntilCanceled<T>(this IOnityAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnityTakeUntilCanceledEnumerator<T>.Create(upstream, cancellationToken, token));
        }

        /// <summary>Ignores the source until a token is canceled, then relays it.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Token whose cancellation opens the stream.</param>
        /// <returns>A lazy description; the source is not moved before the token is canceled.</returns>
        /// <remarks>The enumeration token cancels a pending move (UniTask instead opens the stream and
        /// lets the upstream observe the canceled token). A token that can never be canceled keeps the
        /// stream waiting until the enumeration is canceled or disposed.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> SkipUntilCanceled<T>(this IOnityAsyncEnumerable<T> source,
            CancellationToken cancellationToken)
        {
            OnityLinqCore.NotNull(source, nameof(source));
            return new OnityLinqDescription<T, T>(source,
                (upstream, token) => OnitySkipUntilCanceledEnumerator<T>.Create(upstream, cancellationToken, token));
        }
    }
}
