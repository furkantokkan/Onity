using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine.Events;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the UnityEvent waits, handlers and async sequences. Listener lifetimes are checked through
    /// the runtime listener count of <c>UnityEventBase</c>, which is reached by reflection; a Unity version
    /// that does not expose it reports those checks as inconclusive.
    /// </summary>
    public sealed class OnityUnityEventAsyncEditModeTests
    {
        private static int RuntimeListenerCount(UnityEventBase unityEvent)
        {
            FieldInfo callsField = typeof(UnityEventBase).GetField(
                "m_Calls", BindingFlags.Instance | BindingFlags.NonPublic);
            object calls = callsField?.GetValue(unityEvent);
            PropertyInfo count = calls?.GetType().GetProperty(
                "Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (count == null)
            {
                Assert.Inconclusive("The UnityEvent listener count is not reachable on this Unity version.");
            }

            return (int)count.GetValue(calls);
        }

        [Test]
        public void OnInvokeAsync_CompletesAtTheNextInvoke_AndStopsListening()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask wait = unityEvent.OnInvokeAsync(source.Token);
                Assert.That(wait.IsCompleted, Is.False);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(1));

                unityEvent.Invoke();

                Assert.That(wait.IsCompletedSuccessfully, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
                wait.GetAwaiter().GetResult();
            }
        }

        [Test]
        public void OnInvokeAsync_PreCanceledToken_IsCanceledWithoutListening()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();

                OnityTask wait = unityEvent.OnInvokeAsync(source.Token);

                Assert.That(wait.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
            }
        }

        [Test]
        public void OnInvokeAsync_CancelWhileWaiting_CancelsAndStopsListening()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask wait = unityEvent.OnInvokeAsync(source.Token);

                source.Cancel();

                Assert.That(wait.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
                Assert.DoesNotThrow(() => unityEvent.Invoke());
            }
        }

        [Test]
        public void OnInvokeAsync_ContinuationStartingAnotherOneShot_IsNotResumedBySameInvoke()
        {
            UnityEvent unityEvent = new UnityEvent();
            OnityTask second = default;
            OnityTask first = unityEvent.OnInvokeAsync(CancellationToken.None);
            OnityTaskAwaiter awaiter = first.GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                awaiter.GetResult();
                second = unityEvent.OnInvokeAsync(CancellationToken.None);
            });

            unityEvent.Invoke();
            Assert.That(second.IsCompleted, Is.False);

            unityEvent.Invoke();
            Assert.That(second.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void Handler_WaitsEachInvoke_SkipsInvokesWithoutWait_AndStopsWhenDisposed()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityAsyncUnityEventHandler handler = unityEvent.GetAsyncEventHandler(source.Token);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(1));

                unityEvent.Invoke();

                for (int i = 0; i < 3; i++)
                {
                    OnityTask wait = handler.OnInvokeAsync();
                    Assert.That(wait.IsCompleted, Is.False);
                    unityEvent.Invoke();
                    Assert.That(wait.IsCompletedSuccessfully, Is.True);
                    wait.GetAwaiter().GetResult();
                }

                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(1), "a reusable handler stays registered");

                handler.Dispose();
                handler.Dispose();

                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
            }
        }

        [Test]
        public void Handler_DisposeCancelsThePendingWait_AndLaterWaitsAreCanceled()
        {
            UnityEvent unityEvent = new UnityEvent();
            OnityAsyncUnityEventHandler handler = unityEvent.GetAsyncEventHandler(CancellationToken.None);
            OnityTask pending = handler.OnInvokeAsync();

            handler.Dispose();

            Assert.That(pending.IsCanceled, Is.True);
            Assert.That(handler.OnInvokeAsync().IsCanceled, Is.True);
        }

        [Test]
        public void Handler_NewWaitAbandonsThePendingOne()
        {
            UnityEvent unityEvent = new UnityEvent();
            OnityAsyncUnityEventHandler handler = unityEvent.GetAsyncEventHandler(CancellationToken.None);
            OnityTask abandoned = handler.OnInvokeAsync();
            OnityTask current = handler.OnInvokeAsync();

            unityEvent.Invoke();

            Assert.That(current.IsCompletedSuccessfully, Is.True);
            Assert.That(abandoned.IsCompleted, Is.False);
            handler.Dispose();
        }

        [Test]
        public void Handler_TokenCancelStopsListening_AndCancelsThePendingWait()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityAsyncUnityEventHandler handler = unityEvent.GetAsyncEventHandler(source.Token);
                OnityTask pending = handler.OnInvokeAsync();

                source.Cancel();

                Assert.That(pending.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
                Assert.That(handler.OnInvokeAsync().IsCanceled, Is.True);
            }
        }

        [Test]
        public void TypedOneShot_CompletesWithTheArgument()
        {
            UnityEvent<int> unityEvent = new UnityEvent<int>();

            OnityTask<int> wait = unityEvent.OnInvokeAsync(CancellationToken.None);
            unityEvent.Invoke(42);

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(42));
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
        }

        [Test]
        public void TypedHandler_CompletesWithTheArgument_ThroughEveryHandlerInterface()
        {
            UnityEvent<string> unityEvent = new UnityEvent<string>();
            OnityAsyncUnityEventHandler<string> handler = unityEvent.GetAsyncEventHandler(CancellationToken.None);
            Func<OnityTask<string>>[] waits =
            {
                () => handler.OnInvokeAsync(),
                () => ((IOnityAsyncValueChangedEventHandler<string>)handler).OnValueChangedAsync(),
                () => ((IOnityAsyncEndEditEventHandler<string>)handler).OnEndEditAsync(),
                () => ((IOnityAsyncEndTextSelectionEventHandler<string>)handler).OnEndTextSelectionAsync(),
                () => ((IOnityAsyncTextSelectionEventHandler<string>)handler).OnTextSelectionAsync(),
                () => ((IOnityAsyncDeselectEventHandler<string>)handler).OnDeselectAsync(),
                () => ((IOnityAsyncSelectEventHandler<string>)handler).OnSelectAsync(),
                () => ((IOnityAsyncSubmitEventHandler<string>)handler).OnSubmitAsync()
            };

            for (int i = 0; i < waits.Length; i++)
            {
                string value = "value-" + i;
                OnityTask<string> wait = waits[i]();
                Assert.That(wait.IsCompleted, Is.False);

                unityEvent.Invoke(value);

                Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo(value));
            }

            ((IDisposable)handler).Dispose();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
        }

        [Test]
        public void ClickHandlerInterface_WaitsLikeOnInvokeAsync()
        {
            UnityEvent unityEvent = new UnityEvent();
            IOnityAsyncClickEventHandler handler = unityEvent.GetAsyncEventHandler(CancellationToken.None);

            OnityTask wait = handler.OnClickAsync();
            unityEvent.Invoke();

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            handler.Dispose();
        }

        [Test]
        public void Enumerator_ListensFromTheFirstMove_YieldsPerMove_AndSkipsInvokesBetweenMoves()
        {
            UnityEvent unityEvent = new UnityEvent();
            IOnityAsyncEnumerable<Unit> sequence = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None);
            IOnityAsyncEnumerator<Unit> enumerator = sequence.GetAsyncEnumerator();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0), "nothing is registered before the first move");

            OnityTask<bool> move = enumerator.MoveNextAsync();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(1));
            Assert.That(move.IsCompleted, Is.False);

            unityEvent.Invoke();
            Assert.That(move.GetAwaiter().GetResult(), Is.True);

            unityEvent.Invoke();
            move = enumerator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False, "the invoke between moves was skipped");

            unityEvent.Invoke();
            Assert.That(move.GetAwaiter().GetResult(), Is.True);

            enumerator.DisposeAsync().GetAwaiter().GetResult();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
        }

        [Test]
        public void TypedEnumerator_YieldsTheArgumentOfEachInvoke()
        {
            UnityEvent<int> unityEvent = new UnityEvent<int>();
            IOnityAsyncEnumerator<int> enumerator = unityEvent
                .OnInvokeAsAsyncEnumerable(CancellationToken.None)
                .GetAsyncEnumerator();

            for (int i = 1; i <= 3; i++)
            {
                OnityTask<bool> move = enumerator.MoveNextAsync();
                unityEvent.Invoke(i * 10);

                Assert.That(move.GetAwaiter().GetResult(), Is.True);
                Assert.That(enumerator.Current, Is.EqualTo(i * 10));
            }

            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Enumerator_TwoEnumeratorsOfOneSequence_AreIndependent()
        {
            UnityEvent<int> unityEvent = new UnityEvent<int>();
            IOnityAsyncEnumerable<int> sequence = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None);
            IOnityAsyncEnumerator<int> first = sequence.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> second = sequence.GetAsyncEnumerator();
            OnityTask<bool> firstMove = first.MoveNextAsync();
            OnityTask<bool> secondMove = second.MoveNextAsync();

            unityEvent.Invoke(7);

            Assert.That(firstMove.GetAwaiter().GetResult(), Is.True);
            Assert.That(secondMove.GetAwaiter().GetResult(), Is.True);
            Assert.That(first.Current, Is.EqualTo(7));
            Assert.That(second.Current, Is.EqualTo(7));

            first.DisposeAsync().GetAwaiter().GetResult();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(1), "disposing one enumerator keeps the other");
            second.DisposeAsync().GetAwaiter().GetResult();
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
        }

        [Test]
        public void Enumerator_SourceTokenCancel_CancelsThePendingMove_AndEndsTheEnumeration()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(source.Token)
                    .GetAsyncEnumerator();
                OnityTask<bool> move = enumerator.MoveNextAsync();

                source.Cancel();

                Assert.That(move.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
                Assert.That(enumerator.MoveNextAsync().IsCanceled, Is.True);
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void Enumerator_ConsumerTokenCancel_CancelsThePendingMove_AndEndsTheEnumeration()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource consumer = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None)
                    .GetAsyncEnumerator(consumer.Token);
                OnityTask<bool> move = enumerator.MoveNextAsync();

                consumer.Cancel();

                Assert.That(move.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
                Assert.That(enumerator.MoveNextAsync().IsCanceled, Is.True);
            }
        }

        [Test]
        public void Enumerator_SameTokenForSourceAndConsumer_CancelsOnce()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource shared = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(shared.Token)
                    .GetAsyncEnumerator(shared.Token);
                OnityTask<bool> move = enumerator.MoveNextAsync();

                shared.Cancel();

                Assert.That(move.IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
            }
        }

        [Test]
        public void Enumerator_PreCanceledToken_ReturnsACanceledMoveWithoutListening()
        {
            UnityEvent unityEvent = new UnityEvent();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();
                IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None)
                    .GetAsyncEnumerator(source.Token);

                Assert.That(enumerator.MoveNextAsync().IsCanceled, Is.True);
                Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
            }
        }

        [Test]
        public void Enumerator_DisposeWhileWaiting_EndsTheMoveFalse_AndStopsListening()
        {
            UnityEvent unityEvent = new UnityEvent();
            IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None)
                .GetAsyncEnumerator();
            OnityTask<bool> move = enumerator.MoveNextAsync();

            enumerator.DisposeAsync().GetAwaiter().GetResult();

            Assert.That(move.GetAwaiter().GetResult(), Is.False);
            Assert.That(RuntimeListenerCount(unityEvent), Is.EqualTo(0));
            Assert.That(enumerator.MoveNextAsync().GetAwaiter().GetResult(), Is.False);
            enumerator.DisposeAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void Enumerator_SecondOutstandingMove_Throws_AndCurrentWithoutAnItemThrows()
        {
            UnityEvent unityEvent = new UnityEvent();
            IOnityAsyncEnumerator<Unit> enumerator = unityEvent.OnInvokeAsAsyncEnumerable(CancellationToken.None)
                .GetAsyncEnumerator();

            Assert.Throws<InvalidOperationException>(() => { Unit unused = enumerator.Current; });

            OnityTask<bool> move = enumerator.MoveNextAsync();
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNextAsync());
            Assert.Throws<InvalidOperationException>(() => { Unit unused = enumerator.Current; });

            enumerator.DisposeAsync().GetAwaiter().GetResult();
            Assert.That(move.GetAwaiter().GetResult(), Is.False);
        }

        [Test]
        public void NullEvents_Throw()
        {
            UnityEvent untyped = null;
            UnityEvent<int> typed = null;

            Assert.Throws<ArgumentNullException>(() => untyped.GetAsyncEventHandler(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => untyped.OnInvokeAsync(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => untyped.OnInvokeAsAsyncEnumerable(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => typed.GetAsyncEventHandler(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => typed.OnInvokeAsync(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => typed.OnInvokeAsAsyncEnumerable(CancellationToken.None));
        }
    }
}
