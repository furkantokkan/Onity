using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAutoResetTaskCompletionSourceEditModeTests
    {
        private const int k_waitMilliseconds = 5000;

        [Test]
        public void Typed_Result_AwaitAfterCompletion()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;

            Assert.That(source.TrySetResult(42), Is.True);

            OnityTaskAwaiter<int> awaiter = task.GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.True);
            Assert.That(awaiter.GetResult(), Is.EqualTo(42));
        }

        [Test]
        public void Untyped_Result_AwaitAfterCompletion()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;

            Assert.That(source.TrySetResult(), Is.True);

            OnityTaskAwaiter awaiter = task.GetAwaiter();
            Assert.That(awaiter.IsCompleted, Is.True);
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [Test]
        public void Typed_Result_AwaitBeforeCompletion()
        {
            OnityAutoResetTaskCompletionSource<string> source = OnityAutoResetTaskCompletionSource<string>.Create();
            OnityTaskAwaiter<string> awaiter = source.Task.GetAwaiter();
            string observed = null;
            int calls = 0;

            Assert.That(awaiter.IsCompleted, Is.False);
            awaiter.OnCompleted(() =>
            {
                calls++;
                observed = awaiter.GetResult();
            });
            Assert.That(calls, Is.EqualTo(0));

            Assert.That(source.TrySetResult("done"), Is.True);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observed, Is.EqualTo("done"));
        }

        [Test]
        public void Untyped_Result_AwaitBeforeCompletion()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTaskAwaiter awaiter = source.Task.GetAwaiter();
            int calls = 0;

            Assert.That(awaiter.IsCompleted, Is.False);
            awaiter.OnCompleted(() =>
            {
                calls++;
                awaiter.GetResult();
            });
            Assert.That(calls, Is.EqualTo(0));

            Assert.That(source.TrySetResult(), Is.True);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Typed_LateContinuation_RunsInline()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTaskAwaiter<int> awaiter = source.Task.GetAwaiter();
            int result = 0;

            Assert.That(source.TrySetResult(9), Is.True);
            awaiter.OnCompleted(() => result = awaiter.GetResult());

            Assert.That(result, Is.EqualTo(9));
        }

        [Test]
        public void Typed_Exception_IsRethrownToConsumer()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            InvalidOperationException failure = new InvalidOperationException("typed failure");

            Assert.That(source.TrySetException(failure), Is.True);

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Untyped_Exception_IsRethrownToConsumer()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;
            InvalidOperationException failure = new InvalidOperationException("untyped failure");

            Assert.That(source.TrySetException(failure), Is.True);

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Typed_OperationCanceledExceptionCompletesAsCanceled()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
                OnityTask<int> task = source.Task;
                OperationCanceledException canceled = new OperationCanceledException(cancellation.Token);

                Assert.That(source.TrySetException(canceled), Is.True);

                OperationCanceledException thrown = Assert.Throws<OperationCanceledException>(
                    () => task.GetAwaiter().GetResult());
                Assert.That(thrown, Is.SameAs(canceled));
                Assert.That(thrown.CancellationToken, Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Untyped_OperationCanceledExceptionCompletesAsCanceled()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
                OnityTask task = source.Task;
                OperationCanceledException canceled = new OperationCanceledException(cancellation.Token);

                Assert.That(source.TrySetException(canceled), Is.True);

                OperationCanceledException thrown = Assert.Throws<OperationCanceledException>(
                    () => task.GetAwaiter().GetResult());
                Assert.That(thrown, Is.SameAs(canceled));
                Assert.That(thrown.CancellationToken, Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Typed_Cancel_PreservesTokenIdentity()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
                OnityTask<int> task = source.Task;

                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);

                Assert.That(
                    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                    Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Untyped_Cancel_PreservesTokenIdentity()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
                OnityTask task = source.Task;

                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);

                Assert.That(
                    Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                    Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Cancel_WithDefaultToken_ThrowsOperationCanceled()
        {
            OnityAutoResetTaskCompletionSource untypedSource = OnityAutoResetTaskCompletionSource.Create();
            OnityTask untypedTask = untypedSource.Task;
            OnityAutoResetTaskCompletionSource<int> typedSource = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> typedTask = typedSource.Task;

            Assert.That(untypedSource.TrySetCanceled(), Is.True);
            Assert.That(typedSource.TrySetCanceled(), Is.True);

            Assert.Throws<OperationCanceledException>(() => untypedTask.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => typedTask.GetAwaiter().GetResult());
        }

        [Test]
        public void Cancel_AwaitBeforeCompletion_NotifiesContinuation()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
                OnityTaskAwaiter<int> awaiter = source.Task.GetAwaiter();
                CancellationToken observed = default;

                awaiter.OnCompleted(() =>
                {
                    try
                    {
                        awaiter.GetResult();
                    }
                    catch (OperationCanceledException exception)
                    {
                        observed = exception.CancellationToken;
                    }
                });

                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);
                Assert.That(observed, Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Typed_SecondCompletion_ReturnsFalseAndKeepsFirstOutcome()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;

            Assert.That(source.TrySetResult(1), Is.True);
            Assert.That(source.TrySetResult(2), Is.False);
            Assert.That(source.TrySetException(new InvalidOperationException()), Is.False);
            Assert.That(source.TrySetCanceled(), Is.False);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(1));
        }

        [Test]
        public void Untyped_SecondCompletion_ReturnsFalseAndKeepsFirstOutcome()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            InvalidOperationException failure = new InvalidOperationException("first");
            OnityTask task = source.Task;

            Assert.That(source.TrySetException(failure), Is.True);
            Assert.That(source.TrySetResult(), Is.False);
            Assert.That(source.TrySetException(new InvalidOperationException("second")), Is.False);
            Assert.That(source.TrySetCanceled(), Is.False);

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void TrySetException_Null_Throws()
        {
            OnityAutoResetTaskCompletionSource untypedSource = OnityAutoResetTaskCompletionSource.Create();
            OnityAutoResetTaskCompletionSource<int> typedSource = OnityAutoResetTaskCompletionSource<int>.Create();

            Assert.Throws<ArgumentNullException>(() => untypedSource.TrySetException(null));
            Assert.Throws<ArgumentNullException>(() => typedSource.TrySetException(null));
            Assert.Throws<ArgumentNullException>(
                () => OnityAutoResetTaskCompletionSource.CreateFromException(null, out _));
            Assert.Throws<ArgumentNullException>(
                () => OnityAutoResetTaskCompletionSource<int>.CreateFromException(null, out _));
        }

        [Test]
        public void Typed_PoolReuse_ReturnsSameInstanceWithNewVersion()
        {
            OnityAutoResetTaskCompletionSource<int> first = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> firstTask = first.Task;
            int firstToken = GetToken(firstTask);

            Assert.That(first.TrySetResult(5), Is.True);
            Assert.That(firstTask.GetAwaiter().GetResult(), Is.EqualTo(5));

            OnityAutoResetTaskCompletionSource<int> second = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> secondTask = second.Task;

            Assert.That(second, Is.SameAs(first));
            Assert.That(GetToken(secondTask), Is.Not.EqualTo(firstToken));
            Assert.That(secondTask.GetAwaiter().IsCompleted, Is.False);
            Assert.That(second.TrySetResult(6), Is.True);
            Assert.That(secondTask.GetAwaiter().GetResult(), Is.EqualTo(6));
        }

        [Test]
        public void Untyped_PoolReuse_ReturnsSameInstanceWithNewVersion()
        {
            OnityAutoResetTaskCompletionSource first = OnityAutoResetTaskCompletionSource.Create();
            OnityTask firstTask = first.Task;
            int firstToken = GetToken(firstTask);

            Assert.That(first.TrySetResult(), Is.True);
            firstTask.GetAwaiter().GetResult();

            OnityAutoResetTaskCompletionSource second = OnityAutoResetTaskCompletionSource.Create();
            OnityTask secondTask = second.Task;

            Assert.That(second, Is.SameAs(first));
            Assert.That(GetToken(secondTask), Is.Not.EqualTo(firstToken));
            Assert.That(secondTask.GetAwaiter().IsCompleted, Is.False);
            Assert.That(second.TrySetResult(), Is.True);
            Assert.DoesNotThrow(() => secondTask.GetAwaiter().GetResult());
        }

        [Test]
        public void PoolReuse_RepeatedCycles_KeepTheSameInstance()
        {
            OnityAutoResetTaskCompletionSource<int> expected = OnityAutoResetTaskCompletionSource<int>.Create();
            Assert.That(expected.TrySetResult(0), Is.True);
            expected.Task.GetAwaiter().GetResult();

            for (int i = 1; i <= 1000; i++)
            {
                OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
                Assert.That(source, Is.SameAs(expected));
                Assert.That(source.TrySetResult(i), Is.True);
                Assert.That(source.Task.GetAwaiter().GetResult(), Is.EqualTo(i));
            }
        }

        [Test]
        public void ConcurrentlyLiveSources_AreDistinctInstances()
        {
            OnityAutoResetTaskCompletionSource<int> first = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityAutoResetTaskCompletionSource<int> second = OnityAutoResetTaskCompletionSource<int>.Create();

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.TrySetResult(1), Is.True);
            Assert.That(second.TrySetResult(2), Is.True);
            Assert.That(first.Task.GetAwaiter().GetResult(), Is.EqualTo(1));
            Assert.That(second.Task.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void Typed_StaleToken_ThrowsAfterConsumption()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            Assert.That(source.TrySetResult(3), Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(3));

            // Reuse the instance so the stale value points at a live, pending cycle.
            OnityAutoResetTaskCompletionSource<int> reused = OnityAutoResetTaskCompletionSource<int>.Create();

            Assert.That(reused, Is.SameAs(source));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => _ = task.GetAwaiter().IsCompleted);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().OnCompleted(() => { }));
            Assert.Throws<InvalidOperationException>(() => task.AsTask());

            // The stale value must not have disturbed the live cycle.
            Assert.That(reused.TrySetResult(4), Is.True);
            Assert.That(reused.Task.GetAwaiter().GetResult(), Is.EqualTo(4));
        }

        [Test]
        public void Untyped_StaleToken_ThrowsAfterConsumption()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;
            Assert.That(source.TrySetResult(), Is.True);
            task.GetAwaiter().GetResult();

            OnityAutoResetTaskCompletionSource reused = OnityAutoResetTaskCompletionSource.Create();

            Assert.That(reused, Is.SameAs(source));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => _ = task.GetAwaiter().IsCompleted);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().OnCompleted(() => { }));
            Assert.Throws<InvalidOperationException>(() => task.AsTask());

            Assert.That(reused.TrySetResult(), Is.True);
            Assert.DoesNotThrow(() => reused.Task.GetAwaiter().GetResult());
        }

        [Test]
        public void SecondAwaitOfConsumedTask_Throws()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            Assert.That(source.TrySetResult(8), Is.True);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(8));
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void Completion_AfterConsumption_ReturnsFalseAndDoesNotTouchNextCycle()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            Assert.That(source.TrySetResult(1), Is.True);
            source.Task.GetAwaiter().GetResult();

            // The consumer has taken the result: the source belongs to the pool now.
            Assert.That(source.TrySetResult(2), Is.False);
            Assert.That(source.TrySetCanceled(), Is.False);
            Assert.That(source.TrySetException(new InvalidOperationException()), Is.False);

            OnityAutoResetTaskCompletionSource<int> reused = OnityAutoResetTaskCompletionSource<int>.Create();
            Assert.That(reused, Is.SameAs(source));
            Assert.That(reused.Task.GetAwaiter().IsCompleted, Is.False);
            Assert.That(reused.TrySetResult(3), Is.True);
            Assert.That(reused.Task.GetAwaiter().GetResult(), Is.EqualTo(3));
        }

        [Test]
        public void ConsumedFault_ReleasesSourceToPool()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;
            Assert.That(source.TrySetException(new InvalidOperationException("fault")), Is.True);
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());

            OnityAutoResetTaskCompletionSource reused = OnityAutoResetTaskCompletionSource.Create();

            Assert.That(reused, Is.SameAs(source));
            Assert.That(reused.Task.GetAwaiter().IsCompleted, Is.False);
        }

        [Test]
        public void Typed_AsTask_BridgeCompletesWithResultAndReleasesSource()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            Task<int> bridge = task.AsTask();

            Assert.That(bridge.IsCompleted, Is.False);
            Assert.That(source.TrySetResult(5), Is.True);

            Assert.That(bridge.Wait(k_waitMilliseconds), Is.True);
            Assert.That(bridge.Result, Is.EqualTo(5));
            Assert.That(OnityAutoResetTaskCompletionSource<int>.Create(), Is.SameAs(source));
        }

        [Test]
        public void Untyped_AsTask_BridgeCompletesAndReleasesSource()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            Task bridge = source.Task.AsTask();

            Assert.That(bridge.IsCompleted, Is.False);
            Assert.That(source.TrySetResult(), Is.True);

            Assert.That(bridge.Wait(k_waitMilliseconds), Is.True);
            Assert.That(bridge.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(OnityAutoResetTaskCompletionSource.Create(), Is.SameAs(source));
        }

        [Test]
        public void Typed_AsTask_AfterCompletion_ReturnsCompletedBridge()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            Assert.That(source.TrySetResult(11), Is.True);

            Task<int> bridge = task.AsTask();

            Assert.That(bridge.Wait(k_waitMilliseconds), Is.True);
            Assert.That(bridge.Result, Is.EqualTo(11));
            Assert.That(OnityAutoResetTaskCompletionSource<int>.Create(), Is.SameAs(source));
        }

        [Test]
        public void Typed_AsTask_BridgeCarriesFault()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            Task<int> bridge = source.Task.AsTask();
            InvalidOperationException failure = new InvalidOperationException("bridge failure");

            Assert.That(source.TrySetException(failure), Is.True);

            AggregateException aggregate = Assert.Throws<AggregateException>(() => bridge.Wait(k_waitMilliseconds));
            Assert.That(aggregate.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void Untyped_AsTask_BridgeIsCanceled()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
                Task bridge = source.Task.AsTask();

                Assert.That(source.TrySetCanceled(cancellation.Token), Is.True);

                Assert.Throws<AggregateException>(() => bridge.Wait(k_waitMilliseconds));
                Assert.That(bridge.IsCanceled, Is.True);
            }
        }

        [Test]
        public void AsTask_ThenNativeAwait_Throws()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            Task<int> bridge = task.AsTask();

            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().OnCompleted(() => { }));

            Assert.That(source.TrySetResult(1), Is.True);
            Assert.That(bridge.Wait(k_waitMilliseconds), Is.True);
        }

        [Test]
        public void CreateCompleted_ReturnsCompletedTaskBoundToToken()
        {
            OnityAutoResetTaskCompletionSource source =
                OnityAutoResetTaskCompletionSource.CreateCompleted(out int token);
            OnityTask task = source.Task;

            Assert.That(GetToken(task), Is.EqualTo(token));
            Assert.That(task.GetAwaiter().IsCompleted, Is.True);
            Assert.DoesNotThrow(() => task.GetAwaiter().GetResult());
        }

        [Test]
        public void CreateFromResult_ReturnsCompletedTaskBoundToToken()
        {
            OnityAutoResetTaskCompletionSource<int> source =
                OnityAutoResetTaskCompletionSource<int>.CreateFromResult(77, out int token);
            OnityTask<int> task = source.Task;

            Assert.That(GetToken(task), Is.EqualTo(token));
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(77));
        }

        [Test]
        public void CreateFromException_ReturnsFaultedTask()
        {
            InvalidOperationException failure = new InvalidOperationException("created faulted");
            OnityAutoResetTaskCompletionSource untypedSource =
                OnityAutoResetTaskCompletionSource.CreateFromException(failure, out int untypedToken);
            OnityAutoResetTaskCompletionSource<int> typedSource =
                OnityAutoResetTaskCompletionSource<int>.CreateFromException(failure, out int typedToken);

            Assert.That(GetToken(untypedSource.Task), Is.EqualTo(untypedToken));
            Assert.That(GetToken(typedSource.Task), Is.EqualTo(typedToken));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => untypedSource.Task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => typedSource.Task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void CreateFromCanceled_PreservesTokenIdentity()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                OnityAutoResetTaskCompletionSource untypedSource =
                    OnityAutoResetTaskCompletionSource.CreateFromCanceled(cancellation.Token, out int untypedToken);
                OnityAutoResetTaskCompletionSource<int> typedSource =
                    OnityAutoResetTaskCompletionSource<int>.CreateFromCanceled(cancellation.Token, out int typedToken);

                Assert.That(GetToken(untypedSource.Task), Is.EqualTo(untypedToken));
                Assert.That(GetToken(typedSource.Task), Is.EqualTo(typedToken));
                Assert.That(
                    Assert.Throws<OperationCanceledException>(
                        () => untypedSource.Task.GetAwaiter().GetResult()).CancellationToken,
                    Is.EqualTo(cancellation.Token));
                Assert.That(
                    Assert.Throws<OperationCanceledException>(
                        () => typedSource.Task.GetAwaiter().GetResult()).CancellationToken,
                    Is.EqualTo(cancellation.Token));
            }
        }

        [Test]
        public void Typed_CompletionOnAnotherThread_ReachesAwaiter()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTaskAwaiter<int> awaiter = source.Task.GetAwaiter();
            using (ManualResetEventSlim continued = new ManualResetEventSlim(false))
            {
                int result = 0;
                awaiter.OnCompleted(() =>
                {
                    result = awaiter.GetResult();
                    continued.Set();
                });

                Task.Run(() => source.TrySetResult(21));

                Assert.That(continued.Wait(k_waitMilliseconds), Is.True);
                Assert.That(result, Is.EqualTo(21));
            }
        }

        private static int GetToken(OnityTask task)
        {
            FieldInfo field = typeof(OnityTask).GetField("m_token", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)field.GetValue(task);
        }

        private static int GetToken<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_token", BindingFlags.Instance | BindingFlags.NonPublic);
            return (int)field.GetValue(task);
        }
    }
}
