using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <c>ContinueWith</c>, <c>Unwrap</c> and the null-argument rules of
    /// <see cref="OnityTaskContinuationExtensions"/>.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskContinuationEditModeTests
    {
        [Test]
        public void ContinueWith_TypedAction_RunsAfterTheAntecedentWithItsResult()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            int received = 0;

            OnityTask task = source.Task.ContinueWith(value => { received = value; });
            Assert.That(received, Is.Zero);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult(21);

            Assert.That(received, Is.EqualTo(21));
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void ContinueWith_TypedAsyncAction_WaitsForTheReturnedTask()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource next = new OnityTaskCompletionSource();
            int received = 0;

            OnityTask task = source.Task.ContinueWith(value =>
            {
                received = value;
                return next.Task;
            });
            source.TrySetResult(4);

            Assert.That(received, Is.EqualTo(4));
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            next.TrySetResult();
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void ContinueWith_TypedFunction_ReturnsTheComputedValue()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            OnityTask<string> task = source.Task.ContinueWith(value => "v" + value);
            source.TrySetResult(6);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("v6"));
        }

        [Test]
        public void ContinueWith_TypedAsyncFunction_ReturnsTheNextTasksResult()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<string> next = new OnityTaskCompletionSource<string>();

            OnityTask<string> task = source.Task.ContinueWith(value => next.Task);
            source.TrySetResult(1);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            next.TrySetResult("next");

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo("next"));
        }

        [Test]
        public void ContinueWith_TypedAsyncFunctionLambda_BindsToTheTypedTaskOverload()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            OnityTask<int> task = source.Task.ContinueWith(value => OnityTask.FromResult(value + 1));
            source.TrySetResult(1);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(2));
        }

        [Test]
        public void ContinueWith_UntypedAction_RunsAfterTheAntecedent()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int calls = 0;

            OnityTask task = source.Task.ContinueWith(() => { calls++; });
            Assert.That(calls, Is.Zero);
            source.TrySetResult();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void ContinueWith_UntypedAsyncAction_WaitsForTheReturnedTask()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTaskCompletionSource next = new OnityTaskCompletionSource();

            OnityTask task = source.Task.ContinueWith(() => next.Task);
            source.TrySetResult();
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            next.TrySetResult();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void ContinueWith_UntypedFunction_ReturnsTheComputedValue()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            OnityTask<int> task = source.Task.ContinueWith(() => 99);
            source.TrySetResult();

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(99));
        }

        [Test]
        public void ContinueWith_UntypedAsyncFunction_ReturnsTheNextTasksResult()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> next = new OnityTaskCompletionSource<int>();

            OnityTask<int> task = source.Task.ContinueWith(() => next.Task);
            source.TrySetResult();
            next.TrySetResult(55);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(55));
        }

        [Test]
        public void ContinueWith_FaultedAntecedent_SkipsTheContinuationAndKeepsTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("antecedent failed");
            OnityTaskCompletionSource<int> typed = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource untyped = new OnityTaskCompletionSource();
            int calls = 0;

            OnityTask fromTyped = typed.Task.ContinueWith(value => { calls++; });
            OnityTask fromUntyped = untyped.Task.ContinueWith(() => { calls++; });
            typed.TrySetException(failure);
            untyped.TrySetException(failure);

            Assert.That(calls, Is.Zero);
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromTyped.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromUntyped.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void ContinueWith_CanceledAntecedent_SkipsTheContinuationAndStaysCanceled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            int calls = 0;

            OnityTask task = source.Task.ContinueWith(value => { calls++; });
            source.TrySetCanceled(cancellation.Token);

            Assert.That(calls, Is.Zero);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void ContinueWith_ContinuationThrows_FaultsTheReturnedTask()
        {
            InvalidOperationException failure = new InvalidOperationException("continuation failed");
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            OnityTask task = source.Task.ContinueWith((Action)(() => throw failure));
            source.TrySetResult();

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void ContinueWith_NullContinuation_ThrowsSynchronously()
        {
            OnityTask untyped = OnityTask.CompletedTask;
            OnityTask<int> typed = OnityTask.FromResult(1);

            Assert.Throws<ArgumentNullException>(() => typed.ContinueWith((Action<int>)null));
            Assert.Throws<ArgumentNullException>(() => typed.ContinueWith((Func<int, OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => typed.ContinueWith((Func<int, string>)null));
            Assert.Throws<ArgumentNullException>(() => typed.ContinueWith((Func<int, OnityTask<string>>)null));
            Assert.Throws<ArgumentNullException>(() => untyped.ContinueWith((Action)null));
            Assert.Throws<ArgumentNullException>(() => untyped.ContinueWith((Func<OnityTask>)null));
            Assert.Throws<ArgumentNullException>(() => untyped.ContinueWith((Func<int>)null));
            Assert.Throws<ArgumentNullException>(() => untyped.ContinueWith((Func<OnityTask<int>>)null));
        }

        [Test]
        public void Unwrap_TypedOnityTaskOfOnityTask_ReturnsTheInnerResult()
        {
            OnityTaskCompletionSource<OnityTask<int>> outer = new OnityTaskCompletionSource<OnityTask<int>>();
            OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();

            OnityTask<int> task = outer.Task.Unwrap();
            outer.TrySetResult(inner.Task);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            inner.TrySetResult(12);

            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(12));
        }

        [Test]
        public void Unwrap_UntypedOnityTaskOfOnityTask_CompletesWithTheInnerTask()
        {
            OnityTaskCompletionSource<OnityTask> outer = new OnityTaskCompletionSource<OnityTask>();
            OnityTaskCompletionSource inner = new OnityTaskCompletionSource();

            OnityTask task = outer.Task.Unwrap();
            outer.TrySetResult(inner.Task);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            inner.TrySetResult();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void Unwrap_FaultedInnerTask_PropagatesTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("inner failed");
            OnityTask<OnityTask<int>> outer = OnityTask.FromResult(OnityTask.FromException<int>(failure));

            OnityTask<int> task = outer.Unwrap();

            Assert.That(
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Unwrap_DotNetTaskOfTypedOnityTask_ReturnsTheInnerResult()
        {
            Task<OnityTask<int>> completed = Task.FromResult(OnityTask.FromResult(5));
            Task<OnityTask<int>> completedAgain = Task.FromResult(OnityTask.FromResult(6));

            Assert.That(completed.Unwrap().GetAwaiter().GetResult(), Is.EqualTo(5));
            Assert.That(completedAgain.Unwrap(false).GetAwaiter().GetResult(), Is.EqualTo(6));
        }

        [Test]
        public void Unwrap_PendingDotNetTaskWithoutContextCapture_CompletesWhenBothTasksDo()
        {
            TaskCompletionSource<OnityTask<int>> outer = new TaskCompletionSource<OnityTask<int>>();
            OnityTaskCompletionSource<int> inner = new OnityTaskCompletionSource<int>();

            OnityTask<int> task = outer.Task.Unwrap(false);
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            outer.SetResult(inner.Task);
            inner.TrySetResult(31);

            // The .NET continuation of the outer await may be queued to the thread pool.
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, 5000), Is.True);
            Assert.That(task.GetAwaiter().GetResult(), Is.EqualTo(31));
        }

        [Test]
        public void Unwrap_DotNetTaskOfUntypedOnityTask_CompletesWithTheInnerTask()
        {
            Task<OnityTask> completed = Task.FromResult(OnityTask.CompletedTask);

            Assert.That(completed.Unwrap().Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(completed.Unwrap(false).Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void Unwrap_OnityTaskOfDotNetTask_ReturnsTheInnerResult()
        {
            OnityTask<Task<int>> outer = OnityTask.FromResult(Task.FromResult(8));

            Assert.That(outer.Unwrap().GetAwaiter().GetResult(), Is.EqualTo(8));
            Assert.That(outer.Unwrap(false).GetAwaiter().GetResult(), Is.EqualTo(8));
        }

        [Test]
        public void Unwrap_OnityTaskOfUntypedDotNetTask_CompletesWithTheInnerTask()
        {
            OnityTask<Task> outer = OnityTask.FromResult((Task)Task.CompletedTask);

            Assert.That(outer.Unwrap().Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(outer.Unwrap(false).Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void Unwrap_NullDotNetTask_ThrowsSynchronously()
        {
            Assert.Throws<ArgumentNullException>(() => ((Task<OnityTask<int>>)null).Unwrap());
            Assert.Throws<ArgumentNullException>(() => ((Task<OnityTask<int>>)null).Unwrap(true));
            Assert.Throws<ArgumentNullException>(() => ((Task<OnityTask>)null).Unwrap());
            Assert.Throws<ArgumentNullException>(() => ((Task<OnityTask>)null).Unwrap(true));
        }
    }
}
