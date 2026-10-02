using System;
using System.Threading;

namespace Onity.Unity.Async
{
    internal abstract class OnitySourceAsyncEnumerator<T, TResult> : OnityAsyncEnumeratorBase<TResult>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly CancellationToken m_token;
        private IOnityAsyncEnumerator<T> m_upstream;

        protected OnitySourceAsyncEnumerator(IOnityAsyncEnumerable<T> source, CancellationToken token)
        {
            m_source = source;
            m_token = token;
        }

        protected T UpstreamCurrent => m_upstream.Current;
        protected virtual CancellationToken GetToken() => m_token;

        protected override OnityTask<bool> MoveCore()
        {
            if (IsClosing)
            {
                return OnityTask<bool>.FromResult(false);
            }
            if (m_upstream == null)
            {
                m_upstream = m_source.GetAsyncEnumerator(GetToken());
                if (m_upstream == null)
                {
                    throw new InvalidOperationException("The upstream description returned a null enumerator.");
                }
            }
            if (IsClosing)
            {
                return OnityTask<bool>.FromResult(false);
            }
            return m_upstream.MoveNextAsync();
        }

        protected override OnityTask CleanupCore()
        {
            // Native DisposeAsync may settle the still-pending MoveNextAsync.
            return m_upstream != null ? m_upstream.DisposeAsync() : OnityTask.Completed;
        }

        protected override void FinishCleanupCore()
        {
            m_upstream = null;
        }
    }

    internal sealed class OnitySelectAsyncEnumerable<T, TResult> : IOnityAsyncEnumerable<TResult>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Func<T, TResult> m_selector;

        internal OnitySelectAsyncEnumerable(IOnityAsyncEnumerable<T> source, Func<T, TResult> selector)
        {
            m_source = source;
            m_selector = selector;
        }

        public IOnityAsyncEnumerator<TResult> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, m_selector, cancellationToken);
        }

        private sealed class Enumerator : OnitySourceAsyncEnumerator<T, TResult>
        {
            private readonly Func<T, TResult> m_selector;

            internal Enumerator(IOnityAsyncEnumerable<T> source, Func<T, TResult> selector,
                CancellationToken token) : base(source, token)
            {
                m_selector = selector;
            }

            protected override bool ReadCurrentCore(out TResult current)
            {
                T value = UpstreamCurrent;
                if (IsClosing)
                {
                    current = default;
                    return false;
                }
                current = m_selector(value);
                return true;
            }
        }
    }

    internal sealed class OnityWhereAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly Func<T, bool> m_predicate;

        internal OnityWhereAsyncEnumerable(IOnityAsyncEnumerable<T> source, Func<T, bool> predicate)
        {
            m_source = source;
            m_predicate = predicate;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, m_predicate, cancellationToken);
        }

        private sealed class Enumerator : OnitySourceAsyncEnumerator<T, T>
        {
            private readonly Func<T, bool> m_predicate;

            internal Enumerator(IOnityAsyncEnumerable<T> source, Func<T, bool> predicate,
                CancellationToken token) : base(source, token)
            {
                m_predicate = predicate;
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = UpstreamCurrent;
                return !IsClosing && m_predicate(current);
            }
        }
    }

    internal sealed class OnityTakeAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly int m_count;

        internal OnityTakeAsyncEnumerable(IOnityAsyncEnumerable<T> source, int count)
        {
            m_source = source;
            m_count = count;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, m_count, cancellationToken);
        }

        private sealed class Enumerator : OnitySourceAsyncEnumerator<T, T>
        {
            private int m_remaining;

            internal Enumerator(IOnityAsyncEnumerable<T> source, int count, CancellationToken token)
                : base(source, token)
            {
                m_remaining = count;
            }

            protected override bool EndAfterItem => m_remaining == 0;

            protected override bool ReadCurrentCore(out T current)
            {
                current = UpstreamCurrent;
                if (IsClosing)
                {
                    return false;
                }
                m_remaining--;
                return true;
            }
        }
    }

    internal sealed class OnityCancellationAsyncEnumerable<T> : IOnityAsyncEnumerable<T>
    {
        private readonly IOnityAsyncEnumerable<T> m_source;
        private readonly CancellationToken m_token;

        internal OnityCancellationAsyncEnumerable(IOnityAsyncEnumerable<T> source, CancellationToken token)
        {
            m_source = source;
            m_token = token;
        }

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(m_source, m_token, cancellationToken);
        }

        private sealed class Enumerator : OnitySourceAsyncEnumerator<T, T>
        {
            private readonly CancellationToken m_wrapperToken;
            private readonly CancellationToken m_enumerationToken;
            private CancellationTokenSource m_linked;

            internal Enumerator(IOnityAsyncEnumerable<T> source, CancellationToken wrapperToken,
                CancellationToken enumerationToken) : base(source, enumerationToken)
            {
                m_wrapperToken = wrapperToken;
                m_enumerationToken = enumerationToken;
            }

            protected override CancellationToken GetToken()
            {
                if (!m_wrapperToken.CanBeCanceled)
                {
                    return m_enumerationToken;
                }
                if (!m_enumerationToken.CanBeCanceled || m_wrapperToken == m_enumerationToken)
                {
                    return m_wrapperToken;
                }
                if (ExecutionContext.IsFlowSuppressed())
                {
                    m_linked = CancellationTokenSource.CreateLinkedTokenSource(m_wrapperToken, m_enumerationToken);
                }
                else
                {
                    AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                    try
                    {
                        m_linked = CancellationTokenSource.CreateLinkedTokenSource(m_wrapperToken, m_enumerationToken);
                    }
                    finally
                    {
                        flow.Undo();
                    }
                }
                return m_linked.Token;
            }

            protected override bool ReadCurrentCore(out T current)
            {
                current = UpstreamCurrent;
                return true;
            }

            protected override void FinishCleanupCore()
            {
                base.FinishCleanupCore();
                CancellationTokenSource linked = m_linked;
                m_linked = null;
                linked?.Dispose();
            }
        }
    }
}
