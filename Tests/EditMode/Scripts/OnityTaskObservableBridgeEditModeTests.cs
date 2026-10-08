using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Reactive;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the bridges between tasks and observables: <c>ToOnityTask</c> over an observable with
    /// last-value and first-value modes, and <c>ToObservable</c> over a task.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskObservableBridgeEditModeTests
    {
        private const int k_timeoutSeconds = 10;

        private readonly List<IDisposable> m_subscriptions = new List<IDisposable>();

        private static IEnumerator WaitFor(Func<bool> predicate)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (!predicate() && timer.Elapsed.TotalSeconds < k_timeoutSeconds)
            {
                yield return null;
            }

            Assert.That(predicate(), Is.True, "The observable task timed out.");
        }

        [TearDown]
        public void DisposeSubscriptions()
        {
            for (int i = 0; i < m_subscriptions.Count; i++)
            {
                m_subscriptions[i].Dispose();
            }

            m_subscriptions.Clear();
        }

        private void Track(IDisposable subscription)
        {
            m_subscriptions.Add(subscription);
        }

        private sealed class TestObservable<T> : IOnityObservable<T>
        {
            public readonly List<OnityObserver<T>> Observers = new List<OnityObserver<T>>();
            public int SubscribeCount;
            public int DisposeCount;
            public Action<OnityObserver<T>> OnSubscribe;
            public Exception SubscribeFailure;

            public IDisposable Subscribe(Observer<T> observer)
            {
                return Subscribe(new CallbackObserver(observer));
            }

            public IDisposable Subscribe(OnityObserver<T> observer)
            {
                SubscribeCount++;
                if (SubscribeFailure != null)
                {
                    throw SubscribeFailure;
                }

                Observers.Add(observer);
                OnSubscribe?.Invoke(observer);
                return new DisposableAction(() => DisposeCount++);
            }

            public void Emit(T value)
            {
                for (int i = 0; i < Observers.Count; i++)
                {
                    Observers[i].OnNext(value);
                }
            }

            public void Complete()
            {
                for (int i = 0; i < Observers.Count; i++)
                {
                    Observers[i].OnCompleted();
                }
            }

            public void Fail(Exception exception)
            {
                for (int i = 0; i < Observers.Count; i++)
                {
                    Observers[i].OnError(exception);
                }
            }

            private sealed class CallbackObserver : OnityObserver<T>
            {
                private readonly Observer<T> m_callback;

                public CallbackObserver(Observer<T> callback)
                {
                    m_callback = callback;
                }

                protected override void OnNextCore(T value)
                {
                    m_callback(value);
                }
            }
        }

        private sealed class Recorder<T> : OnityObserver<T>
        {
            public readonly List<T> Values = new List<T>();
            public int Completed;
            public Exception Error;
            public bool ThrowOnNext;

            protected override void OnNextCore(T value)
            {
                if (ThrowOnNext)
                {
                    throw new InvalidOperationException("observer failed");
                }

                Values.Add(value);
            }

            protected override void OnErrorCore(Exception exception)
            {
                Error = exception;
            }

            protected override void OnCompletedCore(OnityResult result)
            {
                Completed++;
            }
        }

        [Test]
        public void ToOnityTask_LastValueMode_CompletesWithTheLastValueWhenTheSourceCompletes()
        {
            TestObservable<int> source = new TestObservable<int>();

            OnityTask<int> task = source.ToOnityTask();
            source.Emit(1);
            source.Emit(2);
            source.Emit(3);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending), "The task waits for completion.");
            source.Complete();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(3));
            Assert.That(source.DisposeCount, Is.EqualTo(1), "The subscription ends with the task.");
        }

        [Test]
        public void ToOnityTask_LastValueMode_CompletionWithoutValue_FaultsWithNoElements()
        {
            TestObservable<int> source = new TestObservable<int>();

            OnityTask<int> task = source.ToOnityTask();
            source.Complete();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            InvalidOperationException failure =
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(failure.Message, Is.EqualTo("Sequence has no elements"));
        }

        [Test]
        public void ToOnityTask_FirstValueMode_CompletesAtTheFirstValueAndEndsTheSubscription()
        {
            TestObservable<string> source = new TestObservable<string>();

            OnityTask<string> task = source.ToOnityTask(true);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.Emit("first");
            source.Emit("second");

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("first"));
            Assert.That(source.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToOnityTask_FirstValueMode_CompletionWithoutValue_Faults()
        {
            TestObservable<int> source = new TestObservable<int>();

            OnityTask<int> task = source.ToOnityTask(true);
            source.Complete();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void ToOnityTask_SourceError_FaultsTheTask()
        {
            InvalidOperationException failure = new InvalidOperationException("source failed");
            TestObservable<int> errored = new TestObservable<int>();
            TestObservable<int> failedCompletion = new TestObservable<int>();

            OnityTask<int> fromError = errored.ToOnityTask();
            OnityTask<int> fromFailureResult = failedCompletion.ToOnityTask(true);
            errored.Fail(failure);
            failedCompletion.Observers[0].OnCompleted(OnityResult.Failure(failure));

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromError.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromFailureResult.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(errored.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToOnityTask_Cancellation_CancelsTheTaskAndEndsTheSubscription()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TestObservable<int> source = new TestObservable<int>();

            OnityTask<int> task = source.ToOnityTask(false, cancellation.Token);
            source.Emit(1);
            cancellation.Cancel();
            source.Complete();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(source.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToOnityTask_AlreadyCanceledToken_ReturnsACanceledTaskWithoutSubscribing()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            TestObservable<int> source = new TestObservable<int>();

            OnityTask<int> task = source.ToOnityTask(false, cancellation.Token);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(source.SubscribeCount, Is.Zero);
        }

        [Test]
        public void ToOnityTask_SubscribeThrows_FaultsTheTask()
        {
            InvalidOperationException failure = new InvalidOperationException("subscribe failed");
            TestObservable<int> source = new TestObservable<int> { SubscribeFailure = failure };

            OnityTask<int> task = source.ToOnityTask();

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void ToOnityTask_FirstValueDeliveredDuringSubscribe_EndsTheSubscriptionAfterItIsAssigned()
        {
            TestObservable<int> source = new TestObservable<int>();
            source.OnSubscribe = observer => observer.OnNext(5);

            OnityTask<int> task = source.ToOnityTask(true);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(5));
            Assert.That(source.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToOnityTask_CompletionDeliveredDuringSubscribe_CompletesWithTheLastValue()
        {
            TestObservable<int> source = new TestObservable<int>();
            source.OnSubscribe = observer =>
            {
                observer.OnNext(1);
                observer.OnNext(2);
                observer.OnCompleted();
            };

            OnityTask<int> task = source.ToOnityTask();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(2));
            Assert.That(source.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToOnityTask_NullSource_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ((IOnityObservable<int>)null).ToOnityTask());
        }

        // The Unit overload is bridged through a .NET task, so it completes after the first value
        // without ever guaranteeing that it completes synchronously: the tests wait for it.
        [UnityTest]
        public IEnumerator ToOnityTask_UnitStreamWithoutArguments_KeepsBindingToTheFirstValueOverload()
        {
            TestObservable<Unit> source = new TestObservable<Unit>();

            // Compiles only when the call binds to the non-generic overload that returns OnityTask.
            OnityTask task = source.ToOnityTask();
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.Emit(Unit.Default);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded),
                "The first value completes it without waiting for the source to complete.");
        }

        [UnityTest]
        public IEnumerator ToOnityTask_UnitStreamWithATokenOnly_KeepsBindingToTheFirstValueOverload()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            TestObservable<Unit> source = new TestObservable<Unit>();

            OnityTask task = source.ToOnityTask(cancellation.Token);
            source.Emit(Unit.Default);
            yield return WaitFor(() => task.IsCompleted);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded),
                "The first value completes it without waiting for the source to complete.");
        }

        [Test]
        public void ToOnityTask_UnitStreamWithTheFirstValueArgument_UsesTheGenericOverload()
        {
            TestObservable<Unit> source = new TestObservable<Unit>();

            OnityTask<Unit> task = source.ToOnityTask(true);
            source.Emit(Unit.Default);

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void ToOnityTask_Subject_FirstValueModeCompletesAndLastValueModeWaits()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Subject<int> subject = new Subject<int>();

            OnityTask<int> first = subject.ToOnityTask(true);
            OnityTask<int> last = subject.ToOnityTask(false, cancellation.Token);
            subject.OnNext(7);

            Assert.That(first.GetAwaiter().GetResult(), Is.EqualTo(7));
            Assert.That(last.Status, Is.EqualTo(OnityTaskStatus.Pending),
                "A subject never completes, so the last-value mode keeps waiting.");
            cancellation.Cancel();
            Assert.That(last.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            subject.Dispose();
        }

        [Test]
        public void ToObservable_CompletedTask_DeliversTheValueThenCompletion()
        {
            Recorder<int> recorder = new Recorder<int>();

            Track(OnityTask.FromResult(4).ToObservable().Subscribe(recorder));

            Assert.That(recorder.Values, Is.EqualTo(new[] { 4 }));
            Assert.That(recorder.Completed, Is.EqualTo(1));
            Assert.That(recorder.Error, Is.Null);
        }

        [Test]
        public void ToObservable_PendingTask_NotifiesEverySubscriberWhenTheTaskCompletes()
        {
            OnityTaskCompletionSource<string> source = new OnityTaskCompletionSource<string>();
            IOnityObservable<string> observable = source.Task.ToObservable();
            Recorder<string> first = new Recorder<string>();
            Recorder<string> second = new Recorder<string>();
            List<string> callbackValues = new List<string>();

            Track(observable.Subscribe(first));
            Track(observable.Subscribe(second));
            Track(observable.Subscribe(new Observer<string>(callbackValues.Add)));
            Assert.That(first.Values, Is.Empty);
            source.TrySetResult("done");

            Assert.That(first.Values, Is.EqualTo(new[] { "done" }));
            Assert.That(second.Values, Is.EqualTo(new[] { "done" }));
            Assert.That(callbackValues, Is.EqualTo(new[] { "done" }));
            Assert.That(first.Completed, Is.EqualTo(1));
        }

        [Test]
        public void ToObservable_LateSubscriber_ReceivesTheReplayedOutcome()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            IOnityObservable<int> observable = source.Task.ToObservable();
            source.TrySetResult(9);
            Recorder<int> late = new Recorder<int>();

            Track(observable.Subscribe(late));

            Assert.That(late.Values, Is.EqualTo(new[] { 9 }));
            Assert.That(late.Completed, Is.EqualTo(1));
        }

        [Test]
        public void ToObservable_FaultedAndCanceledTasks_ReportOnError()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("task failed");
            Recorder<int> faulted = new Recorder<int>();
            Recorder<int> canceled = new Recorder<int>();

            Track(OnityTask.FromException<int>(failure).ToObservable().Subscribe(faulted));
            Track(OnityTask.FromCanceled<int>(cancellation.Token).ToObservable().Subscribe(canceled));

            Assert.That(faulted.Error, Is.SameAs(failure));
            Assert.That(faulted.Values, Is.Empty);
            Assert.That(canceled.Error, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ToObservable_DisposedSubscription_StopsNotifications()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            IOnityObservable<int> observable = source.Task.ToObservable();
            Recorder<int> recorder = new Recorder<int>();
            Recorder<int> other = new Recorder<int>();

            IDisposable subscription = observable.Subscribe(recorder);
            Track(observable.Subscribe(other));
            subscription.Dispose();
            source.TrySetResult(3);

            Assert.That(recorder.Values, Is.Empty);
            Assert.That(other.Values, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void ToObservable_CallbackSubscriberOfAFaultedTask_RoutesTheErrorToTheExceptionHandler()
        {
            Action<Exception> previous = OnityObservableExceptionHandler.Handler;
            List<Exception> handled = new List<Exception>();
            OnityObservableExceptionHandler.Handler = handled.Add;
            try
            {
                InvalidOperationException failure = new InvalidOperationException("callback fault");

                Track(OnityTask.FromException<int>(failure).ToObservable().Subscribe(new Observer<int>(_ => { })));

                Assert.That(handled, Is.EqualTo(new[] { failure }));
            }
            finally
            {
                OnityObservableExceptionHandler.Handler = previous;
            }
        }

        [Test]
        public void ToObservable_ThrowingObserver_DoesNotStopTheOtherObservers()
        {
            Action<Exception> previous = OnityObservableExceptionHandler.Handler;
            List<Exception> handled = new List<Exception>();
            OnityObservableExceptionHandler.Handler = handled.Add;
            try
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                IOnityObservable<int> observable = source.Task.ToObservable();
                Recorder<int> throwing = new Recorder<int> { ThrowOnNext = true };
                Recorder<int> healthy = new Recorder<int>();

                Track(observable.Subscribe(throwing));
                Track(observable.Subscribe(healthy));
                source.TrySetResult(2);

                Assert.That(healthy.Values, Is.EqualTo(new[] { 2 }));
                Assert.That(handled.Count, Is.EqualTo(1));
                Assert.That(handled[0], Is.InstanceOf<InvalidOperationException>());
            }
            finally
            {
                OnityObservableExceptionHandler.Handler = previous;
            }
        }

        [Test]
        public void ToObservable_Untyped_EmitsUnitWhenTheTaskSucceeds()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Recorder<Unit> recorder = new Recorder<Unit>();

            Track(source.Task.ToObservable().Subscribe(recorder));
            Assert.That(recorder.Values, Is.Empty);
            source.TrySetResult();

            Assert.That(recorder.Values.Count, Is.EqualTo(1));
            Assert.That(recorder.Completed, Is.EqualTo(1));
        }

        [Test]
        public void ToObservable_ConsumesASingleConsumerTask()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> original = source.Task;
            Recorder<int> recorder = new Recorder<int>();

            Track(original.ToObservable().Subscribe(recorder));
            source.TrySetResult(8);

            Assert.That(recorder.Values, Is.EqualTo(new[] { 8 }));
            Assert.Throws<InvalidOperationException>(() => _ = original.IsCompleted);
        }

        [Test]
        public void ToObservable_ThenToOnityTask_RoundTripsTheResult()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            OnityTask<int> roundTrip = source.Task.ToObservable().ToOnityTask();
            Assert.That(roundTrip.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult(21);

            Assert.That(roundTrip.GetAwaiter().GetResult(), Is.EqualTo(21));
        }
    }
}
