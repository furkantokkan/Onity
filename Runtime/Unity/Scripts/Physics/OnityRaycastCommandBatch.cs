using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Unity.Physics
{
    /// <summary>
    /// Persistent, allocation-free batch raycast scheduler based on <see cref="RaycastCommand" />.
    /// </summary>
    public sealed class OnityRaycastCommandBatch : IDisposable
    {
        private NativeArray<RaycastCommand> m_commands;
        private NativeArray<RaycastHit> m_hits;
        private readonly Allocator m_allocator;
        private readonly int m_maxHitsPerRaycast;
        private int m_capacity;
        private int m_lastScheduledCount;
        private JobHandle m_pendingHandle;
        private bool m_hasPendingJob;
        private bool m_isDisposed;

        /// <summary>
        /// Creates a new persistent batch container.
        /// </summary>
        /// <param name="initialCapacity">Initial number of rays that can be scheduled.</param>
        /// <param name="maxHitsPerRaycast">Maximum hits per ray.</param>
        /// <param name="allocator">Native allocator type.</param>
        public OnityRaycastCommandBatch(
            int initialCapacity = 256,
            int maxHitsPerRaycast = 1,
            Allocator allocator = Allocator.Persistent)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            if (maxHitsPerRaycast <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHitsPerRaycast));
            }

            m_allocator = allocator;
            m_maxHitsPerRaycast = maxHitsPerRaycast;
            m_capacity = 0;
            m_lastScheduledCount = 0;
            m_pendingHandle = default;
            m_hasPendingJob = false;
            m_isDisposed = false;

            EnsureCapacity(initialCapacity);
        }

        /// <summary>
        /// Current ray capacity.
        /// </summary>
        public int Capacity => m_capacity;

        /// <summary>
        /// Maximum number of hits produced for each ray.
        /// </summary>
        public int MaxHitsPerRaycast => m_maxHitsPerRaycast;

        /// <summary>
        /// Schedules a raycast command batch after completing any previous batch owned by this instance.
        /// </summary>
        /// <param name="origins">Ray origins.</param>
        /// <param name="directions">Ray directions.</param>
        /// <param name="maxDistance">Ray max distance.</param>
        /// <param name="layerMask">Layer filter mask.</param>
        /// <param name="rayCountOverride">Optional explicit ray count.</param>
        /// <param name="minCommandsPerJob">Minimum commands per worker job.</param>
        /// <param name="dependency">Dependency handle.</param>
        /// <returns>Scheduled job handle.</returns>
        public JobHandle Schedule(
            Vector3[] origins,
            Vector3[] directions,
            float maxDistance,
            int layerMask = UnityEngine.Physics.DefaultRaycastLayers,
            int rayCountOverride = -1,
            int minCommandsPerJob = 32,
            JobHandle dependency = default)
        {
            ThrowIfDisposed();

            if (origins == null)
            {
                throw new ArgumentNullException(nameof(origins));
            }

            if (directions == null)
            {
                throw new ArgumentNullException(nameof(directions));
            }

            if (maxDistance < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistance));
            }

            if (minCommandsPerJob <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minCommandsPerJob));
            }

            int rayCount = rayCountOverride >= 0
                ? rayCountOverride
                : Math.Min(origins.Length, directions.Length);

            if (rayCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rayCountOverride));
            }

            if (rayCount > origins.Length || rayCount > directions.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(rayCountOverride));
            }

            CompletePendingJob();
            EnsureCapacity(rayCount);
            m_lastScheduledCount = 0;

            if (rayCount == 0)
            {
                return dependency;
            }

            QueryParameters parameters = new QueryParameters
            {
                layerMask = layerMask,
                hitBackfaces = false,
                hitTriggers = QueryTriggerInteraction.UseGlobal,
                hitMultipleFaces = m_maxHitsPerRaycast > 1
            };

            for (int i = 0; i < rayCount; i++)
            {
                m_commands[i] = new RaycastCommand(
                    origins[i],
                    directions[i],
                    parameters,
                    maxDistance);
            }

            NativeArray<RaycastCommand> activeCommands = m_commands.GetSubArray(0, rayCount);
            NativeArray<RaycastHit> activeHits = m_hits.GetSubArray(0, rayCount * m_maxHitsPerRaycast);

            JobHandle handle = RaycastCommand.ScheduleBatch(
                activeCommands,
                activeHits,
                minCommandsPerJob,
                m_maxHitsPerRaycast,
                dependency);

            m_pendingHandle = handle;
            m_hasPendingJob = true;
            m_lastScheduledCount = rayCount;
            return handle;
        }

        /// <summary>
        /// Completes the pending batch and copies hits for one ray into caller-owned buffer.
        /// </summary>
        /// <param name="rayIndex">Ray index used during scheduling.</param>
        /// <param name="destination">Destination buffer.</param>
        /// <param name="destinationOffset">Destination start index.</param>
        /// <returns>Number of copied hit entries.</returns>
        public int CopyHitsForRay(int rayIndex, RaycastHit[] destination, int destinationOffset = 0)
        {
            ThrowIfDisposed();

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (rayIndex < 0 || rayIndex >= m_lastScheduledCount)
            {
                throw new ArgumentOutOfRangeException(nameof(rayIndex));
            }

            if (destinationOffset < 0 || destinationOffset > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(destinationOffset));
            }

            CompletePendingJob();

            int sourceOffset = rayIndex * m_maxHitsPerRaycast;
            int available = destination.Length - destinationOffset;
            int copyLimit = available < m_maxHitsPerRaycast ? available : m_maxHitsPerRaycast;
            int written = 0;

            for (int i = 0; i < copyLimit; i++)
            {
                RaycastHit hit = m_hits[sourceOffset + i];

                if (hit.collider == null)
                {
                    break;
                }

                destination[destinationOffset + written] = hit;
                written++;
            }

            return written;
        }

        /// <summary>
        /// Completes the pending batch and returns the first hit entry for one ray.
        /// </summary>
        /// <param name="rayIndex">Ray index used during scheduling.</param>
        /// <returns>First hit entry for the given ray.</returns>
        public RaycastHit GetFirstHit(int rayIndex)
        {
            ThrowIfDisposed();

            if (rayIndex < 0 || rayIndex >= m_lastScheduledCount)
            {
                throw new ArgumentOutOfRangeException(nameof(rayIndex));
            }

            CompletePendingJob();
            return m_hits[rayIndex * m_maxHitsPerRaycast];
        }

        /// <summary>
        /// Completes the currently scheduled batch, if any.
        /// </summary>
        public void Complete()
        {
            ThrowIfDisposed();
            CompletePendingJob();
        }

        /// <summary>
        /// Completes the pending batch and clears command and hit buffers.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            CompletePendingJob();
            m_lastScheduledCount = 0;

            for (int i = 0; i < m_commands.Length; i++)
            {
                m_commands[i] = default;
            }

            for (int i = 0; i < m_hits.Length; i++)
            {
                m_hits[i] = default;
            }
        }

        /// <summary>
        /// Ensures internal buffers can hold at least <paramref name="requiredCapacity" /> rays.
        /// A resize completes the pending batch before replacing native buffers.
        /// </summary>
        /// <param name="requiredCapacity">Required ray capacity.</param>
        public void EnsureCapacity(int requiredCapacity)
        {
            ThrowIfDisposed();

            if (requiredCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requiredCapacity));
            }

            if (requiredCapacity <= m_capacity)
            {
                return;
            }

            CompletePendingJob();
            int targetCapacity = NextPowerOfTwo(requiredCapacity);

            NativeArray<RaycastCommand> nextCommands =
                new NativeArray<RaycastCommand>(targetCapacity, m_allocator, NativeArrayOptions.ClearMemory);
            NativeArray<RaycastHit> nextHits =
                new NativeArray<RaycastHit>(targetCapacity * m_maxHitsPerRaycast, m_allocator, NativeArrayOptions.ClearMemory);

            if (m_commands.IsCreated)
            {
                NativeArray<RaycastCommand>.Copy(m_commands, nextCommands, m_capacity);
                m_commands.Dispose();
            }

            if (m_hits.IsCreated)
            {
                NativeArray<RaycastHit>.Copy(m_hits, nextHits, m_capacity * m_maxHitsPerRaycast);
                m_hits.Dispose();
            }

            m_commands = nextCommands;
            m_hits = nextHits;
            m_capacity = targetCapacity;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_isDisposed)
            {
                return;
            }

            CompletePendingJob();
            m_isDisposed = true;

            if (m_commands.IsCreated)
            {
                m_commands.Dispose();
            }

            if (m_hits.IsCreated)
            {
                m_hits.Dispose();
            }

            m_capacity = 0;
            m_lastScheduledCount = 0;
            m_pendingHandle = default;
            m_hasPendingJob = false;
        }

        private static int NextPowerOfTwo(int value)
        {
            int next = 1;

            while (next < value)
            {
                next <<= 1;
            }

            return next;
        }

        private void CompletePendingJob()
        {
            if (m_hasPendingJob == false)
            {
                return;
            }

            m_pendingHandle.Complete();
            m_pendingHandle = default;
            m_hasPendingJob = false;
        }

        private void ThrowIfDisposed()
        {
            if (m_isDisposed)
            {
                throw new ObjectDisposedException(nameof(OnityRaycastCommandBatch));
            }
        }
    }
}
