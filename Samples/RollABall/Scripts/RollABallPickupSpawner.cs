using System;
using System.Collections.Generic;
using Onity.DI;
using Onity.Factory;
using Onity.Messaging;
using Onity.Pooling;
using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Maintains an active pickup set using pooled factory spawn operations.
    /// </summary>
    public sealed class RollABallPickupSpawner : MonoBehaviour
    {
        [Header("Spawn Root")]
        [Tooltip("Optional transform used as parent for spawned pickups.")]
        [SerializeField] private Transform m_spawnRoot;

        [Inject]
        private RollABallGameSettings m_settings = null;

        [Inject]
        private IFactory<RollABallPickup> m_pickupFactory = null;

        [Inject]
        private IPool<RollABallPickup> m_pickupPool = null;

        [Inject]
        private IPublisher<RollABallPickupCollectedMessage> m_pickupPublisher = null;

        [Inject]
        private ISubscriber<RollABallPickupCollectedMessage> m_pickupSubscriber = null;

        private readonly List<RollABallPickup> m_activePickups = new List<RollABallPickup>(64);

        private IDisposable m_pickupCollectedSubscription;
        private int m_nextPickupId;

        /// <summary>
        /// Number of currently active pickups.
        /// </summary>
        public int ActivePickupCount => m_activePickups.Count;

        private void OnEnable()
        {
            m_pickupCollectedSubscription = m_pickupSubscriber.Subscribe(OnPickupCollected);
            SpawnInitialPickups();
        }

        private void OnDisable()
        {
            m_pickupCollectedSubscription?.Dispose();
            m_pickupCollectedSubscription = null;
            ReleaseAllPickups();
        }

        /// <summary>
        /// Clears and respawns all pickups using current settings.
        /// </summary>
        public void RespawnAll()
        {
            ReleaseAllPickups();
            SpawnInitialPickups();
        }

        private void OnPickupCollected(RollABallPickupCollectedMessage message)
        {
            RemoveActivePickupById(message.PickupId);
            SpawnPickup();
        }

        private void SpawnInitialPickups()
        {
            if (m_settings == null)
            {
                return;
            }

            int initialPickupCount = Mathf.Max(1, m_settings.InitialPickupCount);

            for (int i = 0; i < initialPickupCount; i++)
            {
                SpawnPickup();
            }
        }

        private void SpawnPickup()
        {
            if (m_settings == null)
            {
                return;
            }

            RollABallPickup pickup = m_pickupFactory.Create();

            if (pickup == null)
            {
                return;
            }

            Transform spawnParent = m_spawnRoot != null ? m_spawnRoot : transform;
            pickup.transform.SetParent(spawnParent, false);
            pickup.transform.localPosition = GenerateSpawnPosition();
            pickup.transform.localRotation = Quaternion.identity;

            int pickupId = m_nextPickupId;
            m_nextPickupId++;

            int points = Mathf.Max(1, m_settings.PointsPerPickup);
            pickup.Configure(pickupId, points, m_settings.PickupRotationSpeed, m_pickupPublisher, m_pickupPool);
            m_activePickups.Add(pickup);
        }

        private Vector3 GenerateSpawnPosition()
        {
            Vector2 arenaHalfExtents = m_settings.ArenaHalfExtents;
            float padding = Mathf.Max(0f, m_settings.SpawnPadding);
            float xMin = -arenaHalfExtents.x + padding;
            float xMax = arenaHalfExtents.x - padding;
            float zMin = -arenaHalfExtents.y + padding;
            float zMax = arenaHalfExtents.y - padding;

            if (xMin > xMax)
            {
                float center = (xMin + xMax) * 0.5f;
                xMin = center;
                xMax = center;
            }

            if (zMin > zMax)
            {
                float center = (zMin + zMax) * 0.5f;
                zMin = center;
                zMax = center;
            }

            float x = UnityEngine.Random.Range(xMin, xMax);
            float z = UnityEngine.Random.Range(zMin, zMax);
            return new Vector3(x, m_settings.PickupHeight, z);
        }

        private void RemoveActivePickupById(int pickupId)
        {
            for (int i = 0; i < m_activePickups.Count; i++)
            {
                RollABallPickup pickup = m_activePickups[i];

                if (pickup == null || pickup.PickupId != pickupId)
                {
                    continue;
                }

                int lastIndex = m_activePickups.Count - 1;
                m_activePickups[i] = m_activePickups[lastIndex];
                m_activePickups.RemoveAt(lastIndex);
                return;
            }
        }

        private void ReleaseAllPickups()
        {
            for (int i = m_activePickups.Count - 1; i >= 0; i--)
            {
                RollABallPickup pickup = m_activePickups[i];

                if (pickup == null)
                {
                    continue;
                }

                m_pickupPool.Release(pickup);
            }

            m_activePickups.Clear();
        }
    }
}
