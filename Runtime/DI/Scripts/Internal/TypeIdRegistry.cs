using System;
using System.Collections.Generic;

namespace Onity.DI.Internal
{
    /// <summary>
    /// Assigns a process-wide dense integer id to every <see cref="Type" /> that
    /// participates in resolve. Ids start at zero and increase by one, so a baked
    /// graph can key bindings by a small contiguous array index instead of a
    /// dictionary keyed by <see cref="Type" />. The mapping is stable for the
    /// lifetime of the process and shared across every <see cref="OnityContainer" />
    /// instance.
    /// </summary>
    internal static class TypeIdRegistry
    {
        // Guards both the lookup map and the next-id counter. Registration only
        // happens during plan/graph construction (never on the resolve hot path),
        // so the lock cost never appears in a steady-state Resolve.
        private static readonly object s_gate = new object();
        private static readonly Dictionary<Type, int> s_map = new Dictionary<Type, int>(256);
        private static int s_next;

        /// <summary>
        /// Number of distinct types registered so far. Equals the smallest array
        /// length that can be indexed by every issued id.
        /// </summary>
        public static int Count
        {
            get
            {
                lock (s_gate)
                {
                    return s_next;
                }
            }
        }

        /// <summary>
        /// Returns the dense id for a type, assigning a new one on first request.
        /// </summary>
        /// <param name="type">Type to register. Must not be null.</param>
        /// <returns>Stable dense id for the type.</returns>
        public static int Register(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (s_gate)
            {
                if (s_map.TryGetValue(type, out int existing))
                {
                    return existing;
                }

                int id = s_next++;
                s_map.Add(type, id);
                return id;
            }
        }

        /// <summary>
        /// Tries to get the id for a type without assigning a new one.
        /// </summary>
        /// <param name="type">Type to look up.</param>
        /// <param name="id">Dense id when the type was already registered.</param>
        /// <returns>True when the type already has an id.</returns>
        public static bool TryGetId(Type type, out int id)
        {
            if (type == null)
            {
                id = -1;
                return false;
            }

            lock (s_gate)
            {
                return s_map.TryGetValue(type, out id);
            }
        }
    }

    /// <summary>
    /// Caches the <see cref="TypeIdRegistry" /> id for a concrete generic type so
    /// <c>Resolve&lt;T&gt;</c> reads a static field instead of paying a dictionary
    /// lookup per call. The id is resolved once per closed type per process by the
    /// static initializer.
    /// </summary>
    /// <typeparam name="T">Contract type whose id is cached.</typeparam>
    internal static class TypeIdCache<T>
    {
        /// <summary>
        /// Dense id assigned to <typeparamref name="T" /> by <see cref="TypeIdRegistry" />.
        /// </summary>
        public static readonly int Id = TypeIdRegistry.Register(typeof(T));
    }
}
