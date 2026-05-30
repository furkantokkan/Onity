using System;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Creation strategy stored per baked binding. Mirrors the public
    /// <see cref="Onity.Core.Lifetime" /> but adds <see cref="Instance" /> so the
    /// baked fast path can distinguish a pre-supplied instance (no construction,
    /// no caching write) from a lazily constructed singleton.
    /// </summary>
    internal enum BakedLifetime
    {
        /// <summary>Single lazily constructed instance reused for every resolve.</summary>
        Singleton = 0,

        /// <summary>New instance constructed on every resolve.</summary>
        Transient = 1,

        /// <summary>Pre-supplied instance returned as-is on every resolve.</summary>
        Instance = 2
    }

    /// <summary>
    /// Flat description of one explicit local binding in a baked graph. Indexed by
    /// provider slot. Stored as a struct in a contiguous array so the resolve hot
    /// path stays cache friendly and allocation free.
    /// </summary>
    internal readonly struct BakedBinding
    {
        /// <summary>Dense <see cref="TypeIdRegistry" /> id of the contract type.</summary>
        public readonly int ContractTypeId;

        /// <summary>Index of this binding's activator and singleton slot.</summary>
        public readonly int ProviderSlot;

        /// <summary>Creation strategy used when producing the instance.</summary>
        public readonly BakedLifetime Lifetime;

        /// <summary>Start index of this binding's flattened constructor dependencies.</summary>
        public readonly int FirstDependency;

        /// <summary>Number of flattened constructor dependencies.</summary>
        public readonly int DependencyCount;

        /// <summary>
        /// Initializes a baked binding row.
        /// </summary>
        /// <param name="contractTypeId">Dense contract type id.</param>
        /// <param name="providerSlot">Provider slot index.</param>
        /// <param name="lifetime">Creation strategy.</param>
        /// <param name="firstDependency">Start index in the dependency list.</param>
        /// <param name="dependencyCount">Dependency count.</param>
        public BakedBinding(
            int contractTypeId,
            int providerSlot,
            BakedLifetime lifetime,
            int firstDependency,
            int dependencyCount)
        {
            ContractTypeId = contractTypeId;
            ProviderSlot = providerSlot;
            Lifetime = lifetime;
            FirstDependency = firstDependency;
            DependencyCount = dependencyCount;
        }
    }

    /// <summary>
    /// Produces the instance for one provider slot. The closure captures the same
    /// provider object the dictionary path would use, so the baked path returns an
    /// identical instance (singleton identity, transient distinctness, member
    /// injection, and re-entrant cycle detection all stay byte-for-byte equal to
    /// the reflection path). The seam exists only to drop the per-resolve
    /// dictionary lookup, not to re-implement construction.
    /// </summary>
    /// <returns>Resolved instance for the slot.</returns>
    internal delegate object BakedProducer();

    /// <summary>
    /// Compiled, array-backed view of a container's explicit local bindings built
    /// once at <c>Build()</c>. Replaces the per-resolve
    /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}" /> lookup on
    /// the hot path with a dense-id keyed slot array. Only explicit local bindings
    /// are baked; parent-chain, implicit-concrete, and unbound contracts fall back
    /// to the proven reflection path so behavior is identical.
    /// </summary>
    internal sealed class BakedGraph
    {
        // Sentinel stored in m_slotByTypeId for a type id that has no local
        // explicit binding. Resolve treats this as "miss, fall back to slow path".
        private const int k_noSlot = -1;

        // Sparse lookup keyed by dense type id. Length covers every id issued so
        // far at bake time; entries default to k_noSlot, baked contracts point at
        // their provider slot. A sparse int array is acceptable for the documented
        // production graph sizes (a few hundred types) and removes the dictionary
        // hash + equality cost from every resolve.
        private readonly int[] m_slotByTypeId;

        // Indexed by provider slot. Parallel arrays keep the hot path branch-light:
        // read producer, optionally read/write the singleton cache, return.
        private readonly BakedBinding[] m_bindings;
        private readonly BakedProducer[] m_producers;
        private readonly object[] m_singletonCache;

        // Flattened constructor dependency type ids, shared across bindings. Kept
        // to satisfy the documented layout and to let future fully-baked
        // construction walk dependencies without per-binding arrays. The current
        // producer seam does not read it on the hot path.
        private readonly int[] m_dependencyList;

        // Compiled activators per provider slot, aligned with m_producers. Captured
        // for the documented layout and for a future fully-baked CreateInstance.
        private readonly ActivatorDelegate[] m_activators;

        private BakedGraph(
            int[] slotByTypeId,
            BakedBinding[] bindings,
            BakedProducer[] producers,
            object[] singletonCache,
            int[] dependencyList,
            ActivatorDelegate[] activators)
        {
            m_slotByTypeId = slotByTypeId;
            m_bindings = bindings;
            m_producers = producers;
            m_singletonCache = singletonCache;
            m_dependencyList = dependencyList;
            m_activators = activators;
        }

        /// <summary>
        /// Number of baked provider slots.
        /// </summary>
        public int SlotCount => m_producers.Length;

        /// <summary>
        /// Tries to resolve a contract by its dense type id using the baked slot
        /// array. Returns false (and leaves the slow path to run) for any id that
        /// has no local explicit binding.
        /// </summary>
        /// <param name="contractTypeId">Dense contract type id.</param>
        /// <param name="instance">Resolved instance when a slot exists.</param>
        /// <returns>True when the contract has a baked local binding.</returns>
        public bool TryResolve(int contractTypeId, out object instance)
        {
            int[] slotByTypeId = m_slotByTypeId;

            if ((uint)contractTypeId >= (uint)slotByTypeId.Length)
            {
                instance = null;
                return false;
            }

            int slot = slotByTypeId[contractTypeId];

            if (slot == k_noSlot)
            {
                instance = null;
                return false;
            }

            instance = ResolveSlot(slot);
            return true;
        }

        // Produces the instance for a slot. Instance and transient bindings defer to
        // the captured producer every time. Singleton bindings cache the producer's
        // result in a flat slot, so steady-state singleton resolves return the cached
        // reference with no dictionary lookup and no producer call. The single-thread
        // resolve guarantee documented for OnityContainer lets this read/write the
        // slot without a memory barrier.
        private object ResolveSlot(int slot)
        {
            BakedLifetime lifetime = m_bindings[slot].Lifetime;

            if (lifetime != BakedLifetime.Singleton)
            {
                return m_producers[slot]();
            }

            object cached = m_singletonCache[slot];

            if (cached != null)
            {
                return cached;
            }

            // The producer wraps the same SingletonProvider, so its first call both
            // constructs and caches inside the provider; storing the result here just
            // collapses future lookups to a field read. Identity matches the provider.
            object created = m_producers[slot]();
            m_singletonCache[slot] = created;
            return created;
        }

        /// <summary>
        /// Mutable builder that collects baked rows during <c>Build()</c> and emits
        /// an immutable <see cref="BakedGraph" />. Construction cost is paid once
        /// per build and never touches the resolve hot path.
        /// </summary>
        public sealed class Builder
        {
            private readonly System.Collections.Generic.List<BakedBinding> m_bindings;
            private readonly System.Collections.Generic.List<BakedProducer> m_producers;
            private readonly System.Collections.Generic.List<ActivatorDelegate> m_activators;
            private readonly System.Collections.Generic.List<int> m_dependencyList;
            private readonly System.Collections.Generic.Dictionary<int, int> m_slotByTypeId;

            /// <summary>
            /// Initializes an empty builder.
            /// </summary>
            /// <param name="expectedBindingCount">Hint for initial capacity.</param>
            public Builder(int expectedBindingCount)
            {
                int capacity = expectedBindingCount > 0 ? expectedBindingCount : 16;
                m_bindings = new System.Collections.Generic.List<BakedBinding>(capacity);
                m_producers = new System.Collections.Generic.List<BakedProducer>(capacity);
                m_activators = new System.Collections.Generic.List<ActivatorDelegate>(capacity);
                m_dependencyList = new System.Collections.Generic.List<int>(capacity * 2);
                m_slotByTypeId = new System.Collections.Generic.Dictionary<int, int>(capacity);
            }

            /// <summary>
            /// Adds (or replaces) the baked binding for a contract. A later binding
            /// for the same contract id wins, matching the dictionary path where the
            /// last registration for a contract replaces earlier ones.
            /// </summary>
            /// <param name="contractTypeId">Dense contract type id.</param>
            /// <param name="lifetime">Creation strategy.</param>
            /// <param name="producer">Instance producer wrapping the provider.</param>
            /// <param name="activator">Compiled activator, or null for instance bindings.</param>
            /// <param name="dependencyTypeIds">Flattened constructor dependency ids.</param>
            public void Add(
                int contractTypeId,
                BakedLifetime lifetime,
                BakedProducer producer,
                ActivatorDelegate activator,
                int[] dependencyTypeIds)
            {
                if (producer == null)
                {
                    throw new ArgumentNullException(nameof(producer));
                }

                int firstDependency = m_dependencyList.Count;
                int dependencyCount = 0;

                if (dependencyTypeIds != null)
                {
                    for (int i = 0; i < dependencyTypeIds.Length; i++)
                    {
                        m_dependencyList.Add(dependencyTypeIds[i]);
                    }

                    dependencyCount = dependencyTypeIds.Length;
                }

                if (m_slotByTypeId.TryGetValue(contractTypeId, out int existingSlot))
                {
                    m_bindings[existingSlot] = new BakedBinding(
                        contractTypeId,
                        existingSlot,
                        lifetime,
                        firstDependency,
                        dependencyCount);
                    m_producers[existingSlot] = producer;
                    m_activators[existingSlot] = activator;
                    return;
                }

                int slot = m_producers.Count;
                m_slotByTypeId.Add(contractTypeId, slot);
                m_bindings.Add(
                    new BakedBinding(contractTypeId, slot, lifetime, firstDependency, dependencyCount));
                m_producers.Add(producer);
                m_activators.Add(activator);
            }

            /// <summary>
            /// Emits the immutable baked graph. The slot lookup array length covers
            /// the largest baked contract id plus one, so every baked id indexes in
            /// bounds and unbaked ids past the end miss cleanly.
            /// </summary>
            /// <returns>Immutable baked graph.</returns>
            public BakedGraph Build()
            {
                int maxId = -1;

                foreach (System.Collections.Generic.KeyValuePair<int, int> pair in m_slotByTypeId)
                {
                    if (pair.Key > maxId)
                    {
                        maxId = pair.Key;
                    }
                }

                int[] slotByTypeId = new int[maxId + 1];

                for (int i = 0; i < slotByTypeId.Length; i++)
                {
                    slotByTypeId[i] = k_noSlot;
                }

                foreach (System.Collections.Generic.KeyValuePair<int, int> pair in m_slotByTypeId)
                {
                    slotByTypeId[pair.Key] = pair.Value;
                }

                return new BakedGraph(
                    slotByTypeId,
                    m_bindings.ToArray(),
                    m_producers.ToArray(),
                    new object[m_producers.Count],
                    m_dependencyList.ToArray(),
                    m_activators.ToArray());
            }
        }
    }
}
