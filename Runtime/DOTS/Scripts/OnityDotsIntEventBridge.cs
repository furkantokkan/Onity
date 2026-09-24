using System;

#if ONITY_ENTITIES
using Unity.Entities;
#endif

namespace Onity.DOTS
{
    /// <summary>
    /// Managed bridge for publishing integer events into the DOTS queue.
    /// </summary>
    public static class OnityDotsIntEventBridge
    {
#if ONITY_ENTITIES
        private static readonly ComponentType[] s_queryTypes =
        {
            ComponentType.ReadOnly<OnityDotsIntEventQueueTag>(),
            ComponentType.ReadWrite<OnityDotsIntEventAccumulator>(),
            ComponentType.ReadWrite<OnityDotsIntEvent>()
        };

        private static World s_cachedWorld;
        private static EntityQuery s_cachedQueueQuery;
        private static bool s_isQueryCached;
#endif

        /// <summary>
        /// Attempts to enqueue one integer payload into the default world queue.
        /// </summary>
        /// <param name="value">Payload value.</param>
        /// <returns>True when enqueued; otherwise false.</returns>
        public static bool TryPublish(int value)
        {
#if ONITY_ENTITIES
            if (value == 0)
            {
                return false;
            }

            if (TryGetQueueEntity(out Entity queueEntity, out EntityManager entityManager) == false)
            {
                return false;
            }

            DynamicBuffer<OnityDotsIntEvent> queue = entityManager.GetBuffer<OnityDotsIntEvent>(queueEntity);
            queue.Add(new OnityDotsIntEvent(value));
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Gets the current accumulated value from the queue singleton.
        /// </summary>
        /// <param name="value">Read value when successful.</param>
        /// <returns>True when read succeeds; otherwise false.</returns>
        public static bool TryGetAccumulatedValue(out int value)
        {
#if ONITY_ENTITIES
            if (TryGetQueueEntity(out Entity queueEntity, out EntityManager entityManager) == false)
            {
                value = 0;
                return false;
            }

            value = entityManager.GetComponentData<OnityDotsIntEventAccumulator>(queueEntity).Value;
            return true;
#else
            value = 0;
            return false;
#endif
        }

        /// <summary>
        /// Resets the queue accumulator to zero.
        /// </summary>
        /// <returns>True when reset succeeds; otherwise false.</returns>
        public static bool TryResetAccumulator()
        {
#if ONITY_ENTITIES
            if (TryGetQueueEntity(out Entity queueEntity, out EntityManager entityManager) == false)
            {
                return false;
            }

            entityManager.SetComponentData(queueEntity, new OnityDotsIntEventAccumulator());
            return true;
#else
            return false;
#endif
        }

#if ONITY_ENTITIES
        private static bool TryGetQueueEntity(out Entity queueEntity, out EntityManager entityManager)
        {
            World world = World.DefaultGameObjectInjectionWorld;

            if (world == null)
            {
                queueEntity = Entity.Null;
                entityManager = default;
                return false;
            }

            entityManager = world.EntityManager;
            EntityQuery queueQuery = GetOrCreateQueueQuery(world);

            if (queueQuery.IsEmptyIgnoreFilter)
            {
                queueEntity = Entity.Null;
                return false;
            }

            queueEntity = queueQuery.GetSingletonEntity();
            return true;
        }

        private static EntityQuery GetOrCreateQueueQuery(World world)
        {
            if (s_isQueryCached && ReferenceEquals(s_cachedWorld, world))
            {
                return s_cachedQueueQuery;
            }

            s_cachedQueueQuery = world.EntityManager.CreateEntityQuery(s_queryTypes);
            s_cachedWorld = world;
            s_isQueryCached = true;
            return s_cachedQueueQuery;
        }
#endif
    }
}
