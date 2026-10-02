using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Edit mode does not send MonoBehaviour messages to these triggers, so the tests raise them
    /// deterministically by invoking the private message methods.
    /// </summary>
    public sealed class OnityAsyncTriggerEditModeTests
    {
        private sealed class IntTrigger : OnityAsyncTriggerBase<int>
        {
            public void Raise(int value)
            {
                RaiseEvent(value);
            }
        }

        private GameObject m_gameObject;

        [SetUp]
        public void SetUp()
        {
            m_gameObject = new GameObject("async-trigger-edit");
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

        [Test]
        public void GetAsyncTriggers_ReturnOneComponentPerTypeForGameObjectAndComponent()
        {
            Transform transform = m_gameObject.transform;

            Assert.That(transform.GetAsyncAwakeTrigger(), Is.SameAs(m_gameObject.GetAsyncAwakeTrigger()));
            Assert.That(transform.GetAsyncStartTrigger(), Is.SameAs(m_gameObject.GetAsyncStartTrigger()));
            Assert.That(transform.GetAsyncEnableTrigger(), Is.SameAs(m_gameObject.GetAsyncEnableTrigger()));
            Assert.That(transform.GetAsyncDisableTrigger(), Is.SameAs(m_gameObject.GetAsyncDisableTrigger()));
            Assert.That(transform.GetAsyncDestroyTrigger(), Is.SameAs(m_gameObject.GetAsyncDestroyTrigger()));
            Assert.That(m_gameObject.GetComponents<Component>().Length, Is.EqualTo(6));
        }

        [Test]
        public void Entry_Points_NullOwner_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).GetAsyncEnableTrigger());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).GetAsyncDisableTrigger());
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).GetAsyncDestroyTrigger());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).GetAsyncDestroyTrigger());
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).OnDestroyAsync());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).OnDestroyAsync());
            Assert.Throws<ArgumentNullException>(() => ((GameObject)null).AwakeAsync());
            Assert.Throws<ArgumentNullException>(() => ((Component)null).StartAsync());
        }

        [Test]
        public void OnEnableAsync_ConcurrentWaiters_EachResumedOncePerEvent()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            IOnityAsyncOnEnableHandler handler = trigger.GetOnEnableAsyncHandler();
            int[] resumed = new int[4];
            OnityTask[] waits =
            {
                trigger.OnEnableAsync(),
                trigger.OnEnableAsync(),
                trigger.OnEnableAsync(CancellationToken.None),
                handler.OnEnableAsync()
            };

            for (int i = 0; i < waits.Length; i++)
            {
                int index = i;
                OnityTaskAwaiter awaiter = waits[i].GetAwaiter();
                awaiter.OnCompleted(() =>
                {
                    awaiter.GetResult();
                    resumed[index]++;
                });
            }

            Assert.That(resumed, Is.EqualTo(new[] { 0, 0, 0, 0 }));

            RaiseMessage(trigger, "OnEnable");
            Assert.That(resumed, Is.EqualTo(new[] { 1, 1, 1, 1 }));

            RaiseMessage(trigger, "OnEnable");
            Assert.That(resumed, Is.EqualTo(new[] { 1, 1, 1, 1 }));
            ((IDisposable)handler).Dispose();
        }

        [Test]
        public void ReusableHandler_WaitsEveryEvent_AndSkipsEventsWithoutPendingWait()
        {
            OnityAsyncDisableTrigger trigger = m_gameObject.GetAsyncDisableTrigger();
            IOnityAsyncOnDisableHandler handler = trigger.GetOnDisableAsyncHandler();

            for (int i = 0; i < 3; i++)
            {
                OnityTask wait = handler.OnDisableAsync();
                Assert.That(wait.IsCompleted, Is.False);

                RaiseMessage(trigger, "OnDisable");

                Assert.That(wait.IsCompletedSuccessfully, Is.True);
                wait.GetAwaiter().GetResult();
            }

            RaiseMessage(trigger, "OnDisable");
            OnityTask next = handler.OnDisableAsync();
            Assert.That(next.IsCompleted, Is.False, "an event raised before the wait must be skipped");

            ((IDisposable)handler).Dispose();
            Assert.That(next.IsCanceled, Is.True);
        }

        [Test]
        public void OneShotWait_ContinuationThatWaitsAgain_IsNotResumedBySameEvent()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            int resumed = 0;
            OnityTask second = default;
            OnityTaskAwaiter first = trigger.OnEnableAsync().GetAwaiter();
            first.OnCompleted(() =>
            {
                first.GetResult();
                resumed++;
                second = trigger.OnEnableAsync();
            });

            RaiseMessage(trigger, "OnEnable");
            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(second.IsCompleted, Is.False);

            RaiseMessage(trigger, "OnEnable");
            Assert.That(second.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void ContinuationDisposingLaterHandler_SkipsItsResult()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            OnityTaskAwaiter first = trigger.OnEnableAsync().GetAwaiter();
            IOnityAsyncOnEnableHandler later = null;
            first.OnCompleted(() =>
            {
                first.GetResult();
                ((IDisposable)later).Dispose();
            });
            later = trigger.GetOnEnableAsyncHandler();
            OnityTask laterWait = later.OnEnableAsync();

            // The first waiter was registered before the later handler, so it runs first.
            RaiseMessage(trigger, "OnEnable");

            Assert.That(laterWait.IsCanceled, Is.True);
        }

        [Test]
        public void RaiseFromOwnContinuation_ThrowsInvalidOperation()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            Exception reentry = null;
            OnityTaskAwaiter awaiter = trigger.OnEnableAsync().GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                try
                {
                    RaiseMessage(trigger, "OnEnable");
                }
                catch (Exception exception)
                {
                    reentry = exception;
                }
            });

            RaiseMessage(trigger, "OnEnable");

            Assert.That(reentry, Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Cancellation_BeforeWaiting_CompletesCanceled()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            CancellationToken canceled = new CancellationToken(true);

            OnityTask oneShot = trigger.OnEnableAsync(canceled);
            OnityTask reusable = trigger.GetOnEnableAsyncHandler(canceled).OnEnableAsync();

            Assert.That(oneShot.IsCanceled, Is.True);
            Assert.That(reusable.IsCanceled, Is.True);
            OperationCanceledException exception =
                Assert.Throws<OperationCanceledException>(() => oneShot.GetAwaiter().GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(canceled));
        }

        [Test]
        public void Cancellation_WhileWaiting_CancelsAndUnregisters()
        {
            OnityAsyncDisableTrigger trigger = m_gameObject.GetAsyncDisableTrigger();
            using var oneShotSource = new CancellationTokenSource();
            using var handlerSource = new CancellationTokenSource();
            OnityTask oneShot = trigger.OnDisableAsync(oneShotSource.Token);
            IOnityAsyncOnDisableHandler handler = trigger.GetOnDisableAsyncHandler(handlerSource.Token);
            OnityTask handlerWait = handler.OnDisableAsync();

            oneShotSource.Cancel();
            Assert.That(oneShot.IsCanceled, Is.True);
            Assert.That(handlerWait.IsCompleted, Is.False);
            OperationCanceledException exception =
                Assert.Throws<OperationCanceledException>(() => oneShot.GetAwaiter().GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(oneShotSource.Token));

            handlerSource.Cancel();
            Assert.That(handlerWait.IsCanceled, Is.True);
            Assert.That(handler.OnDisableAsync().IsCanceled, Is.True);

            Assert.DoesNotThrow(() => RaiseMessage(trigger, "OnDisable"));
        }

        [Test]
        public void Destroy_CancelsPendingWaits_AndLaterWaits()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            IOnityAsyncOnEnableHandler handler = trigger.GetOnEnableAsyncHandler();
            OnityTask oneShot = trigger.OnEnableAsync();
            OnityTask handlerWait = handler.OnEnableAsync();
            OnityTask idleHandlerWait;

            RaiseMessage(trigger, "OnDestroy");

            Assert.That(oneShot.IsCanceled, Is.True);
            Assert.That(handlerWait.IsCanceled, Is.True);
            idleHandlerWait = handler.OnEnableAsync();
            Assert.That(idleHandlerWait.IsCanceled, Is.True);
            Assert.That(trigger.OnEnableAsync().IsCanceled, Is.True);
            Assert.Throws<OperationCanceledException>(() => oneShot.GetAwaiter().GetResult());
        }

        [Test]
        public void DestroyFromContinuation_StopsDeliveryAndCancelsRemainingWaiters()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            OnityTaskAwaiter first = trigger.OnEnableAsync().GetAwaiter();
            first.OnCompleted(() =>
            {
                first.GetResult();
                RaiseMessage(trigger, "OnDestroy");
            });
            OnityTask second = trigger.OnEnableAsync();
            IOnityAsyncEnumerator<Unit> enumerator = trigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();

            Assert.DoesNotThrow(() => RaiseMessage(trigger, "OnEnable"));

            Assert.That(second.IsCanceled, Is.True);
            Assert.That(move.IsCompletedSuccessfully, Is.True);
            Assert.That(move.GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void Enumerator_YieldsEachEvent_ToEveryEnumerator_AndEndsOnDestroy()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            IOnityAsyncEnumerator<int> first = trigger.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> second = trigger.GetAsyncEnumerator();

            for (int value = 1; value <= 3; value++)
            {
                OnityTask<bool> firstMove = first.MoveNextAsync();
                OnityTask<bool> secondMove = second.MoveNextAsync();
                Assert.That(firstMove.IsCompleted, Is.False);

                trigger.Raise(value);

                Assert.That(firstMove.GetAwaiter().GetResult(), Is.True);
                Assert.That(secondMove.GetAwaiter().GetResult(), Is.True);
                Assert.That(first.Current, Is.EqualTo(value));
                Assert.That(second.Current, Is.EqualTo(value));
            }

            OnityTask<bool> pending = first.MoveNextAsync();
            RaiseMessage(trigger, "OnDestroy");

            Assert.That(pending.GetAwaiter().GetResult(), Is.False);
            Assert.Throws<InvalidOperationException>(() => _ = first.Current);
            Assert.That(second.MoveNextAsync().GetAwaiter().GetResult(), Is.False,
                "an enumerator idle at destroy ends on its next move");
            Assert.That(trigger.GetAsyncEnumerator().MoveNextAsync().GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void Enumerator_EventsBetweenMoves_AreSkipped()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            IOnityAsyncEnumerator<int> enumerator = trigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();
            trigger.Raise(1);
            Assert.That(move.GetAwaiter().GetResult(), Is.True);

            trigger.Raise(2);
            move = enumerator.MoveNextAsync();
            trigger.Raise(3);

            Assert.That(move.GetAwaiter().GetResult(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(3));
        }

        [Test]
        public void Enumerator_SecondOutstandingMove_Throws()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            IOnityAsyncEnumerator<int> enumerator = trigger.GetAsyncEnumerator();
            enumerator.MoveNextAsync();

            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNextAsync());
        }

        [Test]
        public void Enumerator_Cancellation_BeforeAndWhileWaiting()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            CancellationToken canceled = new CancellationToken(true);
            OnityTask<bool> preCanceled = trigger.GetAsyncEnumerator(canceled).MoveNextAsync();
            Assert.That(preCanceled.IsCanceled, Is.True);

            using var source = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = trigger.GetAsyncEnumerator(source.Token);
            OnityTask<bool> move = enumerator.MoveNextAsync();
            source.Cancel();

            Assert.That(move.IsCanceled, Is.True);
            OperationCanceledException exception =
                Assert.Throws<OperationCanceledException>(() => move.GetAwaiter().GetResult());
            Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
            Assert.That(enumerator.MoveNextAsync().IsCanceled, Is.True);
            Assert.DoesNotThrow(() => trigger.Raise(1));
        }

        [Test]
        public void Enumerator_DisposeWhileWaiting_EndsMoveFalse()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            IOnityAsyncEnumerator<int> enumerator = trigger.GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();

            OnityTask dispose = enumerator.DisposeAsync();

            Assert.That(dispose.IsCompletedSuccessfully, Is.True);
            Assert.That(move.GetAwaiter().GetResult(), Is.False);
            Assert.That(enumerator.MoveNextAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(enumerator.DisposeAsync().IsCompletedSuccessfully, Is.True);
            Assert.DoesNotThrow(() => trigger.Raise(1));
        }

        [Test]
        public void Enumerator_SteadyStateMoves_DoNotAllocate()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            IOnityAsyncEnumerator<int> enumerator = trigger.GetAsyncEnumerator();
            for (int i = 0; i < 4; i++)
            {
                OnityTask<bool> warm = enumerator.MoveNextAsync();
                trigger.Raise(i);
                warm.GetAwaiter().GetResult();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                OnityTask<bool> move = enumerator.MoveNextAsync();
                trigger.Raise(i);
                move.GetAwaiter().GetResult();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ReusableHandler_SteadyStateWaits_DoNotAllocate()
        {
            OnityAsyncEnableTrigger trigger = m_gameObject.GetAsyncEnableTrigger();
            Action raise = (Action)Delegate.CreateDelegate(typeof(Action), trigger, FindMessage(trigger, "OnEnable"));
            IOnityAsyncOnEnableHandler handler = trigger.GetOnEnableAsyncHandler();
            for (int i = 0; i < 4; i++)
            {
                OnityTask warm = handler.OnEnableAsync();
                raise();
                warm.GetAwaiter().GetResult();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                OnityTask wait = handler.OnEnableAsync();
                raise();
                wait.GetAwaiter().GetResult();
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
            ((IDisposable)handler).Dispose();
        }

        [Test]
        public async Task AsAsyncEnumerable_BridgesTriggerEvents()
        {
            IntTrigger trigger = m_gameObject.AddComponent<IntTrigger>();
            var enumerator = trigger.AsAsyncEnumerable().GetAsyncEnumerator();
            var move = enumerator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);

            trigger.Raise(7);

            Assert.That(await move, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(7));

            move = enumerator.MoveNextAsync();
            RaiseMessage(trigger, "OnDestroy");
            Assert.That(await move, Is.False);
            await enumerator.DisposeAsync();
        }

        [Test]
        public void AwakeAsync_AlreadyAwake_CompletesImmediately_ElseCompletesOnAwake()
        {
            OnityAsyncAwakeTrigger trigger = m_gameObject.GetAsyncAwakeTrigger();
            OnityTask pending = trigger.AwakeAsync();
            Assert.That(pending.IsCompleted, Is.False);

            RaiseMessage(trigger, "Awake");

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(trigger.AwakeAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(m_gameObject.AwakeAsync().IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void StartAsync_AlreadyStarted_CompletesImmediately_ElseCompletesOnStart()
        {
            OnityAsyncStartTrigger trigger = m_gameObject.GetAsyncStartTrigger();
            OnityTask pending = trigger.StartAsync();
            Assert.That(pending.IsCompleted, Is.False);

            RaiseMessage(trigger, "Start");

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(trigger.StartAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(m_gameObject.transform.StartAsync().IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void StartAsync_DestroyedBeforeStart_IsCanceled()
        {
            OnityAsyncStartTrigger trigger = m_gameObject.GetAsyncStartTrigger();
            OnityTask pending = trigger.StartAsync();

            RaiseMessage(trigger, "OnDestroy");

            Assert.That(pending.IsCanceled, Is.True);
        }

        [Test]
        public void DestroyTrigger_SharesComponentWithDestroyToken_AndOnDestroyAsyncCompletes()
        {
            CancellationToken token = m_gameObject.GetCancellationTokenOnDestroy();
            OnityAsyncDestroyTrigger trigger = m_gameObject.GetAsyncDestroyTrigger();
            OnityTask fromTrigger = trigger.OnDestroyAsync();
            OnityTask fromComponent = m_gameObject.transform.OnDestroyAsync();

            Assert.That(trigger.CancellationToken, Is.EqualTo(token));
            Assert.That(m_gameObject.GetComponents<Component>().Length, Is.EqualTo(2));
            Assert.That(fromTrigger.IsCompleted, Is.False);

            RaiseMessage(trigger, "OnDestroy");

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(fromTrigger.IsCompletedSuccessfully, Is.True);
            Assert.That(fromComponent.IsCompletedSuccessfully, Is.True);
            Assert.That(trigger.OnDestroyAsync().IsCompletedSuccessfully, Is.True);
            fromTrigger.GetAwaiter().GetResult();
        }

        [Test]
        public void OnDestroyAsync_DestroyedOwner_CompletesImmediately()
        {
            Transform transform = m_gameObject.transform;
            GameObject owner = m_gameObject;
            Object.DestroyImmediate(m_gameObject);

            Assert.That(owner.OnDestroyAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(transform.OnDestroyAsync().IsCompletedSuccessfully, Is.True);
        }

        private static void RaiseMessage(Component trigger, string message)
        {
            try
            {
                FindMessage(trigger, message).Invoke(trigger, null);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        private static MethodInfo FindMessage(Component trigger, string message)
        {
            for (Type type = trigger.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo method = type.GetMethod(
                    message,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method != null)
                {
                    return method;
                }
            }

            throw new MissingMethodException(trigger.GetType().Name, message);
        }
    }
}
