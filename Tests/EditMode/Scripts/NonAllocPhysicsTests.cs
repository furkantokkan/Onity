using System;
using NUnit.Framework;
using Onity.Unity.Physics;
using Unity.Collections;
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
    }
}
