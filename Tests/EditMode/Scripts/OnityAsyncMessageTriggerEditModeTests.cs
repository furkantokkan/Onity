using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Raises each generated message trigger by invoking its private Unity message (Edit mode sends none)
    /// and checks every wait shape: one-shot, one-shot with a token, reusable handler, handler with a
    /// token, and the enumerable. One generated row per trigger lives in
    /// <c>OnityAsyncMessageTriggerEditModeTests.Generated.cs</c>. The tests assume the physics, physics2d
    /// and particle system modules, as the Onity test project has them installed.
    /// </summary>
    public sealed partial class OnityAsyncMessageTriggerEditModeTests
    {
        private readonly List<GameObject> m_payloadObjects = new List<GameObject>();
        private GameObject m_gameObject;

        [SetUp]
        public void SetUp()
        {
            m_gameObject = new GameObject("async-message-trigger-edit");
        }

        [TearDown]
        public void TearDown()
        {
            if (m_gameObject != null)
            {
                Object.DestroyImmediate(m_gameObject);
            }

            m_gameObject = null;

            for (int i = 0; i < m_payloadObjects.Count; i++)
            {
                if (m_payloadObjects[i] != null)
                {
                    Object.DestroyImmediate(m_payloadObjects[i]);
                }
            }

            m_payloadObjects.Clear();
        }

        private GameObject NewObject()
        {
            GameObject owner = new GameObject("async-message-trigger-payload");
            m_payloadObjects.Add(owner);
            return owner;
        }

        private T NewComponent<T>()
            where T : Component
        {
            return NewObject().AddComponent<T>();
        }

        private void CheckUnitMessage<TTrigger>(
            string message,
            Func<GameObject, TTrigger> getTrigger,
            Func<Component, TTrigger> getTriggerFromComponent,
            Func<TTrigger, OnityTask> oneShot,
            Func<TTrigger, CancellationToken, OnityTask> oneShotWithToken,
            Func<TTrigger, object> getHandler,
            Func<TTrigger, CancellationToken, object> getHandlerWithToken,
            Func<object, OnityTask> wait)
            where TTrigger : OnityAsyncTriggerBase<Unit>
        {
            TTrigger trigger = getTrigger(m_gameObject);
            Assert.That(getTrigger(m_gameObject), Is.SameAs(trigger), "GetOrAdd reuses the component");
            Assert.That(getTriggerFromComponent(m_gameObject.transform), Is.SameAs(trigger));

            OnityTask oneShotWait = oneShot(trigger);
            Assert.That(oneShotWait.IsCompleted, Is.False);
            RaiseMessage(trigger, message);
            Assert.That(oneShotWait.IsCompletedSuccessfully, Is.True);
            oneShotWait.GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => RaiseMessage(trigger, message), "the one-shot wait unregistered itself");

            using (CancellationTokenSource preCanceled = new CancellationTokenSource())
            {
                preCanceled.Cancel();
                Assert.That(oneShotWithToken(trigger, preCanceled.Token).IsCanceled, Is.True);
            }

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask bound = oneShotWithToken(trigger, source.Token);
                Assert.That(bound.IsCompleted, Is.False);
                source.Cancel();
                Assert.That(bound.IsCanceled, Is.True);
                Assert.DoesNotThrow(() => RaiseMessage(trigger, message));
            }

            object handler = getHandler(trigger);
            for (int i = 0; i < 2; i++)
            {
                OnityTask handlerWait = wait(handler);
                Assert.That(handlerWait.IsCompleted, Is.False);
                RaiseMessage(trigger, message);
                Assert.That(handlerWait.IsCompletedSuccessfully, Is.True);
                handlerWait.GetAwaiter().GetResult();
            }

            ((IDisposable)handler).Dispose();

            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                object bound = getHandlerWithToken(trigger, source.Token);
                OnityTask boundWait = wait(bound);
                Assert.That(boundWait.IsCompleted, Is.False);
                source.Cancel();
                Assert.That(boundWait.IsCanceled, Is.True);
                ((IDisposable)bound).Dispose();
            }

            IOnityAsyncEnumerator<Unit> enumerator = trigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            RaiseMessage(trigger, message);
            Assert.That(move.GetAwaiter().GetResult(), Is.True);
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        private void CheckValueMessage<TTrigger, TPayload>(
            string message,
            TPayload payload,
            object[] arguments,
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
            RaiseMessage(trigger, message, arguments);
            Assert.That(oneShotWait.IsCompletedSuccessfully, Is.True);
            Assert.That(oneShotWait.GetAwaiter().GetResult(), Is.EqualTo(payload));
            Assert.DoesNotThrow(() => RaiseMessage(trigger, message, arguments), "the one-shot wait unregistered itself");

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
                Assert.DoesNotThrow(() => RaiseMessage(trigger, message, arguments));
            }

            object handler = getHandler(trigger);
            for (int i = 0; i < 2; i++)
            {
                OnityTask<TPayload> handlerWait = wait(handler);
                Assert.That(handlerWait.IsCompleted, Is.False);
                RaiseMessage(trigger, message, arguments);
                Assert.That(handlerWait.IsCompletedSuccessfully, Is.True);
                Assert.That(handlerWait.GetAwaiter().GetResult(), Is.EqualTo(payload));
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
            RaiseMessage(trigger, message, arguments);
            Assert.That(move.GetAwaiter().GetResult(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(payload));
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        private static void RaiseMessage(Component trigger, string message, params object[] arguments)
        {
            MethodInfo method = trigger.GetType().GetMethod(
                message,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (method == null)
            {
                throw new MissingMethodException(trigger.GetType().Name, message);
            }

            try
            {
                method.Invoke(trigger, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }
    }
}
