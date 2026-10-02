using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the cancellation-token helpers of <see cref="OnityCancellationTokenExtensions"/>:
    /// context-free registration, task and token conversions, <c>WaitUntilCanceled</c>, disposal on
    /// cancellation, the exception helper and the token comparer.
    /// </summary>
    [TestFixture]
    public sealed class OnityCancellationTokenExtensionsEditModeTests
    {
        private readonly List<Exception> m_published = new List<Exception>();
        private bool m_previousPropagate;

        [SetUp]
        public void SubscribeToTheScheduler()
        {
            m_published.Clear();
            m_previousPropagate = OnityTaskScheduler.PropagateOperationCanceledException;
            OnityTaskScheduler.UnobservedTaskException += Record;
        }

        [TearDown]
        public void RestoreTheScheduler()
        {
            OnityTaskScheduler.UnobservedTaskException -= Record;
            OnityTaskScheduler.PropagateOperationCanceledException = m_previousPropagate;
        }

        private void Record(Exception exception)
        {
            m_published.Add(exception);
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int Disposed;

            public void Dispose()
            {
                Disposed++;
            }
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_RunsTheCallbackOnCancellation()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            int plain = 0;
            object received = null;

            cancellation.Token.RegisterWithoutCaptureExecutionContext(() => plain++);
            cancellation.Token.RegisterWithoutCaptureExecutionContext(state => received = state, "state");
            Assert.That(plain, Is.Zero);
            cancellation.Cancel();

            Assert.That(plain, Is.EqualTo(1));
            Assert.That(received, Is.EqualTo("state"));
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_AlreadyCanceledToken_RunsTheCallbackImmediately()
        {
            int calls = 0;

            new CancellationToken(true).RegisterWithoutCaptureExecutionContext(() => calls++);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_DisposedRegistration_DoesNotRun()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            int calls = 0;

            cancellation.Token.RegisterWithoutCaptureExecutionContext(() => calls++).Dispose();
            cancellation.Cancel();

            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_DoesNotFlowAsyncLocalValues()
        {
            AsyncLocal<string> local = new AsyncLocal<string>();
            local.Value = "caller";
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            string withoutCapture = "unset";
            string withCapture = "unset";
            try
            {
                cancellation.Token.RegisterWithoutCaptureExecutionContext(() => withoutCapture = local.Value);
                cancellation.Token.Register(() => withCapture = local.Value);

                // The canceling worker has no flowed context, so only a captured one can show the value.
                using (ExecutionContext.SuppressFlow())
                {
                    Task.Run(() => cancellation.Cancel()).Wait();
                }

                Assert.That(withCapture, Is.EqualTo("caller"), "A plain registration captures the context.");
                Assert.That(withoutCapture, Is.Null);
                Assert.That(local.Value, Is.EqualTo("caller"));
            }
            finally
            {
                local.Value = null;
            }
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_AlreadySuppressedFlow_IsPreserved()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            using (ExecutionContext.SuppressFlow())
            {
                cancellation.Token.RegisterWithoutCaptureExecutionContext(() => { });
                Assert.That(ExecutionContext.IsFlowSuppressed(), Is.True);
            }

            Assert.That(ExecutionContext.IsFlowSuppressed(), Is.False);
        }

        [Test]
        public void RegisterWithoutCaptureExecutionContext_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => CancellationToken.None.RegisterWithoutCaptureExecutionContext((Action)null));
            Assert.Throws<ArgumentNullException>(
                () => CancellationToken.None.RegisterWithoutCaptureExecutionContext(null, null));
        }

        [Test]
        public void ToCancellationToken_PendingTask_CancelsTheTokenWhenTheTaskSucceeds()
        {
            OnityTaskCompletionSource untypedSource = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();

            CancellationToken untyped = untypedSource.Task.ToCancellationToken();
            CancellationToken typed = typedSource.Task.ToCancellationToken();
            Assert.That(untyped.IsCancellationRequested, Is.False);
            Assert.That(typed.IsCancellationRequested, Is.False);
            untypedSource.TrySetResult();
            typedSource.TrySetResult(1);

            Assert.That(untyped.IsCancellationRequested, Is.True);
            Assert.That(typed.IsCancellationRequested, Is.True);
            Assert.That(m_published, Is.Empty);
        }

        [Test]
        public void ToCancellationToken_FaultedTask_CancelsTheTokenAndPublishesTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("task failed");
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            CancellationToken token = source.Task.ToCancellationToken();
            source.TrySetException(failure);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(m_published, Is.EqualTo(new[] { failure }));
        }

        [Test]
        public void ToCancellationToken_CanceledTask_CancelsTheTokenAndDropsTheCancellation()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            CancellationToken token = source.Task.ToCancellationToken();
            source.TrySetCanceled(cancellation.Token);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(m_published, Is.Empty);
        }

        [Test]
        public void ToCancellationToken_CompletedTask_ReturnsACanceledToken()
        {
            Assert.That(OnityTask.CompletedTask.ToCancellationToken().IsCancellationRequested, Is.True);
            Assert.That(OnityTask.FromResult(1).ToCancellationToken().IsCancellationRequested, Is.True);
        }

        [Test]
        public void ToCancellationToken_CompletedTaskWithALink_ReturnsACanceledToken()
        {
            using CancellationTokenSource link = new CancellationTokenSource();

            CancellationToken untyped = OnityTask.CompletedTask.ToCancellationToken(link.Token);
            CancellationToken typed = OnityTask.FromResult(1).ToCancellationToken(link.Token);

            Assert.That(untyped.IsCancellationRequested, Is.True);
            Assert.That(typed.IsCancellationRequested, Is.True);
            Assert.That(link.IsCancellationRequested, Is.False, "The result must not cancel the linked token.");
            Assert.That(m_published, Is.Empty);
        }

        [Test]
        public void ToCancellationToken_AlreadyFaultedTask_ReturnsACanceledTokenAndPublishesTheFault()
        {
            InvalidOperationException failure = new InvalidOperationException("already failed");

            CancellationToken token = OnityTask.FromException(failure).ToCancellationToken();

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(m_published, Is.EqualTo(new[] { failure }));
        }

        [Test]
        public void ToCancellationToken_ConsumesASingleConsumerTask()
        {
            OnityAutoResetTaskCompletionSource source = OnityAutoResetTaskCompletionSource.Create();
            OnityTask original = source.Task;

            CancellationToken token = original.ToCancellationToken();
            source.TrySetResult();

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.Throws<InvalidOperationException>(() => _ = original.IsCompleted);
        }

        [Test]
        public void ToCancellationToken_WithAlreadyCanceledLink_ReturnsThatTokenAndLeavesTheTask()
        {
            using CancellationTokenSource link = new CancellationTokenSource();
            link.Cancel();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();
            OnityTaskCompletionSource<int> typedSource = new OnityTaskCompletionSource<int>();

            CancellationToken token = source.Task.ToCancellationToken(link.Token);
            CancellationToken typedToken = typedSource.Task.ToCancellationToken(link.Token);

            Assert.That(token, Is.EqualTo(link.Token));
            Assert.That(typedToken, Is.EqualTo(link.Token));
            Assert.That(source.Task.Status, Is.EqualTo(OnityTaskStatus.Pending));
        }

        [Test]
        public void ToCancellationToken_WithALinkThatCannotCancel_BehavesAsThePlainOverload()
        {
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            CancellationToken token = source.Task.ToCancellationToken(CancellationToken.None);
            Assert.That(token.IsCancellationRequested, Is.False);
            source.TrySetResult();

            Assert.That(token.IsCancellationRequested, Is.True);
        }

        [Test]
        public void ToCancellationToken_WithALink_IsCanceledByTheLinkFirst()
        {
            using CancellationTokenSource link = new CancellationTokenSource();
            OnityTaskCompletionSource source = new OnityTaskCompletionSource();

            CancellationToken token = source.Task.ToCancellationToken(link.Token);
            Assert.That(token.IsCancellationRequested, Is.False);
            link.Cancel();
            Assert.That(token.IsCancellationRequested, Is.True);
            source.TrySetResult();

            Assert.That(m_published, Is.Empty);
        }

        [Test]
        public void ToCancellationToken_WithALink_IsCanceledByTheTaskFirst()
        {
            using CancellationTokenSource link = new CancellationTokenSource();
            OnityTaskCompletionSource<int> source = new OnityTaskCompletionSource<int>();

            CancellationToken token = source.Task.ToCancellationToken(link.Token);
            source.TrySetResult(1);

            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(link.IsCancellationRequested, Is.False);
        }

        [Test]
        public void ToOnityTask_Token_CompletesSuccessfullyWhenTheTokenIsCanceledLater()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            (OnityTask task, CancellationTokenRegistration registration) = cancellation.Token.ToOnityTask();
            using (registration)
            {
                Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
                cancellation.Cancel();

                Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Succeeded));
            }
        }

        [Test]
        public void ToOnityTask_Token_AlreadyCanceledToken_ReturnsACanceledTask()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            OnityTask task = cancellation.Token.ToOnityTask().Task;

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Canceled));
            Assert.That(
                Assert.Catch<OperationCanceledException>(() => task.GetAwaiter().GetResult()).CancellationToken,
                Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void ToOnityTask_Token_DisposedRegistration_NeverCompletes()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            (OnityTask task, CancellationTokenRegistration registration) = cancellation.Token.ToOnityTask();
            registration.Dispose();
            cancellation.Cancel();

            Assert.That(task.Status, Is.EqualTo(OnityTaskStatus.Pending));
        }

        [Test]
        public void ToOnityTask_Token_IsShareable()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityTask task = cancellation.Token.ToOnityTask().Task;
            int resumed = 0;

            task.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            task.GetAwaiter().UnsafeOnCompleted(() => resumed++);
            cancellation.Cancel();

            Assert.That(resumed, Is.EqualTo(2));
        }

        [Test]
        public void WaitUntilCanceled_CompletedStates_FollowTheToken()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            Assert.That(CancellationToken.None.WaitUntilCanceled().GetAwaiter().IsCompleted, Is.True,
                "A token that can never be canceled does not wait.");
            Assert.That(cancellation.Token.WaitUntilCanceled().GetAwaiter().IsCompleted, Is.False);
            cancellation.Cancel();
            Assert.That(cancellation.Token.WaitUntilCanceled().GetAwaiter().IsCompleted, Is.True);
        }

        [Test]
        public void WaitUntilCanceled_Continuation_RunsOnCancellationAndNeverThrows()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            OnityCancellationTokenAwaiter awaiter = cancellation.Token.WaitUntilCanceled().GetAwaiter();
            int resumed = 0;

            awaiter.UnsafeOnCompleted(() => resumed++);
            awaiter.OnCompleted(() => resumed++);
            Assert.That(resumed, Is.Zero);
            cancellation.Cancel();

            Assert.That(resumed, Is.EqualTo(2));
            Assert.DoesNotThrow(() => awaiter.GetResult());
        }

        [Test]
        public async Task WaitUntilCanceled_CanBeAwaited()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            Task canceler = Task.Run(() =>
            {
                Thread.Sleep(20);
                cancellation.Cancel();
            });

            await cancellation.Token.WaitUntilCanceled();

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            await canceler;
        }

        [Test]
        public void AddTo_DisposesTheObjectWhenTheTokenIsCanceled()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CountingDisposable disposable = new CountingDisposable();

            disposable.AddTo(cancellation.Token);
            Assert.That(disposable.Disposed, Is.Zero);
            cancellation.Cancel();

            Assert.That(disposable.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_AlreadyCanceledToken_DisposesImmediately()
        {
            CountingDisposable disposable = new CountingDisposable();

            disposable.AddTo(new CancellationToken(true));

            Assert.That(disposable.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_DisposedRegistration_KeepsTheObjectAlive()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            CountingDisposable disposable = new CountingDisposable();

            disposable.AddTo(cancellation.Token).Dispose();
            cancellation.Cancel();

            Assert.That(disposable.Disposed, Is.Zero);
        }

        [Test]
        public void AddTo_NullDisposable_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ((IDisposable)null).AddTo(CancellationToken.None));
        }

        [Test]
        public void IsOperationCanceledException_MatchesCancellationExceptions()
        {
            Assert.That(new OperationCanceledException().IsOperationCanceledException(), Is.True);
            Assert.That(new TaskCanceledException().IsOperationCanceledException(), Is.True);
            Assert.That(new InvalidOperationException().IsOperationCanceledException(), Is.False);
            Assert.That(new TimeoutException().IsOperationCanceledException(), Is.False);
        }

        [Test]
        public void TokenEqualityComparer_ComparesTokensBySource()
        {
            using CancellationTokenSource first = new CancellationTokenSource();
            using CancellationTokenSource second = new CancellationTokenSource();
            IEqualityComparer<CancellationToken> comparer = OnityCancellationTokenEqualityComparer.Default;

            Assert.That(comparer.Equals(first.Token, first.Token), Is.True);
            Assert.That(comparer.Equals(first.Token, second.Token), Is.False);
            Assert.That(comparer.GetHashCode(first.Token), Is.EqualTo(comparer.GetHashCode(first.Token)));
            Assert.That(OnityCancellationTokenEqualityComparer.Default, Is.SameAs(comparer));
        }

        [Test]
        public void TokenEqualityComparer_WorksAsADictionaryComparer()
        {
            using CancellationTokenSource first = new CancellationTokenSource();
            using CancellationTokenSource second = new CancellationTokenSource();
            Dictionary<CancellationToken, string> map =
                new Dictionary<CancellationToken, string>(OnityCancellationTokenEqualityComparer.Default);

            map[first.Token] = "first";
            map[second.Token] = "second";

            Assert.That(map[first.Token], Is.EqualTo("first"));
            Assert.That(map[second.Token], Is.EqualTo("second"));
            Assert.That(map.Count, Is.EqualTo(2));
        }
    }
}
