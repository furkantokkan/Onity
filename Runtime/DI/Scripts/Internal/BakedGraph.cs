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

        /// <summary>Index of this binding's provider and singleton slot.</summary>
        public readonly int ProviderSlot;

        /// <summary>Creation strategy used when producing the instance.</summary>
        public readonly BakedLifetime Lifetime;

        /// <summary>
        /// Initializes a baked binding row.
        /// </summary>
        /// <param name="contractTypeId">Dense contract type id.</param>
        /// <param name="providerSlot">Provider slot index.</param>
        /// <param name="lifetime">Creation strategy.</param>
        public BakedBinding(
            int contractTypeId,
            int providerSlot,
            BakedLifetime lifetime)
        {
            ContractTypeId = contractTypeId;
            ProviderSlot = providerSlot;
            Lifetime = lifetime;
        }
    }

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
        // read provider, optionally read/write the singleton cache, return.
        private readonly OnityContainer m_container;
        private readonly BakedBinding[] m_bindings;
        private readonly OnityContainer.IBakedProvider[] m_providers;
        private readonly object[] m_singletonCache;

        private BakedGraph(
            OnityContainer container,
            int[] slotByTypeId,
            BakedBinding[] bindings,
            OnityContainer.IBakedProvider[] providers,
            object[] singletonCache)
        {
            m_container = container;
            m_slotByTypeId = slotByTypeId;
            m_bindings = bindings;
            m_providers = providers;
            m_singletonCache = singletonCache;
        }

        /// <summary>
        /// Number of baked provider slots.
        /// </summary>
        public int SlotCount => m_providers.Length;

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

        // Produces the instance for a slot. Instance and transient bindings defer
        // to the provider every time. Singleton bindings cache the provider's result
        // in a flat slot, so steady-state singleton resolves return the cached
        // reference with no dictionary lookup and no provider call. The single-thread
        // resolve guarantee documented for OnityContainer lets this read/write the
        // slot without a memory barrier.
        private object ResolveSlot(int slot)
        {
            BakedLifetime lifetime = m_bindings[slot].Lifetime;
            OnityContainer.IBakedProvider provider = m_providers[slot];

            if (lifetime != BakedLifetime.Singleton)
            {
                return provider.Get(m_container);
            }

            object cached = m_singletonCache[slot];

            if (cached != null)
            {
                return cached;
            }

            // The provider is the same SingletonProvider used by the dictionary path,
            // so its first call both constructs and caches inside the provider;
            // storing the result here just collapses future lookups to a field read.
            // Identity matches the provider.
            object created = provider.Get(m_container);
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
            private readonly OnityContainer m_container;
            private readonly System.Collections.Generic.List<BakedBinding> m_bindings;
            private readonly System.Collections.Generic.List<OnityContainer.IBakedProvider> m_providers;
            private readonly System.Collections.Generic.Dictionary<int, int> m_slotByTypeId;

            /// <summary>
            /// Initializes an empty builder.
            /// </summary>
            /// <param name="container">Owning container.</param>
            /// <param name="expectedBindingCount">Hint for initial capacity.</param>
            public Builder(OnityContainer container, int expectedBindingCount)
            {
                m_container = container ?? throw new ArgumentNullException(nameof(container));
                int capacity = expectedBindingCount > 0 ? expectedBindingCount : 16;
                m_bindings = new System.Collections.Generic.List<BakedBinding>(capacity);
                m_providers = new System.Collections.Generic.List<OnityContainer.IBakedProvider>(capacity);
                m_slotByTypeId = new System.Collections.Generic.Dictionary<int, int>(capacity);
            }

            /// <summary>
            /// Adds (or replaces) the baked binding for a contract. A later binding
            /// for the same contract id wins, matching the dictionary path where the
            /// last registration for a contract replaces earlier ones.
            /// </summary>
            /// <param name="contractTypeId">Dense contract type id.</param>
            /// <param name="lifetime">Creation strategy.</param>
            /// <param name="provider">Provider used by the reflection path.</param>
            public void Add(
                int contractTypeId,
                BakedLifetime lifetime,
                OnityContainer.IBakedProvider provider)
            {
                if (provider == null)
                {
                    throw new ArgumentNullException(nameof(provider));
                }

                if (m_slotByTypeId.TryGetValue(contractTypeId, out int existingSlot))
                {
                    m_bindings[existingSlot] = new BakedBinding(
                        contractTypeId,
                        existingSlot,
                        lifetime);
                    m_providers[existingSlot] = provider;
                    return;
                }

                int slot = m_providers.Count;
                m_slotByTypeId.Add(contractTypeId, slot);
                m_bindings.Add(new BakedBinding(contractTypeId, slot, lifetime));
                m_providers.Add(provider);
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
                    m_container,
                    slotByTypeId,
                    m_bindings.ToArray(),
                    m_providers.ToArray(),
                    new object[m_providers.Count]);
            }
        }
    }
}
