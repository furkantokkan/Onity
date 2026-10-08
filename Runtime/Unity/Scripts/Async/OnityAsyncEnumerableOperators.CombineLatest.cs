using System.Threading;

namespace Onity.Unity.Async
{
    // Policy shared by every CombineLatest arity. Each consumer move moves the sources that are idle.
    // Until every source has produced an item, a source that produced one is moved again at once, so
    // only its latest item counts. Afterwards each arriving item yields exactly one result, computed
    // at arrival from the latest item of every source and queued in arrival order; a source is moved
    // again only by a later consumer move. The stream ends when every source has ended, or as soon as
    // a source ends before producing any item (no result could follow).
    internal abstract class OnityCombineLatestEnumerator<TResult> : OnityMultiSourceAsyncEnumerator<TResult>
    {
        private int m_valueCount;
        private int m_endedCount;

        protected OnityCombineLatestEnumerator(OnityStreamSlot[] slots, CancellationToken token)
            : base(slots, 0, token)
        {
        }

        protected abstract TResult Combine();

        protected override void OnRequest()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                RequestMove(GetSlot(i));
            }
        }

        protected override void OnItem(OnityStreamSlot slot)
        {
            if (m_valueCount < SlotCount)
            {
                m_valueCount = 0;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (GetSlot(i).HasValue)
                    {
                        m_valueCount++;
                    }
                }
                if (m_valueCount < SlotCount)
                {
                    RequestMove(slot);
                    return;
                }
            }
            Emit(Combine());
        }

        protected override void OnEnd(OnityStreamSlot slot)
        {
            if (!slot.HasValue || ++m_endedCount == SlotCount)
            {
                Complete();
            }
        }
    }
}
