using System;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Onity.Tests.EditMode
{
    public sealed class OnityTaskLifetimeEditModeTests
    {
        private GameObject m_gameObject;

        [TearDown]
        public void TearDown()
        {
            if (m_gameObject != null)
            {
                Object.DestroyImmediate(m_gameObject);
            }

            m_gameObject = null;
        }

        [Test]
        public void GetCancellationTokenOnDestroy_NullOwners_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).GetCancellationTokenOnDestroy());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).GetCancellationTokenOnDestroy());
            Assert.Throws<ArgumentNullException>(() => ((MonoBehaviour)null).GetCancellationTokenOnDestroy());
        }

        [Test]
        public void RegisterRaiseCancelOnDestroy_NullArguments_Throw()
        {
            m_gameObject = new GameObject("lifetime-null");
            using var source = new CancellationTokenSource();

            Assert.Throws<ArgumentNullException>(() => source.RegisterRaiseCancelOnDestroy((GameObject)null));
            Assert.Throws<ArgumentNullException>(() => source.RegisterRaiseCancelOnDestroy((Component)null));
            Assert.Throws<ArgumentNullException>(() => source.RegisterRaiseCancelOnDestroy((GameObject)null));
            Assert.Throws<ArgumentNullException>(
                () => ((CancellationTokenSource)null).RegisterRaiseCancelOnDestroy(m_gameObject));
        }

        [Test]
        public void GetCancellationTokenOnDestroy_GameObjectAndComponent_ShareOneHiddenTrigger()
        {
            m_gameObject = new GameObject("lifetime-shared");

            CancellationToken fromObject = m_gameObject.GetCancellationTokenOnDestroy();
            CancellationToken fromComponent = m_gameObject.transform.GetCancellationTokenOnDestroy();
            CancellationToken again = m_gameObject.GetCancellationTokenOnDestroy();

            Assert.That(fromObject.CanBeCanceled, Is.True);
            Assert.That(fromObject.IsCancellationRequested, Is.False);
            Assert.That(fromComponent, Is.EqualTo(fromObject));
            Assert.That(again, Is.EqualTo(fromObject));
            Assert.That(m_gameObject.GetComponents<Component>().Length, Is.EqualTo(2));
        }

        [Test]
        public void GetCancellationTokenOnDestroy_RepeatedCall_DoesNotAllocate()
        {
            m_gameObject = new GameObject("lifetime-alloc");
            m_gameObject.GetCancellationTokenOnDestroy();

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
            {
                m_gameObject.GetCancellationTokenOnDestroy();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void GetCancellationTokenOnDestroy_DestroyedOwner_ReturnsCanceledToken()
        {
            m_gameObject = new GameObject("lifetime-destroyed");
            Transform transform = m_gameObject.transform;
            Object.DestroyImmediate(m_gameObject);

            Assert.That(m_gameObject.GetCancellationTokenOnDestroy().IsCancellationRequested, Is.True);
            Assert.That(transform.GetCancellationTokenOnDestroy().IsCancellationRequested, Is.True);
        }

        [Test]
        public void RegisterRaiseCancelOnDestroy_DestroyedOwner_CancelsSourceImmediately()
        {
            m_gameObject = new GameObject("lifetime-destroyed-source");
            Object.DestroyImmediate(m_gameObject);
            using var source = new CancellationTokenSource();

            source.RegisterRaiseCancelOnDestroy(m_gameObject);

            Assert.That(source.IsCancellationRequested, Is.True);
        }

        [Test]
        public void RegisterRaiseCancelOnDestroy_LiveOwner_DoesNotCancelSource()
        {
            m_gameObject = new GameObject("lifetime-live-source");
            using var source = new CancellationTokenSource();

            source.RegisterRaiseCancelOnDestroy(m_gameObject.transform);
            source.RegisterRaiseCancelOnDestroy(m_gameObject);

            Assert.That(source.IsCancellationRequested, Is.False);
        }
    }
}
