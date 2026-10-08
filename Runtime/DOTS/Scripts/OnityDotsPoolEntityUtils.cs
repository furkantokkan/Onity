#if ONITY_ENTITIES
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;

namespace Onity.DOTS
{
    /// <summary>
    /// Marks entities that should be handled through Onity DOTS pooling flow.
    /// </summary>
    public struct OnityPoolableTag : IComponentData
    {
    }

    /// <summary>
    /// Enableable pooled event marker used to request "return to pool" behavior.
    /// </summary>
    public struct OnityPooledEventTag : IComponentData, IEnableableComponent
    {
    }

    /// <summary>
    /// Utility methods for adding pool metadata and signaling pooled despawn.
    /// </summary>
    public static class OnityDotsPoolEntityUtils
    {
        /// <summary>
        /// Adds pool tags through command buffer and initializes pooled event tag as disabled.
        /// </summary>
        /// <param name="commandBuffer">Command buffer.</param>
        /// <param name="entity">Target entity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddPoolComponents(ref EntityCommandBuffer commandBuffer, Entity entity)
        {
            commandBuffer.AddComponent<OnityPoolableTag>(entity);
            commandBuffer.AddComponent<OnityPooledEventTag>(entity);
            commandBuffer.SetComponentEnabled<OnityPooledEventTag>(entity, false);
        }

        /// <summary>
        /// Adds pool tags directly through entity manager and initializes pooled event tag as disabled.
        /// </summary>
        /// <param name="entityManager">Entity manager.</param>
        /// <param name="entity">Target entity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddPoolComponents(ref EntityManager entityManager, Entity entity)
        {
            entityManager.AddComponent<OnityPoolableTag>(entity);
            entityManager.AddComponent<OnityPooledEventTag>(entity);
            entityManager.SetComponentEnabled<OnityPooledEventTag>(entity, false);
        }

        /// <summary>
        /// Signals pooled despawn via parallel command buffer.
        /// </summary>
        /// <param name="commandBuffer">Parallel command buffer writer.</param>
        /// <param name="entityInQueryIndex">Entity index in query.</param>
        /// <param name="entity">Target entity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(
            ref EntityCommandBuffer.ParallelWriter commandBuffer,
            int entityInQueryIndex,
            Entity entity)
        {
            commandBuffer.SetComponentEnabled<OnityPooledEventTag>(entityInQueryIndex, entity, true);
        }

        /// <summary>
        /// Signals pooled despawn via command buffer.
        /// </summary>
        /// <param name="commandBuffer">Command buffer.</param>
        /// <param name="entity">Target entity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(ref EntityCommandBuffer commandBuffer, Entity entity)
        {
            commandBuffer.SetComponentEnabled<OnityPooledEventTag>(entity, true);
        }

        /// <summary>
        /// Signals pooled despawn through enabled reference.
        /// </summary>
        /// <param name="pooledEventTag">Enabled reference wrapper.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(ref EnabledRefRW<OnityPooledEventTag> pooledEventTag)
        {
            pooledEventTag.ValueRW = true;
        }

        /// <summary>
        /// Signals pooled despawn for a batch of entities via command buffer.
        /// </summary>
        /// <param name="commandBuffer">Command buffer.</param>
        /// <param name="entities">Target entities.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(ref EntityCommandBuffer commandBuffer, NativeArray<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                commandBuffer.SetComponentEnabled<OnityPooledEventTag>(entities[i], true);
            }
        }

        /// <summary>
        /// Signals pooled despawn via entity manager.
        /// </summary>
        /// <param name="entityManager">Entity manager.</param>
        /// <param name="entity">Target entity.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(ref EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentEnabled<OnityPooledEventTag>(entity, true);
        }

        /// <summary>
        /// Signals pooled despawn for a batch via entity manager.
        /// </summary>
        /// <param name="entityManager">Entity manager.</param>
        /// <param name="entities">Target entities.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroyEntity(ref EntityManager entityManager, NativeArray<Entity> entities)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                entityManager.SetComponentEnabled<OnityPooledEventTag>(entities[i], true);
            }
        }
    }
}
#endif
