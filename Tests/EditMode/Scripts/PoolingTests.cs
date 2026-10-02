using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Onity.DI;
using Onity.Factory;
using Onity.Pooling;
using Onity.Unity.Installers;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Pool;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class PoolingTests
    {
        private const int k_allocationSampleCount = 64;
        private static byte[] s_allocationProbe;

        [Test]
        public void OnityObjectPool_ReusesReleasedInstances()
        {
            int createCount = 0;
            int getCount = 0;
            int releaseCount = 0;

            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                createFunc: () =>
                {
                    createCount++;
                    return new PooledReference();
                },
                actionOnGet: _ => getCount++,
                actionOnRelease: _ => releaseCount++);

            PooledReference first = pool.Get();
            pool.Release(first);
            PooledReference second = pool.Get();

            Assert.That(createCount, Is.EqualTo(1));
            Assert.That(getCount, Is.EqualTo(2));
            Assert.That(releaseCount, Is.EqualTo(1));
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void OnityObjectPool_GetHookFailure_ReturnsItemToFixedPool()
        {
            int created = 0;
            int getCalls = 0;
            int releaseCalls = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () =>
                {
                    created++;
                    return new PooledReference();
                },
                actionOnGet: _ =>
                {
                    if (++getCalls == 1)
                    {
                        throw new InvalidOperationException("Get hook failed.");
                    }
                },
                actionOnRelease: _ => releaseCalls++,
                initialSize: 1,
                maxSize: 1,
                fixedSize: true);

            Assert.That(() => pool.Get(), Throws.TypeOf<InvalidOperationException>());
            OnityPoolDiagnosticsSnapshot failed = pool.GetDiagnosticsSnapshot();
            Assert.That(failed.CountActive, Is.Zero);
            Assert.That(failed.CountInactive, Is.EqualTo(1));
            Assert.That(failed.GetCount, Is.Zero);
            Assert.That(failed.ReleaseCount, Is.Zero);
            Assert.That(releaseCalls, Is.Zero);

            PooledReference item = pool.Get();
            Assert.That(item, Is.Not.Null);
            Assert.That(created, Is.EqualTo(1));
            pool.Release(item);
            OnityPoolDiagnosticsSnapshot recovered = pool.GetDiagnosticsSnapshot();
            Assert.That(recovered.CountActive, Is.Zero);
            Assert.That(recovered.CountInactive, Is.EqualTo(1));
            Assert.That(recovered.GetCount, Is.EqualTo(1));
            Assert.That(recovered.ReleaseCount, Is.EqualTo(1));
            Assert.That(releaseCalls, Is.EqualTo(1));
        }

        [Test]
        public void OnityObjectPool_Dispose_RejectsFurtherOperations()
        {
            OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference());
            PooledReference item = pool.Get();
            pool.Dispose();

            Assert.That(() => pool.Get(), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => pool.Prewarm(1), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => pool.Release(item), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => pool.Clear(), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void OnityObjectPool_PrewarmAndFixedCapacity_CreateDistinctItemsWithoutHooks()
        {
            int created = 0;
            int getHooks = 0;
            int releaseHooks = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () =>
                {
                    created++;
                    return new PooledReference();
                },
                actionOnGet: _ => getHooks++,
                actionOnRelease: _ => releaseHooks++,
                maxSize: 3,
                initialSize: 3,
                fixedSize: true);

            Assert.That(created, Is.EqualTo(3));
            Assert.That(getHooks, Is.Zero);
            Assert.That(releaseHooks, Is.Zero);

            PooledReference first = pool.Get();
            PooledReference second = pool.Get();
            PooledReference third = pool.Get();
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(second, Is.Not.SameAs(third));
            Assert.That(() => pool.Get(), Throws.TypeOf<InvalidOperationException>());

            pool.Release(second);
            Assert.That(pool.Get(), Is.SameAs(second));
            pool.Release(first);
            pool.Release(second);
            pool.Release(third);
            pool.Prewarm(3);
            Assert.That(created, Is.EqualTo(3));
        }

        [Test]
        public void OnityObjectPool_CheckedReturns_RejectDuplicatesBeforeHooksOrCounters()
        {
            int releaseHooks = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                actionOnRelease: _ => releaseHooks++,
                collectionCheck: true,
                maxSize: 2,
                initialSize: 2,
                fixedSize: true);

            PooledReference first = pool.Get();
            PooledReference second = pool.Get();
            pool.Release(first);
            OnityPoolDiagnosticsSnapshot partial = pool.GetDiagnosticsSnapshot();
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(partial.CountInactive));
            Assert.That(releaseHooks, Is.EqualTo(1));

            pool.Release(second);
            OnityPoolDiagnosticsSnapshot full = pool.GetDiagnosticsSnapshot();
            Assert.That(() => pool.Release(second), Throws.TypeOf<InvalidOperationException>());
            OnityPoolDiagnosticsSnapshot after = pool.GetDiagnosticsSnapshot();
            Assert.That(after.CountAll, Is.EqualTo(full.CountAll));
            Assert.That(after.CountActive, Is.EqualTo(full.CountActive));
            Assert.That(after.CountInactive, Is.EqualTo(full.CountInactive));
            Assert.That(after.ReleaseCount, Is.EqualTo(full.ReleaseCount));
            Assert.That(releaseHooks, Is.EqualTo(2));
        }

        [Test]
        public void OnityObjectPool_CheckedReturns_UseReferenceIdentity()
        {
            using OnityObjectPool<ValueEqualReference> pool = new OnityObjectPool<ValueEqualReference>(
                () => new ValueEqualReference(),
                collectionCheck: true,
                maxSize: 2,
                initialSize: 2,
                fixedSize: true);

            ValueEqualReference first = pool.Get();
            ValueEqualReference second = pool.Get();
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Equals(second), Is.True);
            pool.Release(first);
            pool.Release(second);
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(2));
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnityObjectPool_CheckedReturns_TrackRetriesOverflowAndClear()
        {
            int releaseCalls = 0;
            int destroyed = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                actionOnRelease: _ =>
                {
                    if (++releaseCalls == 1)
                    {
                        throw new InvalidOperationException("Release failed.");
                    }
                },
                actionOnDestroy: _ => destroyed++,
                collectionCheck: true,
                maxSize: 1);

            PooledReference first = pool.Get();
            PooledReference second = pool.Get();
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());
            Assert.That(pool.GetDiagnosticsSnapshot().CountActive, Is.EqualTo(2));
            pool.Release(first);
            pool.Release(second);
            Assert.That(destroyed, Is.EqualTo(1));
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(1));
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());

            pool.Clear();
            PooledReference replacement = pool.Get();
            pool.Release(replacement);
            Assert.That(() => pool.Release(replacement), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnityObjectPool_Clear_DestroysOnlyInactiveItemsAndKeepsActiveCount()
        {
            int destroyed = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                actionOnDestroy: _ => destroyed++,
                collectionCheck: true,
                initialSize: 3,
                maxSize: 3);

            PooledReference active = pool.Get();
            pool.Clear();

            OnityPoolDiagnosticsSnapshot cleared = pool.GetDiagnosticsSnapshot();
            Assert.That(destroyed, Is.EqualTo(2));
            Assert.That(cleared.CountAll, Is.EqualTo(1));
            Assert.That(cleared.CountActive, Is.EqualTo(1));
            Assert.That(cleared.CountInactive, Is.Zero);

            pool.Release(active);
            Assert.That(pool.GetDiagnosticsSnapshot().CountActive, Is.Zero);
            Assert.That(pool.Get(), Is.SameAs(active));
        }

        [Test]
        public void OnityObjectPool_CheckedReturns_FailedReleaseKeepsEarlierItemTracked()
        {
            int releaseCalls = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                actionOnRelease: _ =>
                {
                    if (++releaseCalls == 2)
                    {
                        throw new InvalidOperationException("Release failed.");
                    }
                },
                collectionCheck: true,
                initialSize: 2,
                maxSize: 2,
                fixedSize: true);

            PooledReference first = pool.Get();
            PooledReference second = pool.Get();
            pool.Release(first);
            Assert.That(() => pool.Release(second), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(1));
            pool.Release(second);
            Assert.That(() => pool.Release(first), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => pool.Release(second), Throws.TypeOf<InvalidOperationException>());
            Assert.That(pool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(2));
        }

        [Test]
        public void OnityObjectPool_FailedInitialPrewarmDisposesCreatedItems()
        {
            int created = 0;
            int destroyed = 0;

            Assert.That(() => new OnityObjectPool<PooledReference>(
                () =>
                {
                    if (++created == 2)
                    {
                        throw new InvalidOperationException("Create failed.");
                    }

                    return new PooledReference();
                },
                actionOnDestroy: _ => destroyed++,
                maxSize: 3,
                initialSize: 3), Throws.TypeOf<InvalidOperationException>());

            Assert.That(destroyed, Is.EqualTo(1));
        }

        [Test]
        public void PooledFactories_ApplyNewParametersBeforeEachGetHook()
        {
            int seenNumber = 0;
            string seenTag = null;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                actionOnGet: item =>
                {
                    seenNumber = item.Number;
                    seenTag = item.Tag;
                },
                collectionCheck: true,
                initialSize: 1,
                maxSize: 1,
                fixedSize: true);
            PooledFactory<int, PooledReference> one = new PooledFactory<int, PooledReference>(
                pool, (item, number) => item.Number = number);
            PooledFactory<int, string, PooledReference> two =
                new PooledFactory<int, string, PooledReference>(
                    pool,
                    (item, number, tag) =>
                    {
                        item.Number = number;
                        item.Tag = tag;
                    });

            PooledReference first = one.Create(7);
            Assert.That(seenNumber, Is.EqualTo(7));
            pool.Release(first);

            PooledReference reused = two.Create(9, "second");
            Assert.That(reused, Is.SameAs(first));
            Assert.That(seenNumber, Is.EqualTo(9));
            Assert.That(seenTag, Is.EqualTo("second"));
            pool.Release(reused);
        }

        [Test]
        public void TwoParameterPooledFactory_UsesExplicitInterfaceImplementation()
        {
            ExplicitParameterPool pool = new ExplicitParameterPool();
            PooledFactory<int, string, PooledReference> factory =
                new PooledFactory<int, string, PooledReference>(
                    pool,
                    (item, number, tag) =>
                    {
                        item.Number = number;
                        item.Tag = tag;
                    });

            PooledReference item = factory.Create(12, "custom");

            Assert.That(pool.GetCount, Is.EqualTo(1));
            Assert.That(item.Number, Is.EqualTo(12));
            Assert.That(item.Tag, Is.EqualTo("custom"));
        }

        [Test]
        public void TwoParameterPooledFactory_RejectsNullArguments()
        {
            using OnityObjectPool<PooledReference> pool =
                new OnityObjectPool<PooledReference>(() => new PooledReference());
            Action<PooledReference, int, int> initialize =
                (item, first, second) => item.Number = first + second;

            Assert.That(() => new PooledFactory<int, int, PooledReference>(null, initialize),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(() => new PooledFactory<int, int, PooledReference>(pool, null),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void TwoParameterPooledFactory_RecoversFromInitializationFailureAndRejectsExhaustion()
        {
            int created = 0;
            bool rejectFirst = true;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () =>
                {
                    created++;
                    return new PooledReference();
                },
                initialSize: 1,
                maxSize: 1,
                fixedSize: true);
            PooledFactory<int, int, PooledReference> factory =
                new PooledFactory<int, int, PooledReference>(
                    pool,
                    (item, first, second) =>
                    {
                        if (rejectFirst)
                        {
                            rejectFirst = false;
                            throw new InvalidOperationException("Initialize failed.");
                        }

                        item.Number = first + second;
                    });

            Assert.That(() => factory.Create(1, 2), Throws.TypeOf<InvalidOperationException>());
            OnityPoolDiagnosticsSnapshot failed = pool.GetDiagnosticsSnapshot();
            Assert.That(failed.CountActive, Is.Zero);
            Assert.That(failed.CountInactive, Is.EqualTo(1));
            Assert.That(failed.GetCount, Is.Zero);

            PooledReference item = factory.Create(3, 4);
            Assert.That(item.Number, Is.EqualTo(7));
            Assert.That(() => factory.Create(5, 6), Throws.TypeOf<InvalidOperationException>());
            pool.Release(item);

            PooledReference reused = factory.Create(8, 9);
            Assert.That(reused, Is.SameAs(item));
            Assert.That(reused.Number, Is.EqualTo(17));
            Assert.That(created, Is.EqualTo(1));
            pool.Release(reused);

            pool.Dispose();
            Assert.That(() => factory.Create(10, 11), Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public void OnityObjectPool_InitializerFailure_RestoresFixedCapacityWithoutHooks()
        {
            int created = 0;
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () =>
                {
                    created++;
                    return new PooledReference();
                },
                collectionCheck: true,
                initialSize: 1,
                maxSize: 1,
                fixedSize: true);

            Assert.That(() => pool.Get(1, (item, value) =>
            {
                throw new InvalidOperationException("Initialize failed.");
            }), Throws.TypeOf<InvalidOperationException>());

            PooledReference item = pool.Get();
            Assert.That(item, Is.Not.Null);
            Assert.That(created, Is.EqualTo(1));
            pool.Release(item);
            Assert.That(() => pool.Release(item), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void OnityObjectPool_CheckedWarmedRentReturn_DoesNotAllocate()
        {
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                collectionCheck: true,
                initialSize: 32,
                maxSize: 32,
                fixedSize: true);
            Action operation = () =>
            {
                PooledReference item = pool.Get();
                pool.Release(item);
            };

            Assert.That(CountAllocEvents(operation), Is.Zero);
            PooledReference finalItem = pool.Get();
            pool.Release(finalItem);
            Assert.That(() => pool.Release(finalItem), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void WarmedPoolAndPooledFactories_DoNotAllocatePerRentReturn()
        {
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(),
                initialSize: 1,
                maxSize: 1,
                fixedSize: true);
            PooledFactory<PooledReference> plainFactory = new PooledFactory<PooledReference>(pool);
            PooledFactory<int, PooledReference> oneFactory = new PooledFactory<int, PooledReference>(
                pool, (item, number) => item.Number = number);
            PooledFactory<int, int, PooledReference> twoFactory =
                new PooledFactory<int, int, PooledReference>(
                    pool, (item, first, second) => item.Number = first + second);

            Action positiveControl = () => s_allocationProbe = new byte[1024];
            Action direct = () =>
            {
                PooledReference item = pool.Get();
                pool.Release(item);
            };
            Action plain = () =>
            {
                PooledReference item = plainFactory.Create();
                pool.Release(item);
            };
            Action one = () =>
            {
                PooledReference item = oneFactory.Create(1);
                pool.Release(item);
            };
            Action two = () =>
            {
                PooledReference item = twoFactory.Create(1, 2);
                pool.Release(item);
            };

            Assert.That(CountAllocEvents(positiveControl), Is.GreaterThan(0),
                "The allocation counter must detect its positive control.");
            Assert.That(CountAllocEvents(direct), Is.Zero);
            Assert.That(CountAllocEvents(plain), Is.Zero);
            Assert.That(CountAllocEvents(one), Is.Zero);
            Assert.That(CountAllocEvents(two), Is.Zero);
        }

        [Test]
        [Explicit("Run by exact name to measure warmed pool and factory dispatch in the Editor.")]
        public void MeasureWarmedRentReturn()
        {
            using ObjectPool<PooledReference> raw = new ObjectPool<PooledReference>(
                () => new PooledReference(), null, null, null, false, 16, 16);
            using OnityObjectPool<PooledReference> pool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(), initialSize: 1, maxSize: 16);
            Action<PooledReference, int> initializeOne = (item, number) => item.Number = number;
            Action<PooledReference, int, int> initializeTwo =
                (item, first, second) => item.Number = first + second;
            PooledFactory<PooledReference> plainFactory = new PooledFactory<PooledReference>(pool);
            PooledFactory<int, PooledReference> oneFactory = new PooledFactory<int, PooledReference>(
                pool, initializeOne);
            PooledFactory<int, int, PooledReference> twoFactory =
                new PooledFactory<int, int, PooledReference>(pool, initializeTwo);

            PooledReference rawItem = raw.Get();
            raw.Release(rawItem);
            long trackedGets = 0;
            long trackedReleases = 0;

            Action rawOperation = () =>
            {
                PooledReference item = raw.Get();
                raw.Release(item);
            };
            Action poolOperation = () =>
            {
                PooledReference item = pool.Get();
                pool.Release(item);
            };
            Action trackedRawOperation = () =>
            {
                PooledReference item = raw.Get();
                Interlocked.Increment(ref trackedGets);
                raw.Release(item);
                Interlocked.Increment(ref trackedReleases);
            };
            Action plainOperation = () =>
            {
                PooledReference item = plainFactory.Create();
                pool.Release(item);
            };
            Action oneOperation = () =>
            {
                PooledReference item = oneFactory.Create(1);
                pool.Release(item);
            };
            Action directOneOperation = () =>
            {
                PooledReference item = pool.Get(1, initializeOne);
                pool.Release(item);
            };
            Action twoOperation = () =>
            {
                PooledReference item = twoFactory.Create(1, 2);
                pool.Release(item);
            };
            Action directTwoOperation = () =>
            {
                PooledReference item = pool.Get(1, 2, initializeTwo);
                pool.Release(item);
            };

            string[] names =
            {
                "Raw Unity pool",
                "Raw Unity pool + atomic counters",
                "Onity pool",
                "Onity pooled factory",
                "Onity pool (1 param)",
                "Onity pooled factory (1 param)",
                "Onity pool (2 params)",
                "Onity pooled factory (2 params)"
            };
            Action[] operations =
            {
                rawOperation,
                trackedRawOperation,
                poolOperation,
                plainOperation,
                directOneOperation,
                oneOperation,
                directTwoOperation,
                twoOperation
            };

            MeasureInterleaved(names, operations);
        }

        [Test]
        [Explicit("Run by exact name to compare warmed Onity and Zenject pools in the Editor.")]
        public void MeasureWarmedRentReturnAgainstZenject()
        {
            using OnityObjectPool<PooledReference> defaultPool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(), initialSize: 1, maxSize: 16);
            using OnityObjectPool<PooledReference> checkedPool = new OnityObjectPool<PooledReference>(
                () => new PooledReference(), collectionCheck: true, initialSize: 1, maxSize: 16);
            object zenjectPool = CreateZenjectPool(typeof(PooledReference), 1, 16);

            try
            {
                Type zenjectPoolType = zenjectPool.GetType();
                MethodInfo spawnMethod = zenjectPoolType.GetMethod("Spawn", Type.EmptyTypes);
                MethodInfo despawnMethod = zenjectPoolType.GetMethod(
                    "Despawn", new[] { typeof(PooledReference) });
                Assert.That(spawnMethod, Is.Not.Null);
                Assert.That(despawnMethod, Is.Not.Null);
                Func<PooledReference> zenjectSpawn = (Func<PooledReference>)Delegate.CreateDelegate(
                    typeof(Func<PooledReference>), zenjectPool, spawnMethod);
                Action<PooledReference> zenjectDespawn = (Action<PooledReference>)Delegate.CreateDelegate(
                    typeof(Action<PooledReference>), zenjectPool, despawnMethod);
                Func<PooledReference> defaultGet = defaultPool.Get;
                Action<PooledReference> defaultRelease = defaultPool.Release;
                Func<PooledReference> checkedGet = checkedPool.Get;
                Action<PooledReference> checkedRelease = checkedPool.Release;

                Assert.That(defaultPool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(1));
                Assert.That(checkedPool.GetDiagnosticsSnapshot().CountInactive, Is.EqualTo(1));
                Assert.That(zenjectPoolType.GetProperty("NumInactive")?.GetValue(zenjectPool), Is.EqualTo(1));
                Type profileBlockType = zenjectPoolType.Assembly.GetType("Zenject.ProfileBlock");
                object zenjectMainThread = profileBlockType?.GetProperty("UnityMainThread")?.GetValue(null);
                TestContext.WriteLine($"Profiler.enabled={UnityEngine.Profiling.Profiler.enabled}, " +
                    $"Zenject UnityMainThread=current thread: " +
                    $"{Thread.CurrentThread.Equals(zenjectMainThread)}");

                Action[] operations =
                {
                    () =>
                    {
                        PooledReference item = defaultGet();
                        defaultRelease(item);
                    },
                    () =>
                    {
                        PooledReference item = checkedGet();
                        checkedRelease(item);
                    },
                    () =>
                    {
                        PooledReference item = zenjectSpawn();
                        zenjectDespawn(item);
                    }
                };
                string[] names =
                {
                    "Onity pool (default)",
                    "Onity pool (collection check)",
                    "Zenject MemoryPool"
                };

                Action positiveControl = () => s_allocationProbe = new byte[1024];
                Assert.That(CountAllocEvents(positiveControl), Is.GreaterThan(0));
                int defaultAllocEvents = CountAllocEvents(operations[0]);
                int checkedAllocEvents = CountAllocEvents(operations[1]);
                int zenjectAllocEvents = CountAllocEvents(operations[2]);
                Assert.That(defaultAllocEvents, Is.Zero);
                Assert.That(checkedAllocEvents, Is.Zero);
                TestContext.WriteLine($"Warmed GC.Alloc events per 64 pairs: Onity default " +
                    $"{defaultAllocEvents}, Onity checked {checkedAllocEvents}, Zenject {zenjectAllocEvents}");

                MeasurePoolComparison(names, operations, 2);
                OnityPoolDiagnosticsSnapshot defaultFinal = defaultPool.GetDiagnosticsSnapshot();
                OnityPoolDiagnosticsSnapshot checkedFinal = checkedPool.GetDiagnosticsSnapshot();
                Assert.That(defaultFinal.CountActive, Is.Zero);
                Assert.That(defaultFinal.CountInactive, Is.EqualTo(1));
                Assert.That(checkedFinal.CountActive, Is.Zero);
                Assert.That(checkedFinal.CountInactive, Is.EqualTo(1));
                Assert.That(zenjectPoolType.GetProperty("NumActive")?.GetValue(zenjectPool), Is.EqualTo(0));
                Assert.That(zenjectPoolType.GetProperty("NumInactive")?.GetValue(zenjectPool), Is.EqualTo(1));
                Assert.That(zenjectPoolType.GetProperty("NumTotal")?.GetValue(zenjectPool), Is.EqualTo(1));
            }
            finally
            {
                (zenjectPool as IDisposable)?.Dispose();
            }
        }

        [Test]
        [Explicit("Run by exact name to isolate checked Onity pool wrapper cost in the Editor.")]
        public void MeasureCheckedWarmedRentReturnAgainstUnity()
        {
            using ObjectPool<PooledReference> raw = new ObjectPool<PooledReference>(
                () => new PooledReference(), null, null, null, true, 16, 16);
            using OnityObjectPool<PooledReference> onity = new OnityObjectPool<PooledReference>(
                () => new PooledReference(), collectionCheck: true,
                defaultCapacity: 16, maxSize: 16, initialSize: 1);
            PooledReference prewarmed = raw.Get();
            raw.Release(prewarmed);

            Func<PooledReference> rawGet = raw.Get;
            Action<PooledReference> rawRelease = raw.Release;
            Func<PooledReference> onityGet = onity.Get;
            Action<PooledReference> onityRelease = onity.Release;
            long trackedGets = 0;
            long trackedReleases = 0;

            Action[] operations =
            {
                () =>
                {
                    PooledReference item = rawGet();
                    rawRelease(item);
                },
                () =>
                {
                    PooledReference item = rawGet();
                    Interlocked.Increment(ref trackedGets);
                    rawRelease(item);
                    Interlocked.Increment(ref trackedReleases);
                },
                () =>
                {
                    PooledReference item = onityGet();
                    onityRelease(item);
                }
            };
            string[] names =
            {
                "Unity pool (collection check)",
                "Unity pool (collection check + diagnostic atomics)",
                "Onity pool (collection check)"
            };

            Action positiveControl = () => s_allocationProbe = new byte[1024];
            Assert.That(CountAllocEvents(positiveControl), Is.GreaterThan(0));
            for (int i = 0; i < operations.Length; i++)
            {
                Assert.That(CountAllocEvents(operations[i]), Is.Zero, names[i]);
            }

            MeasurePoolComparison(names, operations, 1);

            OnityPoolDiagnosticsSnapshot final = onity.GetDiagnosticsSnapshot();
            Assert.That(final.CountActive, Is.Zero);
            Assert.That(final.CountInactive, Is.EqualTo(1));
            Assert.That(final.GetCount, Is.EqualTo(final.ReleaseCount));
            Assert.That(raw.CountAll, Is.EqualTo(1));
            Assert.That(raw.CountActive, Is.Zero);
            Assert.That(raw.CountInactive, Is.EqualTo(1));
            Assert.That(trackedGets, Is.EqualTo(trackedReleases));
            TestContext.WriteLine($"Profiler.enabled={UnityEngine.Profiling.Profiler.enabled}; " +
                "warmed GC.Alloc events per 64 pairs: all three paths 0");
        }

        [Test]
        public void PrefabComponentPool_GetAndRelease_InvokesHooksAndTogglesActiveState()
        {
            GameObject prefabRoot = new GameObject("PoolingTestPrefab");
            PoolHookProbe prefabProbe = prefabRoot.AddComponent<PoolHookProbe>();

            try
            {
                using PrefabComponentPool<PoolHookProbe> pool = new PrefabComponentPool<PoolHookProbe>(prefabProbe);
                PoolHookProbe instance = pool.Get();

                Assert.That(instance.gameObject.activeSelf, Is.True);
                Assert.That(instance.OnGetCount, Is.EqualTo(1));
                Assert.That(instance.OnReleaseCount, Is.EqualTo(0));

                pool.Release(instance);

                Assert.That(instance.gameObject.activeSelf, Is.False);
                Assert.That(instance.OnReleaseCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void PrefabComponentPool_GetHookFailure_ReturnsItemToFixedPool()
        {
            GameObject prefabRoot = new GameObject("ThrowingPoolPrefab");
            ThrowingPoolHookProbe prefab = prefabRoot.AddComponent<ThrowingPoolHookProbe>();

            try
            {
                using PrefabComponentPool<ThrowingPoolHookProbe> pool =
                    new PrefabComponentPool<ThrowingPoolHookProbe>(
                        prefab, initialSize: 1, maxSize: 1, fixedSize: true);

                Assert.That(() => pool.Get(), Throws.TypeOf<InvalidOperationException>());
                ThrowingPoolHookProbe item = pool.Get();
                Assert.That(item, Is.Not.Null);
                pool.Release(item);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void PrefabComponentPool_Dispose_RejectsFurtherOperations()
        {
            GameObject prefabRoot = new GameObject("DisposedPoolPrefab");
            PoolHookProbe prefab = prefabRoot.AddComponent<PoolHookProbe>();
            PoolHookProbe item = null;

            try
            {
                PrefabComponentPool<PoolHookProbe> pool = new PrefabComponentPool<PoolHookProbe>(prefab);
                item = pool.Get();
                pool.Dispose();

                Assert.That(() => pool.Get(), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => pool.Prewarm(1), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => pool.Release(item), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => pool.Clear(), Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void WarmedPrefabPool_DoesNotAllocatePerRentReturn()
        {
            GameObject prefabRoot = new GameObject("AllocationPoolPrefab");
            FactoryProbe prefab = prefabRoot.AddComponent<FactoryProbe>();

            try
            {
                using PrefabComponentPool<FactoryProbe> pool = new PrefabComponentPool<FactoryProbe>(
                    prefab, initialSize: 1, maxSize: 1, fixedSize: true);
                Action operation = () =>
                {
                    FactoryProbe item = pool.Get();
                    pool.Release(item);
                };

                Assert.That(CountAllocEvents(operation), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void PrefabComponentPool_PrewarmAndParameterizedGet_ApplyStateBeforePoolHook()
        {
            GameObject prefabRoot = new GameObject("ParameterizedPrefab");
            ParameterizedPoolProbe prefab = prefabRoot.AddComponent<ParameterizedPoolProbe>();

            try
            {
                using PrefabComponentPool<ParameterizedPoolProbe> pool =
                    new PrefabComponentPool<ParameterizedPoolProbe>(
                        prefab, maxSize: 1, initialSize: 1, fixedSize: true);
                PooledFactory<int, ParameterizedPoolProbe> factory =
                    new PooledFactory<int, ParameterizedPoolProbe>(
                        pool, (item, value) => item.Value = value);

                ParameterizedPoolProbe first = factory.Create(7);
                Assert.That(first.SeenOnGet, Is.EqualTo(7));
                Assert.That(first.ActiveOnGet, Is.True);
                Assert.That(() => factory.Create(8), Throws.TypeOf<InvalidOperationException>());
                pool.Release(first);

                ParameterizedPoolProbe reused = factory.Create(9);
                Assert.That(reused, Is.SameAs(first));
                Assert.That(reused.SeenOnGet, Is.EqualTo(9));
                pool.Release(reused);
                pool.Clear();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void BindPooledFactory_BindsFactoryAndPool_WithSingleCall()
        {
            GameObject prefabRoot = new GameObject("FactoryBindingPrefab");
            FactoryProbe prefabProbe = prefabRoot.AddComponent<FactoryProbe>();

            using OnityContainer container = new OnityContainer();
            container.BindPooledFactory(prefabProbe);

            IFactory<FactoryProbe> factory = container.Resolve<IFactory<FactoryProbe>>();
            IPool<FactoryProbe> pool = container.Resolve<IPool<FactoryProbe>>();

            FactoryProbe first = factory.Create();
            pool.Release(first);
            FactoryProbe second = factory.Create();

            try
            {
                Assert.That(second, Is.SameAs(first));
                Assert.That(second.gameObject.activeSelf, Is.True);
            }
            finally
            {
                pool.Release(second);
                pool.Clear();
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void BindPooledFactory_ContainerDispose_DisposesPrefabPoolItCreated()
        {
            GameObject prefabRoot = new GameObject("ScopedPoolPrefab-" + Guid.NewGuid().ToString("N"));
            FactoryProbe prefabProbe = prefabRoot.AddComponent<FactoryProbe>();
            OnityContainer container = new OnityContainer();
            FactoryProbe inactive = null;
            FactoryProbe active = null;

            try
            {
                container.BindPooledFactory(prefabProbe);
                IFactory<FactoryProbe> factory = container.Resolve<IFactory<FactoryProbe>>();
                IPool<FactoryProbe> pool = container.Resolve<IPool<FactoryProbe>>();
                IOnityPoolDiagnosticsSource diagnostics = (IOnityPoolDiagnosticsSource)pool;
                string poolName = diagnostics.GetDiagnosticsSnapshot().PoolName;
                inactive = factory.Create();
                active = factory.Create();
                pool.Release(inactive);

                Assert.That(IsRegisteredInDiagnostics(poolName), Is.True);

                container.Dispose();

                Assert.That(IsRegisteredInDiagnostics(poolName), Is.False);
                Assert.That(diagnostics.GetDiagnosticsSnapshot().IsDisposed, Is.True);
                Assert.That(() => pool.Get(), Throws.TypeOf<ObjectDisposedException>());
                Assert.That(inactive == null, Is.True, "The inactive instance is destroyed with the pool.");
                Assert.That(active != null, Is.True, "A checked-out instance stays with its owner.");
            }
            finally
            {
                container.Dispose();

                if (active != null)
                {
                    UnityEngine.Object.DestroyImmediate(active.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void BindPooledFactory_ContainerDispose_LeavesCallerSuppliedPoolUndisposed()
        {
            GameObject prefabRoot = new GameObject("CallerPoolPrefab-" + Guid.NewGuid().ToString("N"));
            FactoryProbe prefabProbe = prefabRoot.AddComponent<FactoryProbe>();
            PrefabComponentPool<FactoryProbe> pool = new PrefabComponentPool<FactoryProbe>(prefabProbe);
            string poolName = pool.GetDiagnosticsSnapshot().PoolName;
            OnityContainer container = new OnityContainer();
            FactoryProbe item = null;

            try
            {
                container.BindPooledFactory(pool);

                container.Dispose();

                Assert.That(pool.GetDiagnosticsSnapshot().IsDisposed, Is.False);
                Assert.That(IsRegisteredInDiagnostics(poolName), Is.True);

                item = pool.Get();
                Assert.That(item.gameObject.activeSelf, Is.True);
                pool.Release(item);
                item = null;
            }
            finally
            {
                container.Dispose();
                pool.Dispose();

                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item.gameObject);
                }

                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }

            Assert.That(IsRegisteredInDiagnostics(poolName), Is.False);
        }

        [Test]
        public void BindPooledFactory_CallerSuppliedPoolAddedToScope_DisposesWithContainer()
        {
            GameObject prefabRoot = new GameObject("TiedPoolPrefab-" + Guid.NewGuid().ToString("N"));
            FactoryProbe prefabProbe = prefabRoot.AddComponent<FactoryProbe>();
            PrefabComponentPool<FactoryProbe> pool = new PrefabComponentPool<FactoryProbe>(prefabProbe);
            string poolName = pool.GetDiagnosticsSnapshot().PoolName;
            OnityContainer container = new OnityContainer();

            try
            {
                container.BindPooledFactory(pool);
                pool.AddTo(container);

                Assert.That(pool.GetDiagnosticsSnapshot().IsDisposed, Is.False);

                container.Dispose();

                Assert.That(pool.GetDiagnosticsSnapshot().IsDisposed, Is.True);
                Assert.That(IsRegisteredInDiagnostics(poolName), Is.False);
            }
            finally
            {
                container.Dispose();
                pool.Dispose();
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        [Test]
        public void BindPooledFactory_OnDisposedContainer_ThrowsWithoutLeavingARegisteredPool()
        {
            GameObject prefabRoot = new GameObject("DisposedScopePrefab-" + Guid.NewGuid().ToString("N"));
            FactoryProbe prefabProbe = prefabRoot.AddComponent<FactoryProbe>();
            OnityContainer container = new OnityContainer();
            container.Dispose();
            int registeredBefore = CountRegisteredInDiagnostics();

            try
            {
                Assert.That(() => container.BindPooledFactory(prefabProbe), Throws.Exception);
                Assert.That(CountRegisteredInDiagnostics(), Is.EqualTo(registeredBefore));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            }
        }

        private static bool IsRegisteredInDiagnostics(string poolName)
        {
            List<OnityPoolDiagnosticsSnapshot> snapshots = new List<OnityPoolDiagnosticsSnapshot>();
            OnityPoolDiagnosticsRegistry.GetSnapshots(snapshots);

            for (int i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i].PoolName == poolName)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountRegisteredInDiagnostics()
        {
            List<OnityPoolDiagnosticsSnapshot> snapshots = new List<OnityPoolDiagnosticsSnapshot>();
            OnityPoolDiagnosticsRegistry.GetSnapshots(snapshots);
            return snapshots.Count;
        }

        private sealed class PooledReference
        {
            public int Number;
            public string Tag;
        }

        private sealed class ValueEqualReference
        {
            public override bool Equals(object other)
            {
                return other is ValueEqualReference;
            }

            public override int GetHashCode()
            {
                return 1;
            }
        }

        private sealed class ExplicitParameterPool : IParameterizedPool<PooledReference>
        {
            private readonly PooledReference m_item = new PooledReference();

            public int GetCount { get; private set; }

            PooledReference IPool<PooledReference>.Get()
            {
                return m_item;
            }

            PooledReference IParameterizedPool<PooledReference>.Get<TParam>(
                TParam param, Action<PooledReference, TParam> initialize)
            {
                initialize(m_item, param);
                return m_item;
            }

            PooledReference IParameterizedPool<PooledReference>.Get<TParam1, TParam2>(
                TParam1 param1, TParam2 param2,
                Action<PooledReference, TParam1, TParam2> initialize)
            {
                GetCount++;
                initialize(m_item, param1, param2);
                return m_item;
            }

            void IPool<PooledReference>.Release(PooledReference item)
            {
            }

            void IPool<PooledReference>.Clear()
            {
            }
        }

        private static int CountAllocEvents(Action operation)
        {
            for (int i = 0; i < k_allocationSampleCount; i++)
            {
                operation();
            }

            using ProfilerRecorder recorder = new ProfilerRecorder(
                ProfilerCategory.Internal,
                "GC.Alloc",
                1024,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            Assert.That(recorder.Valid, Is.True);
            recorder.Start();

            for (int i = 0; i < k_allocationSampleCount; i++)
            {
                operation();
            }

            recorder.Stop();
            Assert.That(recorder.WrappedAround, Is.False);
            return recorder.Count;
        }

        private static object CreateZenjectPool(Type itemType, int initialSize, int maxSize)
        {
            Type containerType = Type.GetType("Zenject.DiContainer, Zenject");
            Type openPoolType = Type.GetType("Zenject.MemoryPool`1, Zenject");
            Assert.That(containerType, Is.Not.Null, "Zenject reference assembly is required for this explicit benchmark.");
            Assert.That(openPoolType, Is.Not.Null);
            object container = Activator.CreateInstance(containerType);
            MethodInfo bindPool = null;

            foreach (MethodInfo method in containerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name == "BindMemoryPool" && method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0)
                {
                    bindPool = method;
                    break;
                }
            }

            Assert.That(bindPool, Is.Not.Null);
            object binder = bindPool.MakeGenericMethod(itemType).Invoke(container, null);
            MethodInfo withInitialSize = binder.GetType().GetMethod("WithInitialSize", new[] { typeof(int) });
            Assert.That(withInitialSize, Is.Not.Null);
            object maxSizeBinder = withInitialSize.Invoke(binder, new object[] { initialSize });
            MethodInfo withMaxSize = maxSizeBinder.GetType().GetMethod("WithMaxSize", new[] { typeof(int) });
            Assert.That(withMaxSize, Is.Not.Null);
            withMaxSize.Invoke(maxSizeBinder, new object[] { maxSize });

            Type poolType = openPoolType.MakeGenericType(itemType);
            MethodInfo resolve = containerType.GetMethod("Resolve", new[] { typeof(Type) });
            Assert.That(resolve, Is.Not.Null);
            return resolve.Invoke(container, new object[] { poolType });
        }

        private static void MeasurePoolComparison(string[] names, Action[] operations, int referenceIndex)
        {
            const int warmupIterations = 10000;
            const int measuredIterations = 250000;
            const int sampleCount = 11;
            double[][] nanosecondsPerPair = new double[operations.Length][];

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                nanosecondsPerPair[caseIndex] = new double[sampleCount];
                for (int i = 0; i < warmupIterations; i++)
                {
                    operations[caseIndex]();
                }
            }

            for (int sample = 0; sample < sampleCount; sample++)
            {
                for (int slot = 0; slot < operations.Length; slot++)
                {
                    int caseIndex = (slot + sample) % operations.Length;
                    Action operation = operations[caseIndex];
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < measuredIterations; i++)
                    {
                        operation();
                    }
                    long elapsedTicks = Stopwatch.GetTimestamp() - start;
                    nanosecondsPerPair[caseIndex][sample] = elapsedTicks * 1000000000d /
                        (Stopwatch.Frequency * measuredIterations);
                }
            }

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                if (caseIndex == referenceIndex)
                {
                    continue;
                }

                double[] ratios = new double[sampleCount];
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    ratios[sample] = nanosecondsPerPair[caseIndex][sample] /
                        nanosecondsPerPair[referenceIndex][sample];
                }

                Array.Sort(ratios);
                TestContext.WriteLine($"{names[caseIndex]}/{names[referenceIndex]} paired median: " +
                    $"{ratios[sampleCount / 2]:F3}x, " +
                    $"Q1-Q3 {ratios[2]:F3}-{ratios[8]:F3}, " +
                    $"range {ratios[0]:F3}-{ratios[sampleCount - 1]:F3}");
            }

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                double[] samples = nanosecondsPerPair[caseIndex];
                Array.Sort(samples);
                TestContext.WriteLine($"{names[caseIndex]}: median {samples[sampleCount / 2]:F1} ns/pair, " +
                    $"range {samples[0]:F1}-{samples[sampleCount - 1]:F1}");
            }
        }

        private static void MeasureInterleaved(string[] names, Action[] operations)
        {
            const int warmupIterations = 10000;
            const int measuredIterations = 250000;
            const int sampleCount = 11;

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                for (int i = 0; i < warmupIterations; i++)
                {
                    operations[caseIndex]();
                }
            }

            double[][] nanosecondsPerPair = new double[operations.Length][];

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                nanosecondsPerPair[caseIndex] = new double[sampleCount];
            }

            for (int sample = 0; sample < sampleCount; sample++)
            {
                for (int slot = 0; slot < operations.Length; slot++)
                {
                    int caseIndex = (slot + sample) % operations.Length;
                    Action operation = operations[caseIndex];
                    long start = Stopwatch.GetTimestamp();

                    for (int i = 0; i < measuredIterations; i++)
                    {
                        operation();
                    }

                    long elapsedTicks = Stopwatch.GetTimestamp() - start;
                    nanosecondsPerPair[caseIndex][sample] = elapsedTicks * 1000000000d /
                        (Stopwatch.Frequency * measuredIterations);
                }
            }

            double[] onityToRawRatios = new double[sampleCount];
            double[] onityToTrackedRawRatios = new double[sampleCount];
            double[] factoryToPoolRatios = new double[sampleCount];

            for (int sample = 0; sample < sampleCount; sample++)
            {
                onityToRawRatios[sample] = nanosecondsPerPair[2][sample] / nanosecondsPerPair[0][sample];
                onityToTrackedRawRatios[sample] = nanosecondsPerPair[2][sample] /
                    nanosecondsPerPair[1][sample];
                factoryToPoolRatios[sample] = nanosecondsPerPair[3][sample] / nanosecondsPerPair[2][sample];
            }

            for (int caseIndex = 0; caseIndex < operations.Length; caseIndex++)
            {
                double[] samples = nanosecondsPerPair[caseIndex];
                Array.Sort(samples);
                TestContext.WriteLine(
                    $"{names[caseIndex]}: median {samples[sampleCount / 2]:F1} ns/pair, " +
                    $"range {samples[0]:F1}-{samples[sampleCount - 1]:F1}");
            }

            Array.Sort(onityToRawRatios);
            Array.Sort(onityToTrackedRawRatios);
            Array.Sort(factoryToPoolRatios);
            TestContext.WriteLine($"Onity/raw paired median: {onityToRawRatios[sampleCount / 2]:F2}x");
            TestContext.WriteLine($"Onity/raw + atomics paired median: " +
                $"{onityToTrackedRawRatios[sampleCount / 2]:F2}x");
            TestContext.WriteLine($"Factory/pool paired median: {factoryToPoolRatios[sampleCount / 2]:F2}x");
        }

        private sealed class ParameterizedPoolProbe : MonoBehaviour, IPoolHooks
        {
            public int Value { get; set; }
            public int SeenOnGet { get; private set; }
            public bool ActiveOnGet { get; private set; }

            public void OnPoolGet()
            {
                SeenOnGet = Value;
                ActiveOnGet = gameObject.activeSelf;
            }

            public void OnPoolRelease()
            {
            }
        }

        private sealed class PoolHookProbe : MonoBehaviour, IPoolHooks
        {
            public int OnGetCount { get; private set; }

            public int OnReleaseCount { get; private set; }

            public void OnPoolGet()
            {
                OnGetCount++;
            }

            public void OnPoolRelease()
            {
                OnReleaseCount++;
            }
        }

        private sealed class ThrowingPoolHookProbe : MonoBehaviour, IPoolHooks
        {
            private int m_getCount;

            public void OnPoolGet()
            {
                if (++m_getCount == 1)
                {
                    throw new InvalidOperationException("Get hook failed.");
                }
            }

            public void OnPoolRelease()
            {
            }
        }

        private sealed class FactoryProbe : MonoBehaviour
        {
        }
    }
}
