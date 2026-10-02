using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityAsyncTriggerPlayModeTests
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
        public IEnumerator AwakeAsync_ActiveObject_CompletesImmediately()
        {
            m_gameObject = new GameObject("trigger-awake-active");

            Assert.That(m_gameObject.AwakeAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(m_gameObject.transform.AwakeAsync().IsCompletedSuccessfully, Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator AwakeAsync_InactiveObject_CompletesOnActivation()
        {
            m_gameObject = new GameObject("trigger-awake-inactive");
            m_gameObject.SetActive(false);
            OnityTask awake = m_gameObject.AwakeAsync();
            yield return null;

            Assert.That(awake.IsCompleted, Is.False);

            m_gameObject.SetActive(true);

            Assert.That(awake.IsCompletedSuccessfully, Is.True);
            awake.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator AwakeAsync_InactiveObjectDestroyedBeforeActivation_IsCanceled()
        {
            m_gameObject = new GameObject("trigger-awake-destroyed");
            m_gameObject.SetActive(false);
            OnityTask awake = m_gameObject.AwakeAsync();
            OnityAsyncEnableTrigger enableTrigger = m_gameObject.GetAsyncEnableTrigger();
            OnityTask enable = enableTrigger.OnEnableAsync();
            IOnityAsyncEnumerator<Unit> enumerator = enableTrigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();

            Object.Destroy(m_gameObject);
            for (int frame = 0; frame < 3 && !(awake.IsCompleted && enable.IsCompleted && move.IsCompleted); frame++)
            {
                yield return null;
            }

            Assert.That(awake.IsCanceled, Is.True);
            Assert.That(enable.IsCanceled, Is.True);
            Assert.That(move.GetAwaiter().GetResult(), Is.False);
        }

        [UnityTest]
        public IEnumerator StartAsync_CompletesAfterStart_ThenImmediately()
        {
            m_gameObject = new GameObject("trigger-start");
            OnityTask start = m_gameObject.StartAsync();

            Assert.That(start.IsCompleted, Is.False);
            yield return null;

            Assert.That(start.IsCompletedSuccessfully, Is.True);
            start.GetAwaiter().GetResult();
            Assert.That(m_gameObject.transform.StartAsync().IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator EnableDisable_WaitersResumeInLifecycleOrder()
        {
            m_gameObject = new GameObject("trigger-enable-disable");
            OnityAsyncEnableTrigger enableTrigger = m_gameObject.GetAsyncEnableTrigger();
            OnityAsyncDisableTrigger disableTrigger = m_gameObject.GetAsyncDisableTrigger();
            List<string> order = new List<string>();
            Observe(enableTrigger.OnEnableAsync(), "enable-1", order);
            Observe(enableTrigger.OnEnableAsync(), "enable-2", order);
            Observe(disableTrigger.OnDisableAsync(), "disable-1", order);
            Observe(disableTrigger.OnDisableAsync(), "disable-2", order);
            yield return null;

            Assert.That(order, Is.Empty);

            m_gameObject.SetActive(false);
            Assert.That(order, Is.EqualTo(new[] { "disable-1", "disable-2" }));

            m_gameObject.SetActive(true);
            Assert.That(order, Is.EqualTo(new[] { "disable-1", "disable-2", "enable-1", "enable-2" }));

            m_gameObject.SetActive(false);
            m_gameObject.SetActive(true);
            Assert.That(order.Count, Is.EqualTo(4), "one-shot waiters resume once");
        }

        [UnityTest]
        public IEnumerator ReusableHandlers_ResumeOncePerToggle()
        {
            m_gameObject = new GameObject("trigger-reusable");
            IOnityAsyncOnEnableHandler enable = m_gameObject.GetAsyncEnableTrigger().GetOnEnableAsyncHandler();
            IOnityAsyncOnDisableHandler disable = m_gameObject.GetAsyncDisableTrigger().GetOnDisableAsyncHandler();
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                OnityTask disableWait = disable.OnDisableAsync();
                OnityTask enableWait = enable.OnEnableAsync();

                m_gameObject.SetActive(false);
                Assert.That(disableWait.IsCompletedSuccessfully, Is.True);
                Assert.That(enableWait.IsCompleted, Is.False);

                m_gameObject.SetActive(true);
                Assert.That(enableWait.IsCompletedSuccessfully, Is.True);
                disableWait.GetAwaiter().GetResult();
                enableWait.GetAwaiter().GetResult();
            }

            ((IDisposable)enable).Dispose();
            ((IDisposable)disable).Dispose();
        }

        [UnityTest]
        public IEnumerator Destroy_DisableResumesThenWaitersCanceledAndEnumeratorsEnd()
        {
            m_gameObject = new GameObject("trigger-destroy");
            OnityAsyncEnableTrigger enableTrigger = m_gameObject.GetAsyncEnableTrigger();
            OnityAsyncDisableTrigger disableTrigger = m_gameObject.GetAsyncDisableTrigger();
            using var source = new CancellationTokenSource();
            OnityTask enable = enableTrigger.OnEnableAsync(source.Token);
            IOnityAsyncOnEnableHandler handler = enableTrigger.GetOnEnableAsyncHandler();
            OnityTask handlerWait = handler.OnEnableAsync();
            OnityTask disable = disableTrigger.OnDisableAsync();
            IOnityAsyncEnumerator<Unit> disableEvents = disableTrigger.GetAsyncEnumerator();
            OnityTask<bool> disableMove = disableEvents.MoveNextAsync();
            IOnityAsyncEnumerator<Unit> enableEvents = enableTrigger.GetAsyncEnumerator();
            OnityTask<bool> enableMove = enableEvents.MoveNextAsync();
            OnityTask destroyed = m_gameObject.OnDestroyAsync();
            CancellationToken token = m_gameObject.GetCancellationTokenOnDestroy();
            yield return null;

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(disable.IsCompletedSuccessfully, Is.True, "OnDisable runs before OnDestroy");
            Assert.That(disableMove.GetAwaiter().GetResult(), Is.True);
            Assert.That(disableEvents.MoveNextAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(enable.IsCanceled, Is.True);
            Assert.That(handlerWait.IsCanceled, Is.True);
            Assert.That(handler.OnEnableAsync().IsCanceled, Is.True);
            Assert.That(enableMove.GetAwaiter().GetResult(), Is.False);
            Assert.That(destroyed.IsCompletedSuccessfully, Is.True);
            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyTrigger_SharedByTokenAndOnDestroyAsync()
        {
            m_gameObject = new GameObject("trigger-destroy-shared");
            CancellationToken token = m_gameObject.transform.GetCancellationTokenOnDestroy();
            OnityTask fromComponent = m_gameObject.transform.OnDestroyAsync();
            OnityTask fromObject = m_gameObject.OnDestroyAsync();
            OnityAsyncDestroyTrigger trigger = m_gameObject.GetAsyncDestroyTrigger();

            Assert.That(m_gameObject.GetComponents<OnityAsyncDestroyTrigger>().Length, Is.EqualTo(1));
            Assert.That(trigger.CancellationToken, Is.EqualTo(token));

            Object.Destroy(m_gameObject);
            yield return null;

            Assert.That(fromComponent.IsCompletedSuccessfully, Is.True);
            Assert.That(fromObject.IsCompletedSuccessfully, Is.True);
            Assert.That(token.IsCancellationRequested, Is.True);
            fromObject.GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator OnDestroyAsync_InactiveObjectNeverAwakened_CompletesViaMonitor()
        {
            m_gameObject = new GameObject("trigger-destroy-inactive");
            m_gameObject.SetActive(false);
            OnityTask destroyed = m_gameObject.OnDestroyAsync();

            Object.Destroy(m_gameObject);
            for (int frame = 0; frame < 3 && !destroyed.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(destroyed.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator Enumerator_YieldsEachToggle_UntilCanceled()
        {
            m_gameObject = new GameObject("trigger-enumerator");
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            using var source = new CancellationTokenSource();
            IOnityAsyncEnumerator<Unit> events = trigger.GetAsyncEnumerator(source.Token);
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                OnityTask<bool> move = events.MoveNextAsync();
                m_gameObject.SetActive(false);
                Assert.That(move.IsCompleted, Is.False);
                m_gameObject.SetActive(true);
                Assert.That(move.GetAwaiter().GetResult(), Is.True);
            }

            OnityTask<bool> canceled = events.MoveNextAsync();
            source.Cancel();
            Assert.That(canceled.IsCanceled, Is.True);
            events.DisposeAsync().GetAwaiter().GetResult();
        }

        private static void Observe(OnityTask task, string name, List<string> order)
        {
            OnityTaskAwaiter awaiter = task.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                order.Add(name);
            });
        }
    }
}
