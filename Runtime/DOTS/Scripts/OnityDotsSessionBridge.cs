using System;

#if ONITY_ENTITIES
using Unity.Burst;
using Unity.Entities;
#endif

namespace Onity.DOTS
{
#if ONITY_ENTITIES
    /// <summary>
    /// Tag for DOTS session singleton entity.
    /// </summary>
    public struct OnityDotsSessionTag : IComponentData
    {
    }

    /// <summary>
    /// Session metadata payload mirrored between managed scene flow and DOTS world.
    /// </summary>
    public struct OnityDotsSessionState : IComponentData
    {
        /// <summary>
        /// Deterministic session seed.
        /// </summary>
        public int SessionSeed;

        /// <summary>
        /// First gameplay wave index.
        /// </summary>
        public int StartingWave;

        /// <summary>
        /// Additional enemies spawned per wave.
        /// </summary>
        public int EnemyBonusPerWave;

        /// <summary>
        /// Reserved extension slot.
        /// </summary>
        public int Reserved;

        /// <summary>
        /// Version marker.
        /// </summary>
        public int Version;
    }

    /// <summary>
    /// Ensures DOTS session singleton exists in the default world.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct OnityDotsSessionBootstrapSystem : ISystem
    {
        /// <inheritdoc />
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            if (SystemAPI.TryGetSingletonEntity<OnityDotsSessionTag>(out _))
            {
                return;
            }

            Entity sessionEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<OnityDotsSessionTag>(sessionEntity);
            state.EntityManager.AddComponentData(sessionEntity, new OnityDotsSessionState());
        }

        /// <inheritdoc />
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
        }
    }
#endif

    /// <summary>
    /// Managed bridge for reading/writing DOTS session metadata.
    /// </summary>
    public static class OnityDotsSessionBridge
    {
#if ONITY_ENTITIES
        private static readonly ComponentType[] s_queryTypes =
        {
            ComponentType.ReadOnly<OnityDotsSessionTag>(),
            ComponentType.ReadWrite<OnityDotsSessionState>()
        };

        private static World s_cachedWorld;
        private static EntityQuery s_cachedQuery;
        private static bool s_isQueryCached;
#endif

        /// <summary>
        /// Writes integer session metadata values into DOTS singleton.
        /// </summary>
        /// <param name="value0">First value.</param>
        /// <param name="value1">Second value.</param>
        /// <param name="value2">Third value.</param>
        /// <param name="value3">Fourth value.</param>
        /// <param name="version">Version marker.</param>
        /// <returns>True when write succeeds; otherwise false.</returns>
        public static bool TrySet(
            int value0,
            int value1,
            int value2,
            int value3,
            int version = 0)
        {
#if ONITY_ENTITIES
            if (TryGetOrCreateSessionEntity(out Entity sessionEntity, out EntityManager entityManager) == false)
            {
                return false;
            }

            entityManager.SetComponentData(
                sessionEntity,
                new OnityDotsSessionState
                {
                    SessionSeed = value0,
                    StartingWave = value1,
                    EnemyBonusPerWave = value2,
                    Reserved = value3,
                    Version = version
                });
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Reads integer session metadata values from DOTS singleton.
        /// </summary>
        /// <param name="value0">First value.</param>
        /// <param name="value1">Second value.</param>
        /// <param name="value2">Third value.</param>
        /// <param name="value3">Fourth value.</param>
        /// <param name="version">Version marker.</param>
        /// <returns>True when read succeeds; otherwise false.</returns>
        public static bool TryGet(
            out int value0,
            out int value1,
            out int value2,
            out int value3,
            out int version)
        {
#if ONITY_ENTITIES
            if (TryGetOrCreateSessionEntity(out Entity sessionEntity, out EntityManager entityManager) == false)
            {
                value0 = 0;
                value1 = 0;
                value2 = 0;
                value3 = 0;
                version = 0;
                return false;
            }

            OnityDotsSessionState data = entityManager.GetComponentData<OnityDotsSessionState>(sessionEntity);
            value0 = data.SessionSeed;
            value1 = data.StartingWave;
            value2 = data.EnemyBonusPerWave;
            value3 = data.Reserved;
            version = data.Version;
            return true;
#else
            value0 = 0;
            value1 = 0;
            value2 = 0;
            value3 = 0;
            version = 0;
            return false;
#endif
        }

        /// <summary>
        /// Writes semantic session metadata values into DOTS singleton.
        /// </summary>
        /// <param name="sessionSeed">Deterministic session seed.</param>
        /// <param name="startingWave">First gameplay wave index.</param>
        /// <param name="enemyBonusPerWave">Additional enemies spawned per wave.</param>
        /// <param name="reserved">Reserved extension slot.</param>
        /// <param name="version">Version marker.</param>
        /// <returns>True when write succeeds; otherwise false.</returns>
        public static bool TrySetSessionState(
            int sessionSeed,
            int startingWave,
            int enemyBonusPerWave,
            int reserved = 0,
            int version = 0)
        {
            return TrySet(sessionSeed, startingWave, enemyBonusPerWave, reserved, version);
        }

        /// <summary>
        /// Reads semantic session metadata values from DOTS singleton.
        /// </summary>
        /// <param name="sessionSeed">Deterministic session seed.</param>
        /// <param name="startingWave">First gameplay wave index.</param>
        /// <param name="enemyBonusPerWave">Additional enemies spawned per wave.</param>
        /// <param name="reserved">Reserved extension slot.</param>
        /// <param name="version">Version marker.</param>
        /// <returns>True when read succeeds; otherwise false.</returns>
        public static bool TryGetSessionState(
            out int sessionSeed,
            out int startingWave,
            out int enemyBonusPerWave,
            out int reserved,
            out int version)
        {
            return TryGet(
                out sessionSeed,
                out startingWave,
                out enemyBonusPerWave,
                out reserved,
                out version);
        }

#if ONITY_ENTITIES
        private static bool TryGetOrCreateSessionEntity(out Entity sessionEntity, out EntityManager entityManager)
        {
            World world = World.DefaultGameObjectInjectionWorld;

            if (world == null)
            {
                sessionEntity = Entity.Null;
                entityManager = default;
                return false;
            }

            entityManager = world.EntityManager;
            EntityQuery sessionQuery = GetOrCreateQuery(world);

            if (sessionQuery.IsEmptyIgnoreFilter)
            {
                sessionEntity = entityManager.CreateEntity();
                entityManager.AddComponent<OnityDotsSessionTag>(sessionEntity);
                entityManager.AddComponentData(sessionEntity, new OnityDotsSessionState());
                return true;
            }

            sessionEntity = sessionQuery.GetSingletonEntity();
            return true;
        }

        private static EntityQuery GetOrCreateQuery(World world)
        {
            if (s_isQueryCached && ReferenceEquals(s_cachedWorld, world))
            {
                return s_cachedQuery;
            }

            s_cachedQuery = world.EntityManager.CreateEntityQuery(s_queryTypes);
            s_cachedWorld = world;
            s_isQueryCached = true;
            return s_cachedQuery;
        }
#endif
    }
}
