#if ONITY_ENTITIES
using Unity.Burst;
using Unity.Entities;

namespace Onity.DOTS
{
    /// <summary>
    /// Unmanaged integer event used for Burst-friendly queue processing.
    /// </summary>
    public struct OnityDotsIntEvent : IBufferElementData
    {
        /// <summary>
        /// Event payload value.
        /// </summary>
        public int Value;

        /// <summary>
        /// Initializes an integer event.
        /// </summary>
        /// <param name="value">Payload value.</param>
        public OnityDotsIntEvent(int value)
        {
            Value = value;
        }
    }

    /// <summary>
    /// Tag component that identifies the queue singleton entity.
    /// </summary>
    public struct OnityDotsIntEventQueueTag : IComponentData
    {
    }

    /// <summary>
    /// Singleton accumulator that is updated from queued integer events.
    /// </summary>
    public struct OnityDotsIntEventAccumulator : IComponentData
    {
        /// <summary>
        /// Current accumulated value.
        /// </summary>
        public int Value;
    }

    /// <summary>
    /// Creates queue singleton data for the default world.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct OnityDotsIntEventBootstrapSystem : ISystem
    {
        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingletonEntity<OnityDotsIntEventQueueTag>(out _))
            {
                return;
            }

            Entity queueEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<OnityDotsIntEventQueueTag>(queueEntity);
            state.EntityManager.AddComponentData(queueEntity, new OnityDotsIntEventAccumulator());
            state.EntityManager.AddBuffer<OnityDotsIntEvent>(queueEntity);
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }

    /// <summary>
    /// Applies queued integer events to the accumulator in a Burst-compatible system.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct OnityDotsIntEventAccumulateSystem : ISystem
    {
        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OnityDotsIntEventQueueTag>();
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity queueEntity = SystemAPI.GetSingletonEntity<OnityDotsIntEventQueueTag>();
            DynamicBuffer<OnityDotsIntEvent> eventQueue = state.EntityManager.GetBuffer<OnityDotsIntEvent>(queueEntity);

            if (eventQueue.Length == 0)
            {
                return;
            }

            RefRW<OnityDotsIntEventAccumulator> accumulator = SystemAPI.GetSingletonRW<OnityDotsIntEventAccumulator>();
            int accumulatedValue = accumulator.ValueRO.Value;

            for (int i = 0; i < eventQueue.Length; i++)
            {
                accumulatedValue += eventQueue[i].Value;
            }

            accumulator.ValueRW.Value = accumulatedValue;
            eventQueue.Clear();
        }
    }
}
#endif
