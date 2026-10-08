using System;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Onity.Tests.UGUI.PlayMode
{
    /// <summary>
    /// Raises each EventSystems trigger by calling the explicit event interface it implements and checks every
    /// wait shape: one-shot, one-shot with a token, reusable handler, handler with a token and the enumerable.
    /// One generated row per trigger lives in <c>OnityAsyncEventSystemTriggerPlayModeTests.Generated.cs</c>.
    /// </summary>
    public sealed partial class OnityAsyncEventSystemTriggerPlayModeTests
    {
        private GameObject m_gameObject;

        [SetUp]
        public void SetUp()
        {
            m_gameObject = new GameObject("async-event-system-trigger");
        }

        [TearDown]
        public void TearDown()
        {
            if (m_gameObject != null)
            {
                Object.DestroyImmediate(m_gameObject);
            }

            m_gameObject = null;
        }

        private void CheckEventMessage<TTrigger, TPayload>(
            TPayload payload,
            Action<TTrigger> raise,
            Func<GameObject, TTrigger> getTrigger,
            Func<Component, TTrigger> getTriggerFromComponent,
            Func<TTrigger, OnityTask<TPayload>> oneShot,
            Func<TTrigger, CancellationToken, OnityTask<TPayload>> oneShotWithToken,
            Func<TTrigger, object> getHandler,
            Func<TTrigger, CancellationToken, object> getHandlerWithToken,
            Func<object, OnityTask<TPayload>> wait)
            where TTrigger : OnityAsyncTriggerBase<TPayload>
        {
            TTrigger trigger = getTrigger(m_gameObject);
            Assert.That(getTrigger(m_gameObject), Is.SameAs(trigger), "GetOrAdd reuses the component");
            Assert.That(getTriggerFromComponent(m_gameObject.transform), Is.SameAs(trigger));

            OnityTask<TPayload> oneShotWait = oneShot(trigger);
            Assert.That(oneShotWait.IsCompleted, Is.False);
            raise(trigger);
            Assert.That(oneShotWait.IsCompletedSuccessfully, Is.True);
            Assert.That(oneShotWait.GetAwaiter().GetResult(), Is.SameAs(payload));
            Assert.DoesNotThrow(() => raise(trigger), "the one-shot wait unregistered itself");

            using (CancellationTokenSource preCanceled = new CancellationTokenSource())
            {
                preCanceled.Cancel();
                Assert.That(oneShotWithToken(trigger, preCanceled.Token).IsCanceled, Is.True);
            }

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask<TPayload> bound = oneShotWithToken(trigger, source.Token);
                Assert.That(bound.IsCompleted, Is.False);
                source.Cancel();
                Assert.That(bound.IsCanceled, Is.True);
                Assert.DoesNotThrow(() => raise(trigger));
            }

            object handler = getHandler(trigger);
            for (int i = 0; i < 2; i++)
            {
                OnityTask<TPayload> handlerWait = wait(handler);
                Assert.That(handlerWait.IsCompleted, Is.False);
                raise(trigger);
                Assert.That(handlerWait.IsCompletedSuccessfully, Is.True);
                Assert.That(handlerWait.GetAwaiter().GetResult(), Is.SameAs(payload));
            }

            ((IDisposable)handler).Dispose();

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                object bound = getHandlerWithToken(trigger, source.Token);
                OnityTask<TPayload> boundWait = wait(bound);
                Assert.That(boundWait.IsCompleted, Is.False);
                source.Cancel();
                Assert.That(boundWait.IsCanceled, Is.True);
                ((IDisposable)bound).Dispose();
            }

            IOnityAsyncEnumerator<TPayload> enumerator = trigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            raise(trigger);
            Assert.That(move.GetAwaiter().GetResult(), Is.True);
            Assert.That(enumerator.Current, Is.SameAs(payload));
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }
    }
}
