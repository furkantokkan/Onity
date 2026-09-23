using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityTaskCompletionSourceEditModeTests
    {
        [Test]
        public void Task_IsSourceBackedUntilAsTaskIsRequested()
        {
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();
            OnityTask<int> typedTask = typedSource.Task;
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();
            OnityTask untypedTask = untypedSource.Task;

            Assert.That(GetTaskState(typedTask), Is.SameAs(typedSource));
            Assert.That(GetTaskState(untypedTask), Is.SameAs(untypedSource));
            Assert.That(GetTaskBridge(typedSource), Is.Null);
            Assert.That(GetTaskBridge<bool>(untypedSource), Is.Null);

            Assert.That(typedSource.TrySetResult(17), Is.True);
            Assert.That(untypedSource.TrySetResult(), Is.True);
            Assert.That(GetTaskBridge(typedSource), Is.Null);
            Assert.That(GetTaskBridge<bool>(untypedSource), Is.Null);
            Assert.That(typedTask.GetAwaiter().GetResult(), Is.EqualTo(17));
            Assert.DoesNotThrow(() => untypedTask.GetAwaiter().GetResult());
        }

        [Test]
        public void TypedTask_InvokesMultipleAndLateObservers()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<int> task = source.Task;
            int firstCalls = 0;
            int secondCalls = 0;
            int thirdCalls = 0;
            int lateCalls = 0;

            task.GetAwaiter().OnCompleted(() => firstCalls++);
            task.GetAwaiter().OnCompleted(() => secondCalls++);
            task.GetAwaiter().OnCompleted(() => thirdCalls++);

            Assert.That(source.TrySetResult(23), Is.True);
            task.GetAwaiter().OnCompleted(() => lateCalls++);

            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.EqualTo(1));
            Assert.That(thirdCalls, Is.EqualTo(1));
            Assert.That(lateCalls, Is.EqualTo(1));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(23));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(23));
        }

        [Test]
        public void UntypedTask_InvokesMultipleAndLateObservers()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTask task = source.Task;
            int calls = 0;

            task.GetAwaiter().OnCompleted(() => calls++);
            task.GetAwaiter().OnCompleted(() => calls++);
            Assert.That(source.TrySetResult(), Is.True);
            task.GetAwaiter().OnCompleted(() => calls++);

            Assert.That(calls, Is.EqualTo(3));
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public async Task TypedTask_AllowsTwoPendingAsyncConsumers()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTask<int> sharedTask = source.Task;
            Task<int> first = ObserveAsync(sharedTask);
            Task<int> second = ObserveAsync(sharedTask);

            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(source.TrySetResult(28), Is.True);

            Assert.That(await first, Is.EqualTo(28));
            Assert.That(await second, Is.EqualTo(28));
        }

        [Test]
        public void TerminalOutcome_CannotBeOverwritten()
        {
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();

            Assert.That(typedSource.TrySetResult(7), Is.True);
            Assert.That(typedSource.TrySetResult(8), Is.False);
            Assert.That(typedSource.TrySetException(new InvalidOperationException()), Is.False);
            Assert.That(typedSource.TrySetCanceled(), Is.False);
            Assert.That(typedSource.Task.GetAwaiter().GetResult(), Is.EqualTo(7));

            Assert.That(untypedSource.TrySetResult(), Is.True);
            Assert.That(untypedSource.TrySetResult(), Is.False);
            Assert.That(untypedSource.TrySetException(new InvalidOperationException()), Is.False);
            Assert.That(untypedSource.TrySetCanceled(), Is.False);
            Assert.That(untypedSource.Task.IsCompletedSuccessfully, Is.True);
        }

        [Test]
        public void Exception_IsPreservedForNativeAndTaskConsumers()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            InvalidOperationException failure = new InvalidOperationException("source failure");
            Task<int> bridge = source.Task.AsTask();

            Assert.That(source.TrySetException(failure), Is.True);
            Assert.That(source.Task.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => source.Task.GetAwaiter().GetResult()), Is.SameAs(failure));
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => bridge.GetAwaiter().GetResult()), Is.SameAs(failure));
            Assert.Throws<ArgumentNullException>(() => source.TrySetException(null));
        }

        [Test]
        public void UntypedFault_IsPreservedForLateTaskConversion()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            InvalidOperationException failure = new InvalidOperationException("untyped failure");

            Assert.That(source.TrySetException(failure), Is.True);
            Assert.That(source.TrySetResult(), Is.False);
            Assert.That(source.Task.IsFaulted, Is.True);
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => source.Task.GetAwaiter().GetResult()), Is.SameAs(failure));
            Task bridge = source.Task.AsTask();
            Assert.That(bridge, Is.SameAs(source.Task.AsTask()));
            Assert.That(Assert.Throws<InvalidOperationException>(
                () => bridge.GetAwaiter().GetResult()), Is.SameAs(failure));
        }

        [Test]
        public void Cancellation_PreservesTokenForNativeAndTaskConsumers()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();
            Task<int> typedBridge = typedSource.Task.AsTask();
            Task untypedBridge = untypedSource.Task.AsTask();

            Assert.That(typedSource.TrySetCanceled(cancellation.Token), Is.True);
            Assert.That(untypedSource.TrySetCanceled(cancellation.Token), Is.True);
            Assert.That(typedSource.Task.IsCanceled, Is.True);
            Assert.That(untypedSource.Task.IsCanceled, Is.True);
            Assert.That(Assert.Throws<OperationCanceledException>(
                () => typedSource.Task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(Assert.Throws<OperationCanceledException>(
                () => untypedSource.Task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => typedBridge.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(Assert.Catch<OperationCanceledException>(
                () => untypedBridge.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void CancellationException_CompletesAsCanceled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            Assert.That(source.TrySetException(
                new OperationCanceledException(cancellation.Token)), Is.True);
            Assert.That(source.Task.IsCanceled, Is.True);
            Assert.That(Assert.Throws<OperationCanceledException>(
                () => source.Task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void Callback_CanRegisterAnotherObserverAndCannotChangeResult()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            int calls = 0;
            bool secondCompletionWon = true;

            source.Task.GetAwaiter().OnCompleted(() =>
            {
                calls++;
                source.Task.GetAwaiter().OnCompleted(() => calls++);
                secondCompletionWon = source.TrySetResult(99);
            });

            Assert.That(source.TrySetResult(42), Is.True);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(secondCompletionWon, Is.False);
            Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(42));
        }

        [Test]
        public void ThrowingObserver_DoesNotBlockOtherObservers()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            bool nextObserverRan = false;
            LogAssert.Expect(LogType.Exception, new Regex("observer failure"));

            source.Task.GetAwaiter().OnCompleted(() =>
                throw new InvalidOperationException("observer failure"));
            source.Task.GetAwaiter().OnCompleted(() => nextObserverRan = true);

            Assert.That(source.TrySetResult(), Is.True);
            Assert.That(nextObserverRan, Is.True);
        }

        [Test]
        public void AsTask_IsCachedBeforeAndAfterCompletion()
        {
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();
            Task<int> earlyTyped = typedSource.Task.AsTask();
            Task earlyUntyped = untypedSource.Task.AsTask();

            Assert.That(typedSource.Task.AsTask(), Is.SameAs(earlyTyped));
            Assert.That(untypedSource.Task.AsTask(), Is.SameAs(earlyUntyped));
            Assert.That(typedSource.TrySetResult(54), Is.True);
            Assert.That(untypedSource.TrySetResult(), Is.True);
            Assert.That(typedSource.Task.AsTask(), Is.SameAs(earlyTyped));
            Assert.That(untypedSource.Task.AsTask(), Is.SameAs(earlyUntyped));
            Assert.That(earlyTyped.GetAwaiter().GetResult(), Is.EqualTo(54));
            Assert.DoesNotThrow(() => earlyUntyped.GetAwaiter().GetResult());

            OnityTaskCompletionSource<int> lateSource = new OnityTaskCompletionSource<int>();
            lateSource.TrySetResult(55);
            Task<int> lateBridge = lateSource.Task.AsTask();
            Assert.That(lateBridge, Is.SameAs(lateSource.Task.AsTask()));
            Assert.That(lateBridge.IsCompleted, Is.True);
            Assert.That(lateBridge.GetAwaiter().GetResult(), Is.EqualTo(55));
        }

        [Test]
        public void PublishedCompletion_HasCompletedTaskBridge()
        {
            for (int iteration = 0; iteration < 128; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                Task<int> bridge = source.Task.AsTask();
                Task completion = Task.Run(() => source.TrySetResult(82));

                Assert.That(SpinWait.SpinUntil(() => source.Task.IsCompleted, 5000), Is.True);
                Assert.That(bridge.IsCompleted, Is.True);
                Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo(82));
                Assert.That(completion.Wait(5000), Is.True);
            }
        }

        [Test]
        public void ConcurrentTerminalCalls_HaveOneWinnerAndMatchingOutcome()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            for (int iteration = 0; iteration < 64; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                Task<bool> result = Task.Run(() =>
                {
                    start.Wait();
                    return source.TrySetResult(71);
                });
                Task<bool> fault = Task.Run(() =>
                {
                    start.Wait();
                    return source.TrySetException(new InvalidOperationException("race failure"));
                });
                Task<bool> cancel = Task.Run(() =>
                {
                    start.Wait();
                    return source.TrySetCanceled(cancellation.Token);
                });

                start.Set();
                Assert.That(Task.WaitAll(new Task[] { result, fault, cancel }, 5000), Is.True);
                int winnerCount = (result.Result ? 1 : 0)
                    + (fault.Result ? 1 : 0)
                    + (cancel.Result ? 1 : 0);
                Assert.That(winnerCount, Is.EqualTo(1));

                if (result.Result)
                {
                    Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(71));
                }
                else if (fault.Result)
                {
                    Assert.That(source.Task.IsFaulted, Is.True);
                    Assert.Throws<InvalidOperationException>(
                        () => source.Task.GetAwaiter().GetResult());
                }
                else
                {
                    Assert.That(source.Task.IsCanceled, Is.True);
                    Assert.That(Assert.Throws<OperationCanceledException>(
                        () => source.Task.GetAwaiter().GetResult()).CancellationToken,
                        Is.EqualTo(cancellation.Token));
                }
            }
        }

        [Test]
        public void RegistrationAndCompletionRace_InvokesObserverExactlyOnce()
        {
            for (int iteration = 0; iteration < 64; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                int calls = 0;
                Task register = Task.Run(() =>
                {
                    start.Wait();
                    source.Task.GetAwaiter().OnCompleted(() => Interlocked.Increment(ref calls));
                });
                Task complete = Task.Run(() =>
                {
                    start.Wait();
                    source.TrySetResult(61);
                });

                start.Set();
                Assert.That(Task.WaitAll(new[] { register, complete }, 5000), Is.True);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(61));
            }
        }

        [Test]
        public void ConcurrentRegistrations_KeepAllObservers()
        {
            for (int iteration = 0; iteration < 32; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                int calls = 0;
                Task[] registrations = new Task[4];
                for (int index = 0; index < registrations.Length; index++)
                {
                    registrations[index] = Task.Run(() =>
                    {
                        start.Wait();
                        source.Task.GetAwaiter().OnCompleted(
                            () => Interlocked.Increment(ref calls));
                    });
                }

                start.Set();
                Assert.That(Task.WaitAll(registrations, 5000), Is.True);
                Assert.That(source.TrySetResult(1), Is.True);
                Assert.That(calls, Is.EqualTo(registrations.Length));
            }
        }

        [Test]
        public void AsTaskAndCompletionRace_AlwaysCompletesOneCachedBridge()
        {
            for (int iteration = 0; iteration < 64; iteration++)
            {
                OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
                using ManualResetEventSlim start = new ManualResetEventSlim(false);
                int expected = iteration;
                Task<Task<int>> convert = Task.Run<Task<int>>(() =>
                {
                    start.Wait();
                    return source.Task.AsTask();
                });
                Task complete = Task.Run(() =>
                {
                    start.Wait();
                    source.TrySetResult(expected);
                });

                start.Set();
                Assert.That(Task.WaitAll(new Task[] { convert, complete }, 5000), Is.True);
                Task<int> bridge = convert.Result;
                Assert.That(bridge, Is.SameAs(source.Task.AsTask()));
                Assert.That(bridge.IsCompleted, Is.True);
                Assert.That(bridge.GetAwaiter().GetResult(), Is.EqualTo(expected));
            }
        }

        private static async Task<int> ObserveAsync(OnityTask<int> task)
        {
            return await task;
        }

        private static object GetTaskState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField(
                "m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetTaskState(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField(
                "m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static object GetTaskBridge<T>(OnityTaskCompletionSource<T> source)
        {
            FieldInfo field = typeof(OnityTaskCompletionSource<T>).GetField(
                "m_taskBridge", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(source);
        }
    }
}
