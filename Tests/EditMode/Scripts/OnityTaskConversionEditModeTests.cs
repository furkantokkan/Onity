using System;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the public <c>Status</c>, <c>ToString</c> and conversion members of
    /// <see cref="OnityTask"/> and <see cref="OnityTask{T}"/>, and the
    /// <see cref="OnityTaskStatusExtensions"/> predicates.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskConversionEditModeTests
    {
        [Test]
        public void Status_UntypedTask_ReportsEveryState()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityTaskCompletionSource pending = new OnityTaskCompletionSource();
            OnityTaskCompletionSource faulted = new OnityTaskCompletionSource();
            OnityTaskCompletionSource canceled = new OnityTaskCompletionSource();
            faulted.TrySetException(failure);
            canceled.TrySetCanceled(cancellation.Token);

            Assert.That(pending.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            Assert.That(OnityTask.CompletedTask.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(default(OnityTask).Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(faulted.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(canceled.Task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                OnityTask.FromException(new InvalidOperationException()).Status,
                Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(OnityTask.FromCanceled(cancellation.Token).Status, Is.EqualTo(OnityTaskStatus.Canceled));

            // Reading the status does not observe a fault, and a fault that nobody observes is
            // published when its source is collected, into whichever test runs by then.
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => faulted.Task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Status_TypedTask_ReportsEveryState()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityTaskCompletionSource<int> pending = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> succeeded = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> faulted = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> canceled = new OnityTaskCompletionSource<int>();
            succeeded.TrySetResult(1);
            faulted.TrySetException(failure);
            canceled.TrySetCanceled(cancellation.Token);

            Assert.That(pending.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
            Assert.That(succeeded.Task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(OnityTask.FromResult(2).Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(faulted.Task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(canceled.Task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                OnityTask.FromException<int>(new InvalidOperationException()).Status,
                Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                OnityTask.FromCanceled<int>(cancellation.Token).Status,
                Is.EqualTo(OnityTaskStatus.Canceled));

            // Reading the status does not observe a fault, and a fault that nobody observes is
            // published when its source is collected, into whichever test runs by then.
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => faulted.Task.GetAwaiter().GetResult()),
                Is.SameAs(failure));
        }

        [Test]
        public void Status_ConsumedPooledTask_ThrowsLikeAnyOtherMember()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;
            source.TrySetResult();
            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded), "Reading the status must not consume.");

            task.GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => _ = task.Status);
        }

        [Test]
        public void ToString_ReportsTheStatusInParentheses()
        {
            OnityTaskCompletionSource pending = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> typed = new OnityTaskCompletionSource<int>();

            Assert.That(pending.Task.ToString(), Is.EqualTo("(Pending)"));
            Assert.That(typed.Task.ToString(), Is.EqualTo("(Pending)"));
            Assert.That(OnityTask.CompletedTask.ToString(), Is.EqualTo("(Succeeded)"));
            Assert.That(OnityTask.FromResult(5).ToString(), Is.EqualTo("(Succeeded)"));
            Assert.That(OnityTask.FromException(new Exception()).ToString(), Is.EqualTo("(Faulted)"));
            Assert.That(OnityTask.FromCanceled<int>(new CancellationToken(true)).ToString(), Is.EqualTo("(Canceled)"));
        }

        [Test]
        public void ToString_ConsumedPooledTask_DoesNotThrow()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> task = source.Task;
            source.TrySetResult(1);
            task.GetAwaiter().GetResult();

            Assert.That(task.ToString(), Is.EqualTo("(Consumed)"));
        }

        [Test]
        public void StatusExtensions_MatchTheirStatus()
        {
            Assert.That(OnityTaskStatus.Pending.IsCompleted(), Is.False);
            Assert.That(OnityTaskStatus.Succeeded.IsCompleted(), Is.True);
            Assert.That(OnityTaskStatus.Faulted.IsCompleted(), Is.True);
            Assert.That(OnityTaskStatus.Canceled.IsCompleted(), Is.True);

            Assert.That(OnityTaskStatus.Succeeded.IsCompletedSuccessfully(), Is.True);
            Assert.That(OnityTaskStatus.Pending.IsCompletedSuccessfully(), Is.False);
            Assert.That(OnityTaskStatus.Faulted.IsCompletedSuccessfully(), Is.False);

            Assert.That(OnityTaskStatus.Canceled.IsCanceled(), Is.True);
            Assert.That(OnityTaskStatus.Faulted.IsCanceled(), Is.False);

            Assert.That(OnityTaskStatus.Faulted.IsFaulted(), Is.True);
            Assert.That(OnityTaskStatus.Canceled.IsFaulted(), Is.False);
        }

        [Test]
        public void Status_NumericValuesMatchTheValueTaskSourceStatus()
        {
            Assert.That((int)OnityTaskStatus.Pending, Is.EqualTo((int)System.Threading.Tasks.Sources.ValueTaskSourceStatus.Pending));
            Assert.That((int)OnityTaskStatus.Succeeded, Is.EqualTo((int)System.Threading.Tasks.Sources.ValueTaskSourceStatus.Succeeded));
            Assert.That((int)OnityTaskStatus.Faulted, Is.EqualTo((int)System.Threading.Tasks.Sources.ValueTaskSourceStatus.Faulted));
            Assert.That((int)OnityTaskStatus.Canceled, Is.EqualTo((int)System.Threading.Tasks.Sources.ValueTaskSourceStatus.Canceled));
        }

        [Test]
        public void AsUnitTask_CompletedTask_ReturnsACompletedUnitTask()
        {
            OnityTask<Unit> unit = OnityTask.CompletedTask.AsUnitTask();

            Assert.That(unit.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(unit.GetAwaiter().GetResult(), Is.EqualTo(Unit.Default));
        }

        [Test]
        public void AsUnitTask_PendingTask_CompletesWhenTheOriginalDoes()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            OnityTask<Unit> unit = source.Task.AsUnitTask();
            Assert.That(unit.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult();

            Assert.That(unit.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void AsUnitTask_FaultedAndCanceledTasks_KeepTheirOutcome()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityTaskCompletionSource faulted = new OnityTaskCompletionSource();
            OnityTaskCompletionSource canceled = new OnityTaskCompletionSource();

            OnityTask<Unit> fromFaulted = faulted.Task.AsUnitTask();
            OnityTask<Unit> fromCanceled = canceled.Task.AsUnitTask();
            faulted.TrySetException(failure);
            canceled.TrySetCanceled(cancellation.Token);

            Assert.That(fromFaulted.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromFaulted.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(fromCanceled.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => fromCanceled.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void AsUnitTask_ConsumesASingleConsumerOriginal()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask original = source.Task;

            OnityTask<Unit> unit = original.AsUnitTask();
            source.TrySetResult();

            Assert.That(unit.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.Throws<InvalidOperationException>(() => _ = original.IsCompleted,
                "The conversion must consume the pooled original.");
        }

        [Test]
        public void AsOnityTask_CompletedTypedTask_ReturnsACompletedUntypedTask()
        {
            OnityTask untyped = OnityTask.FromResult(3).AsOnityTask();

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            untyped.GetAwaiter().GetResult();
        }

        [Test]
        public void AsOnityTask_PendingTypedTask_CompletesWhenTheOriginalDoes()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            OnityTask untyped = source.Task.AsOnityTask();
            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Pending));
            source.TrySetResult(10);

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
        }

        [Test]
        public void AsOnityTask_FaultedAndCanceledTypedTasks_KeepTheirOutcome()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            InvalidOperationException failure = new InvalidOperationException("failed");
            OnityTaskCompletionSource<int> faulted = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> canceled = new OnityTaskCompletionSource<int>();

            OnityTask fromFaulted = faulted.Task.AsOnityTask();
            OnityTask fromCanceled = canceled.Task.AsOnityTask();
            faulted.TrySetException(failure);
            canceled.TrySetCanceled(cancellation.Token);

            Assert.That(fromFaulted.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(
                Assert.Throws<InvalidOperationException>(() => fromFaulted.GetAwaiter().GetResult()),
                Is.SameAs(failure));
            Assert.That(fromCanceled.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => fromCanceled.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void AsOnityTask_ConsumesASingleConsumerOriginal()
        {
            OnityAutoResetTaskCompletionSource<int> source = OnityAutoResetTaskCompletionSource<int>.Create();
            OnityTask<int> original = source.Task;

            OnityTask untyped = original.AsOnityTask();
            source.TrySetResult(1);

            Assert.That(untyped.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.Throws<InvalidOperationException>(() => _ = original.IsCompleted,
                "The conversion must consume the pooled original.");
        }
    }
}
