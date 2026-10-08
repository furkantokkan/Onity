using System.Collections;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityUnityEventAsyncPlayModeTests
    {
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
        public IEnumerator DestroyToken_CancelsAPendingWait_AndEndsAnEnumeration()
        {
            m_gameObject = new GameObject("unity-event-destroy");
            CancellationToken destroyed = m_gameObject.GetCancellationTokenOnDestroy();
            UnityEvent unityEvent = new UnityEvent();
            UnityEvent<int> typed = new UnityEvent<int>();
            OnityTask wait = unityEvent.OnInvokeAsync(destroyed);
            OnityTask<int> typedWait = typed.OnInvokeAsync(destroyed);
            OnityAsyncUnityEventHandler handler = unityEvent.GetAsyncEventHandler(destroyed);
            OnityTask handlerWait = handler.OnInvokeAsync();
            IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(destroyed)
                .GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();
            yield return null;

            Assert.That(wait.IsCompleted, Is.False);

            Object.Destroy(m_gameObject);
            for (int frame = 0; frame < 3 && !(wait.IsCompleted && move.IsCompleted); frame++)
            {
                yield return null;
            }

            Assert.That(wait.IsCanceled, Is.True);
            Assert.That(typedWait.IsCanceled, Is.True);
            Assert.That(handlerWait.IsCanceled, Is.True);
            Assert.That(move.IsCanceled, Is.True);
            Assert.That(handler.OnInvokeAsync().IsCanceled, Is.True);
            Assert.DoesNotThrow(() => unityEvent.Invoke());
        }

        [UnityTest]
        public IEnumerator Enumerator_ConsumesInvokesRaisedFromLaterFrames()
        {
            m_gameObject = new GameObject("unity-event-frames");
            UnityEvent<int> unityEvent = new UnityEvent<int>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<int> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(source.Token)
                    .GetAsyncEnumerator();

                for (int value = 1; value <= 3; value++)
                {
                    OnityTask<bool> move = enumerator.MoveNextAsync();
                    yield return null;

                    Assert.That(move.IsCompleted, Is.False);

                    unityEvent.Invoke(value);

                    Assert.That(move.GetAwaiter().GetResult(), Is.True);
                    Assert.That(enumerator.Current, Is.EqualTo(value));
                }

                OnityTask<bool> last = enumerator.MoveNextAsync();
                source.Cancel();
                yield return null;

                Assert.That(last.IsCanceled, Is.True);
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }
}
