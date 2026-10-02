using System;
using System.Threading;

namespace Onity.Unity.Async
{
    // A pending WaitToReadAsync: it holds the consumer lease without consuming an item and completes
    // with true when an item is buffered, false or the original error when the channel completes, or
    // canceled by its token. State uses the channel gate like the other channel waiters.
    internal sealed class OnityChannelWaitWaiter<T> : OnityChannelWaiter
    {
        private readonly OnityChannel<T> m_channel;
        internal readonly OnityTaskCompletionSource<bool> Source = new OnityTaskCompletionSource<bool>();

        internal OnityChannelWaitWaiter(OnityChannel<T> channel, CancellationToken token) : base(channel.Gate, token)
        {
            m_channel = channel;
        }

        protected override void CancelCore(CancellationToken token) => m_channel.CancelWait(this, token);
        protected override void FailCore(Exception exception) => m_channel.FailWait(this, exception);
        protected override void PublishCore() => Outcome.Publish(Source, Outcome.Value);
    }
}
