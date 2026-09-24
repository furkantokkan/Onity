using System;
using NUnit.Framework;
using Onity.Unity.Physics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class NonAllocPhysicsTests
    {
        [Test]
        public void NonAllocRaycast_WithColliderInFront_ReturnsHit()
        {
            GameObject hitTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hitTarget.transform.position = new Vector3(0f, 0f, 2f);
            UnityEngine.Physics.SyncTransforms();

            try
            {
                RaycastHit[] hits = new RaycastHit[4];
                int hitCount = OnityNonAllocPhysics.Raycast(
                    Vector3.zero,
                    Vector3.forward,
                    hits,
                    5f);

                Assert.That(hitCount, Is.GreaterThan(0));
                Assert.That(hits[0].collider, Is.Not.Null);
                Assert.That(hits[0].collider.gameObject, Is.EqualTo(hitTarget));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hitTarget);
            }
        }

        [Test]
        public void NonAllocRaycast_NullResultBuffer_Throws()
        {
            Ray ray = new Ray(Vector3.zero, Vector3.forward);

            Assert.That(
                () => OnityNonAllocPhysics.Raycast(ray, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NonAllocOverlapSphere_NullResultBuffer_Throws()
        {
            Assert.That(
                () => OnityNonAllocPhysics.OverlapSphere(Vector3.zero, 1f, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void RaycastCommandBatch_InvalidCtorArgs_Throws()
        {
            Assert.That(
                () => new OnityRaycastCommandBatch(0, 1, Allocator.Persistent),
                Throws.TypeOf<ArgumentOutOfRangeException>());

            Assert.That(
                () => new OnityRaycastCommandBatch(1, 0, Allocator.Persistent),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RaycastCommandBatch_EnsureCapacity_GrowsCapacity()
        {
            using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            int originalCapacity = batch.Capacity;

            batch.EnsureCapacity(64);

            Assert.That(batch.Capacity, Is.GreaterThanOrEqualTo(64));
            Assert.That(batch.Capacity, Is.GreaterThanOrEqualTo(originalCapacity));
        }

        [Test]
        public void RaycastCommandBatch_Schedule_WithColliderInFront_ReturnsHit()
        {
            GameObject hitTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hitTarget.transform.position = new Vector3(0f, 0f, 2f);
            UnityEngine.Physics.SyncTransforms();

            try
            {
                using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);

                Vector3[] origins = { Vector3.zero };
                Vector3[] directions = { Vector3.forward };

                JobHandle handle = batch.Schedule(origins, directions, 5f);
                handle.Complete();

                RaycastHit firstHit = batch.GetFirstHit(0);
                Assert.That(firstHit.collider, Is.Not.Null);
                Assert.That(firstHit.collider.gameObject, Is.EqualTo(hitTarget));

                RaycastHit[] destination = new RaycastHit[2];
                int copied = batch.CopyHitsForRay(0, destination);
                Assert.That(copied, Is.EqualTo(1));
                Assert.That(destination[0].collider.gameObject, Is.EqualTo(hitTarget));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hitTarget);
            }
        }

        [Test]
        public void RaycastCommandBatch_GetFirstHit_CompletesPendingJob()
        {
            GameObject hitTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hitTarget.transform.position = new Vector3(0f, 0f, 2f);
            UnityEngine.Physics.SyncTransforms();

            try
            {
                using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
                using PendingDependency dependency = new PendingDependency();
                Vector3[] origins = { Vector3.zero };
                Vector3[] directions = { Vector3.forward };

                JobHandle handle = batch.Schedule(
                    origins,
                    directions,
                    5f,
                    dependency: dependency.Handle);
                Assert.That(handle.IsCompleted, Is.False);
                dependency.Release();
                RaycastHit firstHit = batch.GetFirstHit(0);

                Assert.That(firstHit.collider, Is.Not.Null);
                Assert.That(firstHit.collider.gameObject, Is.EqualTo(hitTarget));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hitTarget);
            }
        }

        [Test]
        public void RaycastCommandBatch_Complete_CompletesScheduledBatch()
        {
            using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            using PendingDependency dependency = new PendingDependency();
            Vector3[] origins = { Vector3.zero };
            Vector3[] directions = { Vector3.forward };

            JobHandle handle = batch.Schedule(
                origins,
                directions,
                1f,
                dependency: dependency.Handle);

            Assert.That(handle.IsCompleted, Is.False);
            dependency.Release();
            Assert.DoesNotThrow(batch.Complete);
            Assert.DoesNotThrow(handle.Complete);
        }

        [Test]
        public void RaycastCommandBatch_ScheduleAgain_CompletesPreviousBatch()
        {
            GameObject hitTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hitTarget.transform.position = new Vector3(0f, 0f, 2f);
            UnityEngine.Physics.SyncTransforms();

            try
            {
                using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
                using PendingDependency dependency = new PendingDependency();
                Vector3[] origins = { Vector3.zero };

                JobHandle firstHandle = batch.Schedule(
                    origins,
                    new[] { Vector3.back },
                    5f,
                    dependency: dependency.Handle);
                Assert.That(firstHandle.IsCompleted, Is.False);
                dependency.Release();
                batch.Schedule(origins, new[] { Vector3.forward }, 5f);

                RaycastHit firstHit = batch.GetFirstHit(0);
                Assert.That(firstHit.collider, Is.Not.Null);
                Assert.That(firstHit.collider.gameObject, Is.EqualTo(hitTarget));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hitTarget);
            }
        }

        [Test]
        public void RaycastCommandBatch_EnsureCapacityWhilePending_CompletesBeforeResize()
        {
            using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(1, 1, Allocator.Persistent);
            using PendingDependency dependency = new PendingDependency();
            JobHandle handle = batch.Schedule(
                new[] { Vector3.zero },
                new[] { Vector3.forward },
                1f,
                dependency: dependency.Handle);

            Assert.That(handle.IsCompleted, Is.False);
            dependency.Release();
            Assert.DoesNotThrow(() => batch.EnsureCapacity(64));
            Assert.That(batch.Capacity, Is.GreaterThanOrEqualTo(64));
        }

        [Test]
        public void RaycastCommandBatch_ClearWhilePending_CompletesBeforeMutation()
        {
            using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            using PendingDependency dependency = new PendingDependency();
            JobHandle handle = batch.Schedule(
                new[] { Vector3.zero },
                new[] { Vector3.forward },
                1f,
                dependency: dependency.Handle);

            Assert.That(handle.IsCompleted, Is.False);
            dependency.Release();
            Assert.DoesNotThrow(batch.Clear);
            Assert.That(
                () => batch.GetFirstHit(0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RaycastCommandBatch_DisposeWhilePending_CompletesBeforeDisposal()
        {
            OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            using PendingDependency dependency = new PendingDependency();
            JobHandle handle = batch.Schedule(
                new[] { Vector3.zero },
                new[] { Vector3.forward },
                1f,
                dependency: dependency.Handle);

            try
            {
                Assert.That(handle.IsCompleted, Is.False);
                dependency.Release();
                Assert.DoesNotThrow(batch.Dispose);
            }
            finally
            {
                dependency.Release();
                batch.Dispose();
            }
        }

        [Test]
        public void RaycastCommandBatch_InvalidMinCommandsPerJob_Throws()
        {
            using OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            Vector3[] origins = { Vector3.zero };
            Vector3[] directions = { Vector3.forward };

            Assert.That(
                () => batch.Schedule(origins, directions, 1f, minCommandsPerJob: 0),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => batch.Schedule(origins, directions, 1f, minCommandsPerJob: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RaycastCommandBatch_Disposed_ThrowsOnUsage()
        {
            OnityRaycastCommandBatch batch = new OnityRaycastCommandBatch(4, 1, Allocator.Persistent);
            batch.Dispose();

            Assert.That(
                () => batch.EnsureCapacity(8),
                Throws.TypeOf<ObjectDisposedException>());

            Assert.That(
                () => batch.Clear(),
                Throws.TypeOf<ObjectDisposedException>());
        }

        private sealed class PendingDependency : IDisposable
        {
            private NativeArray<int> m_gate;

            public PendingDependency()
            {
                m_gate = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                Handle = new GateJob
                {
                    Gate = m_gate
                }.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }

            public JobHandle Handle { get; }

            public void Release()
            {
                if (m_gate.IsCreated)
                {
                    m_gate[0] = 1;
                }
            }

            public void Dispose()
            {
                if (m_gate.IsCreated == false)
                {
                    return;
                }

                Release();
                Handle.Complete();
                m_gate.Dispose();
            }
        }

        private struct GateJob : IJob
        {
            [NativeDisableContainerSafetyRestriction]
            public NativeArray<int> Gate;

            public void Execute()
            {
                while (Gate[0] == 0)
                {
                    System.Threading.Thread.Sleep(0);
                }
            }
        }
    }
}
