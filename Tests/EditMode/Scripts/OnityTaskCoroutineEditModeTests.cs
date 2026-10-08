using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the parts of the coroutine interop that need no PlayerLoop: the coroutine enumerators
    /// of tasks and the argument and completion rules of the enumerator-to-task conversions.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskCoroutineEditModeTests
    {
        private static IEnumerator EmptyEnumerator()
        {
            yield break;
        }

        private static IEnumerator FailingEnumerator(Exception exception)
        {
            if (exception != null)
            {
                throw exception;
            }

            yield break;
        }

        private sealed class CountingEnumerator : IEnumerator
        {
            public int MoveNextCalls;

            public object Current => null;

            public bool MoveNext()
            {
                MoveNextCalls++;
                return false;
            }

            public void Reset()
            {
            }
        }

        [Test]
        public void ToCoroutine_Untyped_StartsOnFirstMoveNextAndEndsWhenTheTaskCompletes()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            IEnumerator coroutine = source.Task.ToCoroutine();

            Assert.That(coroutine.MoveNext(), Is.True);
            Assert.That(coroutine.Current, Is.Null);
            Assert.That(coroutine.MoveNext(), Is.True);
            source.TrySetResult();

            Assert.That(coroutine.MoveNext(), Is.False);
        }

        [Test]
        public void ToCoroutine_Untyped_CompletedTaskEndsAtTheFirstMoveNext()
        {
            Assert.That(OnityTask.CompletedTask.ToCoroutine().MoveNext(), Is.False);
        }

        [Test]
        public void ToCoroutine_Untyped_DoesNotTouchTheTaskBeforeTheFirstMoveNext()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask task = source.Task;

            IEnumerator coroutine = task.ToCoroutine();

            Assert.That(task.IsCompleted, Is.False, "Creating the enumerator must not consume the task.");
            source.TrySetResult();
            Assert.That(coroutine.MoveNext(), Is.False);
        }

        [Test]
        public void ToCoroutine_Untyped_FailureGoesToTheHandler()
        {
            InvalidOperationException failure = new InvalidOperationException("task failed");
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Exception handled = null;
            IEnumerator coroutine = source.Task.ToCoroutine(exception => handled = exception);

            Assert.That(coroutine.MoveNext(), Is.True);
            source.TrySetException(failure);

            Assert.That(coroutine.MoveNext(), Is.False);
            Assert.That(handled, Is.SameAs(failure));
        }

        [Test]
        public void ToCoroutine_Untyped_FailureWithoutHandlerIsRethrownFromMoveNext()
        {
            InvalidOperationException failure = new InvalidOperationException("task failed");
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            IEnumerator coroutine = source.Task.ToCoroutine();

            Assert.That(coroutine.MoveNext(), Is.True);
            source.TrySetException(failure);

            Assert.That(Assert.Throws<InvalidOperationException>(() => coroutine.MoveNext()), Is.SameAs(failure));
        }

        [Test]
        public void ToCoroutine_Untyped_CancellationGoesToTheHandler()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            Exception handled = null;
            IEnumerator coroutine = source.Task.ToCoroutine(exception => handled = exception);

            Assert.That(coroutine.MoveNext(), Is.True);
            source.TrySetCanceled(cancellation.Token);

            Assert.That(coroutine.MoveNext(), Is.False);
            Assert.That(handled, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ToCoroutine_Typed_HandsOverTheResult()
        {
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();
            int received = 0;
            IEnumerator coroutine = source.Task.ToCoroutine(value => received = value);

            Assert.That(coroutine.MoveNext(), Is.True);
            Assert.That(coroutine.Current, Is.Null);
            source.TrySetResult(77);

            Assert.That(coroutine.MoveNext(), Is.False);
            Assert.That(received, Is.EqualTo(77));
            Assert.That(coroutine.Current, Is.EqualTo(77));
        }

        [Test]
        public void ToCoroutine_Typed_FailureGoesToTheHandlerOrIsRethrown()
        {
            InvalidOperationException failure = new InvalidOperationException("typed failed");
            OnityTaskCompletionSource<int> handledSource = new OnityTaskCompletionSource<int>();
            OnityTaskCompletionSource<int> thrownSource = new OnityTaskCompletionSource<int>();
            Exception handled = null;
            IEnumerator withHandler = handledSource.Task.ToCoroutine(null, exception => handled = exception);
            IEnumerator withoutHandler = thrownSource.Task.ToCoroutine();

            Assert.That(withHandler.MoveNext(), Is.True);
            Assert.That(withoutHandler.MoveNext(), Is.True);
            handledSource.TrySetException(failure);
            thrownSource.TrySetException(failure);

            Assert.That(withHandler.MoveNext(), Is.False);
            Assert.That(handled, Is.SameAs(failure));
            Assert.That(Assert.Throws<InvalidOperationException>(() => withoutHandler.MoveNext()), Is.SameAs(failure));
        }

        [Test]
        public void ToCoroutine_Static_CallsTheFactoryAtOnceAndAwaitsTheTaskLater()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            int factoryCalls = 0;

            IEnumerator coroutine = OnityTask.ToCoroutine(() =>
            {
                factoryCalls++;
                return source.Task;
            });

            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(coroutine.MoveNext(), Is.True);
            Assert.That(coroutine.MoveNext(), Is.True);
            Assert.That(factoryCalls, Is.EqualTo(1));
            source.TrySetResult();
            Assert.That(coroutine.MoveNext(), Is.False);
        }

        [Test]
        public void ToCoroutine_Static_NullFactory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityTask.ToCoroutine(null));
        }

        [Test]
        public void ToOnityTask_Enumerator_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => ((IEnumerator)null).ToOnityTask());
            Assert.Throws<ArgumentNullException>(() => ((IEnumerator)null).WithCancellation(CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => ((IEnumerator)null).GetAwaiter());
            Assert.Throws<ArgumentNullException>(() => EmptyEnumerator().ToOnityTask(null));
            Assert.Throws<ArgumentNullException>(() => ((IEnumerator)null).ToOnityTask(null));
        }

        [Test]
        public void ToOnityTask_EnumeratorWithoutYields_CompletesImmediately()
        {
            Assert.That(EmptyEnumerator().ToOnityTask().Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(EmptyEnumerator().WithCancellation(CancellationToken.None).Status,
                Is.EqualTo(OnityTaskStatus.Succeeded));
            Assert.That(EmptyEnumerator().GetAwaiter().IsCompleted, Is.True);
        }

        [Test]
        public void ToOnityTask_EnumeratorThatThrows_FaultsTheTask()
        {
            InvalidOperationException failure = new InvalidOperationException("coroutine failed");

            OnityTask task = FailingEnumerator(failure).ToOnityTask();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Faulted));
            Assert.That(Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()), Is.SameAs(failure));
        }

        [Test]
        public void ToOnityTask_PreCanceledToken_CancelsBeforeAdvancingTheEnumerator()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            CountingEnumerator first = new CountingEnumerator();
            CountingEnumerator second = new CountingEnumerator();

            OnityTask byToOnityTask = first.ToOnityTask(OnityPlayerLoopTiming.Update, cancellation.Token);
            OnityTask byWithCancellation = second.WithCancellation(cancellation.Token);

            Assert.That(byToOnityTask.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(byWithCancellation.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(first.MoveNextCalls, Is.Zero);
            Assert.That(second.MoveNextCalls, Is.Zero);
        }

        [Test]
        public async Task GetAwaiter_AllowsAwaitingAnEnumeratorDirectly()
        {
            CountingEnumerator enumerator = new CountingEnumerator();

            await enumerator;

            Assert.That(enumerator.MoveNextCalls, Is.EqualTo(1));
        }

        [Test]
        public void StartAsyncCoroutine_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(
                () => ((UnityEngine.MonoBehaviour)null).StartAsyncCoroutine(_ => OnityTask.CompletedTask));
        }
    }
}
