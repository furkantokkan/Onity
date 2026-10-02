using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the conversions between <see cref="OnityTask"/> and <see cref="ValueTask"/>, and from
    /// .NET tasks to Onity tasks, including the SynchronizationContext choice of
    /// <c>Task.AsOnityTask</c>.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskValueTaskEditModeTests
    {
        private const int k_timeoutMilliseconds = 10000;

        [Test]
        public void AsValueTask_CompletedTask_ReturnsACompletedValueTask()
        {
            ValueTask untyped = OnityTask.CompletedTask.AsValueTask();
            ValueTask<int> typed = OnityTask.FromResult(7).AsValueTask();

            Assert.That(untyped.IsCompletedSuccessfully, Is.True);
            Assert.That(typed.IsCompletedSuccessfully, Is.True);
            Assert.That(typed.Result, Is.EqualTo(7));
        }

        [Test]
        public void AsValueTask_PendingTask_CompletesWhenTheOnityTaskDoes()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int resumed = 0;

            ValueTask valueTask = source.Task.AsValueTask();
            Assert.That(valueTask.IsCompleted, Is.False);
            valueTask.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            Assert.That(resumed, Is.Zero);
            source.TrySetResult();

            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(valueTask.IsCompletedSuccessfully, Is.True);
            valueTask.GetAwaiter().GetResult();
        }

        [Test]
        public void AsValueTask_PendingTypedTask_CompletesWithTheResult()
        {
            OnityTaskCompletionSource<string> source = new OnityTaskCompletionSource<string>();
            int resumed = 0;

            ValueTask<string> valueTask = source.Task.AsValueTask();
            Assert.That(valueTask.IsCompleted, Is.False);
            valueTask.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            source.TrySetResult("done");

            Assert.That(resumed, Is.EqualTo(1));
            Assert.That(valueTask.GetAwaiter().GetResult(), Is.EqualTo("done"));
        }

        [Test]
        public void AsValueTask_FaultedAndCanceledTasks_KeepTheirOutcome()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityTaskCompletionSource faulted = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> canceled = new OnityTaskCompletionSource<int>();
            faulted.TrySetException(failure);
            canceled.TrySetCanceled(cancellation.Token);

            ValueTask faultedValue = faulted.Task.AsValueTask();
            ValueTask<int> canceledValue = canceled.Task.AsValueTask();

            Assert.That(faultedValue.IsFaulted, Is.True);
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => faultedValue.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(canceledValue.IsCanceled, Is.True);
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => canceledValue.GetAwaiter().GetResult())
                    .CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void ImplicitConversion_ConvertsTasksToValueTasks()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();

            ValueTask valueTask = source.Task;
            ValueTask<int> typedValueTask = typedSource.Task;
            Assert.That(valueTask.IsCompleted, Is.False);
            Assert.That(typedValueTask.IsCompleted, Is.False);
            source.TrySetResult();
            typedSource.TrySetResult(3);

            Assert.That(valueTask.IsCompletedSuccessfully, Is.True);
            Assert.That(typedValueTask.Result, Is.EqualTo(3));
        }

        [Test]
        public void AsValueTask_ConsumesASingleConsumerOnityTask()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask original = source.Task;
            ValueTask valueTask = original.AsValueTask();

            source.TrySetResult();
            valueTask.GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => _ = original.IsCompleted);
        }

        [Test]
        public async Task AsOnityTask_CompletedValueTasks_Complete()
        {
            await default(ValueTask).AsOnityTask();
            int result = await new ValueTask<int>(5).AsOnityTask();

            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public async Task AsOnityTask_PendingValueTask_CompletesWhenTheValueTaskDoes()
        {
            TaskCompletionSource<int> source = new TaskCompletionSource<int>();

            OnityTask<int> converted = new ValueTask<int>(source.Task).AsOnityTask();
            Assert.That(converted.IsCompleted, Is.False);
            source.SetResult(14);

            Assert.That(await converted, Is.EqualTo(14));
        }

        [Test]
        public async Task AsOnityTask_FaultedValueTask_KeepsTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("failed");
            TaskCompletionSource<bool> source = new TaskCompletionSource<bool>();

            OnityTask converted = new ValueTask(source.Task).AsOnityTask();
            source.SetException(failure);

            InvalidOperationException thrown = null;
            try
            {
                await converted;
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            Assert.That(thrown, Is.SameAs(failure));
        }

        [Test]
        public void AsOnityTask_CompletedDotNetTask_ReturnsACompletedTask()
        {
            OnityTask untyped = Task.CompletedTask.AsOnityTask();
            OnityTask<int> typed = Task.FromResult(9).AsOnityTask();

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(9));
        }

        [Test]
        public void AsOnityTask_FaultedDotNetTask_ReportsTheFirstInnerException()
        {
            InvalidOperationException failure = new InvalidOperationException("first");
            Task faulted = Task.FromException(failure);
            Task<int> faultedTyped = Task.FromException<int>(failure);

            OnityTask untyped = faulted.AsOnityTask(false);
            OnityTask<int> typed = faultedTyped.AsOnityTask(false);

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(typed.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => untyped.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => typed.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void AsOnityTask_CanceledDotNetTask_KeepsTheCancellationToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Task canceled = Task.FromCanceled(cancellation.Token);
            Task<int> canceledTyped = Task.FromCanceled<int>(cancellation.Token);

            OnityTask untyped = canceled.AsOnityTask(false);
            OnityTask<int> typed = canceledTyped.AsOnityTask(false);

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(typed.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => untyped.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => typed.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void AsOnityTask_PendingDotNetTaskWithoutContext_CompletesOnTheCompletingThread()
        {
            TaskCompletionSource<int> source = new TaskCompletionSource<int>();
            using ManualResetEventSlim completed = new ManualResetEventSlim();
            int completionThread = 0;

            OnityTask<int> task = source.Task.AsOnityTask(false);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            task.GetAwaiter().UnsafeOnCompleted(() =>
            {
                completionThread = Thread.CurrentThread.ManagedThreadId;
                completed.Set();
            });
            int completer = 0;
            Task.Run(() =>
            {
                completer = Thread.CurrentThread.ManagedThreadId;
                source.SetResult(4);
            });

            Assert.That(completed.Wait(k_timeoutMilliseconds), Is.True);
            Assert.That(completionThread, Is.EqualTo(completer),
                "Without a context the Onity task completes on the thread that completes the .NET task.");
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(4));
        }

        [Test]
        public async Task AsOnityTask_PendingDotNetTaskWithTheCurrentContext_CompletesOnThatContext()
        {
            Assume.That(SynchronizationContext.Current, Is.Not.Null, "The test runner has no SynchronizationContext.");
            int contextThread = Thread.CurrentThread.ManagedThreadId;
            TaskCompletionSource<int> source = new TaskCompletionSource<int>();
            int completionThread = 0;

            OnityTask<int> task = source.Task.AsOnityTask(true);
            task.GetAwaiter().UnsafeOnCompleted(() => completionThread = Thread.CurrentThread.ManagedThreadId);
            await Task.Run(() => source.SetResult(6)).ConfigureAwait(true);
            int result = await task;

            Assert.That(result, Is.EqualTo(6));
            Assert.That(completionThread, Is.EqualTo(contextThread),
                "With the current context the Onity task completes on the context that was current at the call.");
        }

        [Test]
        public void AsOnityTask_NullDotNetTask_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ((Task)null).AsOnityTask());
            Assert.Throws<ArgumentNullException>(() => ((Task<int>)null).AsOnityTask());
        }
    }
}
