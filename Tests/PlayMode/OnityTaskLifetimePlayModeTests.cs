using System;
using System.Collections;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskLifetimePlayModeTests
    {
        private sealed class LifetimeProbe : MonoBehaviour
        {
        }

        private GameObject m_gameObject;

        [TearDown]
        public void TearDown()
        {
            if (m_gameObject != null)
            {
                Object.Destroy(m_gameObject);
            }

            m_gameObject = null;
        }

        [UnityTest]
        public IEnumerator GameObjectToken_CancelsOnceOnDestroy()
        {
            m_gameObject = new GameObject("lifetime-destroy");
            CancellationToken token = m_gameObject.GetCancellationTokenOnDestroy();
            int cancelCount = 0;
            token.Register(() => cancelCount++);
            Assert.That(token.IsCancellationRequested, Is.False);

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(cancelCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ComponentToken_CancelsOnDestroy()
        {
            m_gameObject = new GameObject("lifetime-component");
            CancellationToken token = m_gameObject.transform.GetCancellationTokenOnDestroy();

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator MonoBehaviourToken_UsesNativeTokenAndAddsNoTrigger()
        {
            m_gameObject = new GameObject("lifetime-mono");
            var probe = m_gameObject.AddComponent<LifetimeProbe>();
            CancellationToken token = probe.GetCancellationTokenOnDestroy();

            Assert.That(token, Is.EqualTo(probe.destroyCancellationToken));
            Assert.That(m_gameObject.GetComponents<Component>().Length, Is.EqualTo(2));

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator RequestAfterDestroy_ReturnsCanceledToken()
        {
            m_gameObject = new GameObject("lifetime-late");
            Transform transform = m_gameObject.transform;
            GameObject owner = m_gameObject;

            Object.Destroy(owner);
            yield return null;

            Assert.That(owner.GetCancellationTokenOnDestroy().IsCancellationRequested, Is.True);
            Assert.That(transform.GetCancellationTokenOnDestroy().IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator RegisterRaiseCancelOnDestroy_CancelsSourceOnDestroy()
        {
            m_gameObject = new GameObject("lifetime-addto");
            using var gameObjectSource = new CancellationTokenSource();
            using var componentSource = new CancellationTokenSource();
            using var registerSource = new CancellationTokenSource();
            gameObjectSource.RegisterRaiseCancelOnDestroy(m_gameObject);
            componentSource.RegisterRaiseCancelOnDestroy(m_gameObject.transform);
            registerSource.RegisterRaiseCancelOnDestroy(m_gameObject);
            Assert.That(gameObjectSource.IsCancellationRequested, Is.False);

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(gameObjectSource.IsCancellationRequested, Is.True);
            Assert.That(componentSource.IsCancellationRequested, Is.True);
            Assert.That(registerSource.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator RegisterRaiseCancelOnDestroy_SourceDisposedBeforeDestroy_DoesNotThrowOrLog()
        {
            m_gameObject = new GameObject("lifetime-disposed-source");
            var source = new CancellationTokenSource();
            source.RegisterRaiseCancelOnDestroy(m_gameObject);
            source.Dispose();

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(m_gameObject == null, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator MultipleRequests_ShareOneTriggerAndToken()
        {
            m_gameObject = new GameObject("lifetime-shared");
            CancellationToken first = m_gameObject.GetCancellationTokenOnDestroy();
            CancellationToken second = m_gameObject.transform.GetCancellationTokenOnDestroy();
            using var source = new CancellationTokenSource();
            source.RegisterRaiseCancelOnDestroy(m_gameObject);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(m_gameObject.GetComponents<Component>().Length, Is.EqualTo(2));
            yield return null;
            Assert.That(first.IsCancellationRequested, Is.False);
        }

        [UnityTest]
        public IEnumerator RepeatedRequest_DoesNotAllocate()
        {
            m_gameObject = new GameObject("lifetime-alloc");
            m_gameObject.GetCancellationTokenOnDestroy();
            yield return null;

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 100; i++)
            {
                m_gameObject.GetCancellationTokenOnDestroy();
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator NeverAwakenedObject_DestroyedWhileInactive_StillCancels()
        {
            m_gameObject = new GameObject("lifetime-inactive");
            m_gameObject.SetActive(false);
            CancellationToken token = m_gameObject.GetCancellationTokenOnDestroy();

            Object.Destroy(m_gameObject);

            for (int i = 0; i < 5 && !token.IsCancellationRequested; i++)
            {
                yield return null;
            }

            Assert.That(token.IsCancellationRequested, Is.True);
        }
    }
}
