using NUnit.Framework;
using Onity.DI;
using Onity.Factory;
using Onity.Pooling;
using Onity.Unity.Installers;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class PoolingTests
    {
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
                Object.DestroyImmediate(prefabRoot);
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
                Object.DestroyImmediate(prefabRoot);
            }
        }

        private sealed class PooledReference
        {
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

        private sealed class FactoryProbe : MonoBehaviour
        {
        }
    }
}
