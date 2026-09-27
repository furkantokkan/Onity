using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // All state and assignment pins use the channel gate. Registrations and
    // continuations are detached/published outside it. These owners are unpooled.
    internal abstract class OnityChannelWaiter
    {
        private sealed class TokenCallback
        {
            internal readonly OnityChannelWaiter Owner;
            internal readonly CancellationToken Token;

            internal TokenCallback(OnityChannelWaiter owner, CancellationToken token)
            {
                Owner = owner;
                Token = token;
            }
        }

        private static readonly Action<object> s_cancel = state =>
        {
            var callback = (TokenCallback)state;
            callback.Owner.Cancel(callback.Token);
        };

        private readonly object m_gate;
        private readonly CancellationToken m_firstToken;
        private readonly CancellationToken m_secondToken;
        private CancellationTokenRegistration m_firstRegistration;
        private CancellationTokenRegistration m_secondRegistration;
        private bool m_initializing = true;
        private bool m_finishing;
        private int m_callbacks;

        internal bool Committed;
        internal OnityAsyncStreamOutcome Outcome;

        protected OnityChannelWaiter(object gate, CancellationToken firstToken,
            CancellationToken secondToken = default)
        {
            m_gate = gate;
            m_firstToken = firstToken;
            m_secondToken = secondToken;
        }

        internal void Initialize()
        {
            CancellationTokenRegistration first = default;
            CancellationTokenRegistration second = default;
            try
            {
                if (ExecutionContext.IsFlowSuppressed())
                {
                    Register(ref first, ref second);
                }
                else
                {
                    AsyncFlowControl flow = ExecutionContext.SuppressFlow();
                    try
                    {
                        Register(ref first, ref second);
                    }
                    finally
                    {
                        flow.Undo();
                    }
                }
            }
            catch (Exception exception)
            {
                FailCore(exception);
            }
            finally
            {
                lock (m_gate)
                {
                    m_firstRegistration = first;
                    m_secondRegistration = second;
                    m_initializing = false;
                }
                Finish();
            }
        }

        private void Register(ref CancellationTokenRegistration first,
            ref CancellationTokenRegistration second)
        {
            if (m_firstToken.CanBeCanceled)
            {
                first = m_firstToken.Register(s_cancel, new TokenCallback(this, m_firstToken), false);
            }
            if (m_secondToken.CanBeCanceled && m_secondToken != m_firstToken)
            {
                second = m_secondToken.Register(s_cancel, new TokenCallback(this, m_secondToken), false);
            }
        }

        private void Cancel(CancellationToken token)
        {
            lock (m_gate)
            {
                if (m_finishing)
                {
                    return;
                }
                m_callbacks++;
            }
            try
            {
                CancelCore(token);
            }
            finally
            {
                lock (m_gate)
                {
                    m_callbacks--;
                }
                Finish();
            }
        }

        internal void Finish()
        {
            CancellationTokenRegistration first;
            CancellationTokenRegistration second;
            lock (m_gate)
            {
                if (!Committed || m_initializing || m_callbacks != 0 || m_finishing)
                {
                    return;
                }
                m_finishing = true;
                first = m_firstRegistration;
                second = m_secondRegistration;
                m_firstRegistration = default;
                m_secondRegistration = default;
            }
            try
            {
                first.Dispose();
            }
            finally
            {
                try
                {
                    second.Dispose();
                }
                finally
                {
                    PublishCore();
                }
            }
        }

        protected abstract void CancelCore(CancellationToken token);
        protected abstract void FailCore(Exception exception);
        protected abstract void PublishCore();
    }

    internal sealed class OnityChannelReadWaiter<T> : OnityChannelWaiter
    {
        private readonly OnityChannel<T> m_channel;
        internal readonly OnityChannelReader<T>.Enumerator Enumerator;
        internal readonly OnityTaskCompletionSource<T> DirectSource;
        internal readonly OnityTaskCompletionSource<bool> MoveSource;
        internal T Item;

        internal OnityChannelReadWaiter(OnityChannel<T> channel, CancellationToken token)
            : base(channel.Gate, token)
        {
            m_channel = channel;
            DirectSource = new OnityTaskCompletionSource<T>();
        }

        internal OnityChannelReadWaiter(OnityChannel<T> channel,
            OnityChannelReader<T>.Enumerator enumerator) : base(channel.Gate,
                enumerator.CapturedToken, enumerator.EnumerationToken)
        {
            m_channel = channel;
            Enumerator = enumerator;
            MoveSource = new OnityTaskCompletionSource<bool>();
            // Fresh pending built-in source: this context-free callback is first,
            // and sees terminal status before external native continuations run.
            MoveSource.Task.GetAwaiter().UnsafeOnCompleted(OnMovePublished);
        }

        private void OnMovePublished() => m_channel.EndPublication(Enumerator, MoveSource);

        protected override void CancelCore(CancellationToken token) => m_channel.CancelRead(this, token);
        protected override void FailCore(Exception exception) => m_channel.FailRead(this, exception);

        protected override void PublishCore()
        {
            // This is the outstanding-move publication boundary, not dequeue.
            m_channel.FinishRead(this);
            if (Enumerator == null)
            {
                T item = Item;
                Item = default;
                Outcome.Publish(DirectSource, item);
            }
            else
            {
                Item = default;
                Outcome.Publish(MoveSource, Outcome.Value);
            }
        }
    }

    internal sealed class OnityChannelWriteWaiter<T> : OnityChannelWaiter
    {
        private readonly OnityChannel<T> m_channel;
        internal readonly OnityTaskCompletionSource Source = new OnityTaskCompletionSource();
        internal OnityChannelWriteWaiter<T> Previous;
        internal OnityChannelWriteWaiter<T> Next;
        internal T Item;

        internal OnityChannelWriteWaiter(OnityChannel<T> channel, T item, CancellationToken token)
            : base(channel.Gate, token)
        {
            m_channel = channel;
            Item = item;
        }

        protected override void CancelCore(CancellationToken token) => m_channel.CancelWrite(this, token);
        protected override void FailCore(Exception exception) => m_channel.FailWrite(this, exception);

        protected override void PublishCore()
        {
            Item = default;
            Outcome.Publish<bool>(Source, true);
        }
    }
}
