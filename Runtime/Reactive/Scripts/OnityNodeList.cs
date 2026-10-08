using System;
using System.Runtime.CompilerServices;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Reactive
{
    /// <summary>
    /// Subscriber storage shared by <see cref="Subject{T}" /> and <see cref="ReactiveProperty{T}" />. It lives
    /// inline in its owner (always accessed through the owner's field, never copied).
    /// </summary>
    /// <remarks>
    /// Every registered node knows its index. Outside a notification, removal moves the last node into the
    /// freed slot (O(1)). During a notification, removal only clears the slot; the list compacts in order
    /// after the outermost notification ends. Nodes added during a notification are appended, so the
    /// notification in progress reaches them too. Invariant: <c>m_count &lt;= m_slots.Length</c>, and every
    /// registered node sits at its own index. Nodes are stored in struct slots, so storing one needs no
    /// array covariance check.
    /// <para>
    /// <see cref="Single" /> is the only registered node exactly when the list holds one slot, that slot is
    /// live and no removal is pending; otherwise it is null. Owners use it to deliver to a lone subscriber
    /// without the general loop. Every operation that changes the count, a slot or the pending flag keeps
    /// it exact.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">Value type.</typeparam>
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
    internal struct OnityNodeList<T>
    {
        private const int k_initialCapacity = 4;

        private Slot[] m_slots;
        private OnityObserverNode<T> m_single;
        private int m_count;
        private int m_notificationDepth;
        private bool m_hasPendingRemovals;

        /// <summary>Creates an empty list that allocates its storage on the first add.</summary>
        /// <returns>Empty list.</returns>
        internal static OnityNodeList<T> Create()
        {
            OnityNodeList<T> list = default;
            list.m_slots = Array.Empty<Slot>();
            return list;
        }

        /// <summary>Registers <paramref name="node" /> at the end of the list.</summary>
        /// <param name="node">Node that is not registered anywhere.</param>
        /// <param name="owner">Source that owns this list.</param>
        internal void Add(OnityObserverNode<T> node, IOnityNodeOwner<T> owner)
        {
            Slot[] slots = m_slots;
            int count = m_count;

            if (count == slots.Length)
            {
                slots = Grow(count);
            }

            node.Owner = owner;
            node.Index = count;
            slots[count].Node = node;
            m_count = count + 1;

            // An empty list has no pending removal, so its first node is the single one; a second node ends
            // that. Larger lists already have no single node, so the field is not written again.
            if (count == 0)
            {
                m_single = node;
            }
            else if (count == 1)
            {
                m_single = null;
            }
        }

        /// <summary>Removes a registered node of this list.</summary>
        /// <param name="node">Node whose <see cref="OnityObserverNode{T}.Owner" /> is this list's owner.</param>
        internal void Remove(OnityObserverNode<T> node)
        {
            int index = node.Index;
            node.Owner = null;
            Slot[] slots = m_slots;

            if (m_notificationDepth > 0)
            {
                slots[index].Node = null;
                m_hasPendingRemovals = true;
                m_single = null;
                return;
            }

            int lastIndex = m_count - 1;
            OnityObserverNode<T> last = slots[lastIndex].Node;
            slots[lastIndex].Node = null;
            m_count = lastIndex;

            if (index != lastIndex)
            {
                slots[index].Node = last;
                last.Index = index;
            }

            // Outside a notification every slot is live and nothing is pending. A list that shrank to one node
            // has that node as its single one; an empty list has none; larger lists keep null.
            if (lastIndex <= 1)
            {
                m_single = lastIndex == 1 ? slots[0].Node : null;
            }
        }

        /// <summary>
        /// Detaches every node and releases the storage. The depth of a notification in progress is kept, so
        /// that notification stops at its next step.
        /// </summary>
        internal void Clear()
        {
            Slot[] slots = m_slots;
            int count = m_count;

            for (int i = 0; i < count; i++)
            {
                OnityObserverNode<T> node = slots[i].Node;

                if (node != null)
                {
                    node.Owner = null;
                }
            }

            m_slots = Array.Empty<Slot>();
            m_single = null;
            m_count = 0;
            m_hasPendingRemovals = false;
        }

        /// <summary>
        /// Marks the start of a notification and returns the depth to restore when it ends.
        /// </summary>
        /// <returns>Notification depth before this notification (0 for an outermost one).</returns>
        /// <remarks>
        /// Save and restore instead of increment and decrement: the end of a notification stores the saved
        /// value without reading the field again, so back-to-back publications do not chain two
        /// read-modify-writes of the same field through memory. Nested notifications restore their own
        /// saved depth, so the field holds the same values as with increment and decrement.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int BeginNotification()
        {
            int depth = m_notificationDepth;
            m_notificationDepth = depth + 1;
            return depth;
        }

        /// <summary>
        /// Marks the end of a notification. Returns true when the outermost notification ended and nodes
        /// were removed during it; the owner then calls <see cref="Compact" />.
        /// </summary>
        /// <param name="depth">Value returned by the matching <see cref="BeginNotification" />.</param>
        /// <returns>True when the list must be compacted now.</returns>
        /// <remarks>
        /// The owner makes the <see cref="Compact" /> call itself, inside its own branch: under IL2CPP a call
        /// made from here would compute its method metadata on every notification, even when no compaction
        /// follows.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool EndNotification(int depth)
        {
            m_notificationDepth = depth;
            return depth == 0 && m_hasPendingRemovals;
        }

        /// <summary>
        /// Ends a notification after which <see cref="Single" /> is still non-null. During a notification it
        /// can only turn null (swap-back removal and compaction need depth 0), so it is still the same node:
        /// no node was added and no removal is pending. Only the saved depth is stored back.
        /// </summary>
        /// <param name="depth">Value returned by the matching <see cref="BeginNotification" />.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EndUnchangedNotification(int depth)
        {
            m_notificationDepth = depth;
        }

        /// <summary>
        /// The only registered node when the list holds exactly one live slot and no removal is pending;
        /// otherwise null. Also null after <see cref="Clear" />, so a non-null value implies a live owner.
        /// </summary>
        internal OnityObserverNode<T> Single
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_single;
        }

        /// <summary>
        /// Number of slots in use, including slots cleared during a notification. Owners re-read it at
        /// every step of a notification pass, so changes made by callbacks take effect immediately.
        /// </summary>
        internal int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_count;
        }

        /// <summary>Returns the node in a slot, or null for a slot cleared during a notification.</summary>
        /// <param name="index">Slot index below <see cref="Count" />.</param>
        /// <returns>Node or null.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityObserverNode<T> NodeAt(int index)
        {
            return m_slots[index].Node;
        }

        private Slot[] Grow(int count)
        {
            int capacity = count == 0 ? k_initialCapacity : count * 2;
            Slot[] grown = new Slot[capacity];
            Array.Copy(m_slots, grown, count);
            m_slots = grown;
            return grown;
        }

        /// <summary>
        /// Removes the slots cleared during the notification that just ended, keeping the order of the
        /// remaining nodes and updating their indexes. Called by the owner when
        /// <see cref="EndNotification" /> returns true.
        /// </summary>
        internal void Compact()
        {
            Slot[] slots = m_slots;
            int count = m_count;
            int writeIndex = 0;

            for (int readIndex = 0; readIndex < count; readIndex++)
            {
                OnityObserverNode<T> node = slots[readIndex].Node;

                if (node == null)
                {
                    continue;
                }

                if (writeIndex != readIndex)
                {
                    slots[writeIndex].Node = node;
                    node.Index = writeIndex;
                }

                writeIndex++;
            }

            for (int clearIndex = writeIndex; clearIndex < count; clearIndex++)
            {
                slots[clearIndex].Node = null;
            }

            m_count = writeIndex;
            m_hasPendingRemovals = false;
            m_single = writeIndex == 1 ? slots[0].Node : null;
        }

        /// <summary>One list entry; a struct, so storing a node needs no array covariance check.</summary>
        private struct Slot
        {
            public OnityObserverNode<T> Node;
        }
    }
}
