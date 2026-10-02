using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityAsyncEnumerableAdapterEditModeTests
    {
        private static readonly object s_domainSentinel = new object();

        [Test]
        public void NullAdaptersValidateSynchronously_AndCreationAndIdleDisposalAreInert()
        {
            IAsyncEnumerable<int> bcl = null;
            IOnityAsyncEnumerable<int> native = null;
            Assert.Throws<ArgumentNullException>(() => bcl.AsOnityAsyncEnumerable());
            Assert.Throws<ArgumentNullException>(() => native.AsAsyncEnumerable());
            var input = new BclProbe();
            var imported = input.AsOnityAsyncEnumerable().GetAsyncEnumerator();
            Assert.That(input.Acquires, Is.Zero);
            Read(imported.DisposeAsync());
            Assert.That(input.Acquires, Is.Zero);
            var output = new NativeProbe();
            var exported = output.AsAsyncEnumerable().GetAsyncEnumerator();
            Assert.That(output.Acquires, Is.Zero);
            Read(exported.DisposeAsync());
            Assert.That(output.Acquires, Is.Zero);
            var idle = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator();
            Read(idle.DisposeAsync());
            Assert.That(Read(idle.MoveNextAsync()), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EveryUpdate_RejectsEditOrWorkerExecution_EvenPrecanceled(bool worker)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var iterator = OnityAsyncEnumerable.EveryUpdate().GetAsyncEnumerator(cancellation.Token);
                try
                {
                    if (worker)
                    {
                        Task<Exception> result = Task.Run(() => Catch(() => Read(iterator.MoveNextAsync())));
                        Join(result);
                        Assert.That(result.GetAwaiter().GetResult(), Is.TypeOf<InvalidOperationException>());
                    }
                    else
                    {
                        var move = iterator.MoveNextAsync();
                        Assert.That(move.IsFaulted, Is.True);
                        Assert.That(move.IsCanceled, Is.False);
                        Assert.That(Catch(() => Read(move)), Is.TypeOf<InvalidOperationException>());
                    }
                }
                finally
                {
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualAwaitForeach_AdaptersBreakOrThrow_AndCovariance(bool throwBody)
        {
            var fault = new InvalidOperationException("adapter body");
            var bcl = new BclProbe();
            var imported = ConsumeNative(bcl.AsOnityAsyncEnumerable(), throwBody, fault);
            Assert.That(imported.IsCompleted, Is.True);
            CheckBody(imported, throwBody, fault);
            Assert.That(bcl.Disposals, Is.EqualTo(1));
            var native = new NativeProbe();
            Task<int> exported = ConsumeBcl(native.AsAsyncEnumerable(), throwBody, fault);
            Join(exported);
            if (throwBody)
            {
                Assert.That(Catch(() => exported.GetAwaiter().GetResult()), Is.SameAs(fault));
            }
            else
            {
                Assert.That(exported.GetAwaiter().GetResult(), Is.EqualTo(37));
            }
            Assert.That(native.Disposals, Is.EqualTo(1));
            IAsyncEnumerable<object> covariance = OnityAsyncEnumerable.Return("BCL covariance").AsAsyncEnumerable();
            Assert.That(Read(covariance.AsOnityAsyncEnumerable().FirstAsync()), Is.EqualTo("BCL covariance"));
            var reusable = OnityAsyncEnumerable.Range(4, 2).AsAsyncEnumerable().AsOnityAsyncEnumerable();
            Assert.That(Read(reusable.ToArrayAsync()), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(Read(reusable.ToArrayAsync()), Is.EqualTo(new[] { 4, 5 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealCompilerIterator_PendingMoveIsConsumedBeforeDisposal_AndCallerTokenIsOwnedExternally(bool canceled)
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int finallyCalls = 0;
            using (var caller = new CancellationTokenSource())
            {
                var iterator = CompilerIterator(release.Task, () => finallyCalls++).AsOnityAsyncEnumerable()
                    .GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                if (canceled)
                {
                    caller.Cancel();
                }
                var disposal = iterator.DisposeAsync();
                try
                {
                    Assert.That(Read(move), Is.False);
                    Assert.That(disposal.IsCompleted, Is.False, "Uncooperative compiler Move is still outstanding.");
                    Assert.That(finallyCalls, Is.Zero);
                    release.TrySetResult(true);
                    Wait(() => disposal.IsCompleted);
                    Read(disposal);
                    Assert.That(finallyCalls, Is.EqualTo(1));
                    Assert.That(caller.IsCancellationRequested, Is.EqualTo(canceled));
                    Assert.DoesNotThrow(() => caller.Cancel());
                }
                finally
                {
                    release.TrySetResult(true);
                    Wait(() => disposal.IsCompleted);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReusableValueTask_StatusIsReadBeforeGetResult_AndVersionsAreConsumedExactlyOnce(bool canceled)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var source = new ValueSource();
                var probe = new BclProbe { Move = () => source.MoveTask };
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                var fault = new OperationCanceledException("real faulted OCE", cancellation.Token);
                try
                {
                    source.Complete(canceled ? ValueTaskSourceStatus.Canceled : ValueTaskSourceStatus.Faulted, fault);
                    Assert.That(move.IsCanceled, Is.EqualTo(canceled));
                    Assert.That(move.IsFaulted, Is.EqualTo(!canceled));
                    Exception firstError = Catch(() => Read(move));
                    if (canceled)
                    {
                        Assert.That(firstError, Is.InstanceOf<OperationCanceledException>());
                        Assert.That(((OperationCanceledException)firstError).CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                    else
                    {
                        Assert.That(firstError, Is.SameAs(fault));
                    }
                    Assert.That(source.Results, Is.EqualTo(1));
                    Assert.That(source.Flags, Is.EqualTo(ValueTaskSourceOnCompletedFlags.None));
                    var again = iterator.MoveNextAsync();
                    Assert.That(again.IsCanceled, Is.EqualTo(canceled));
                    Exception stickyError = Catch(() => Read(again));
                    if (canceled)
                    {
                        Assert.That(stickyError, Is.InstanceOf<OperationCanceledException>());
                        Assert.That(((OperationCanceledException)stickyError).CancellationToken, Is.EqualTo(cancellation.Token));
                    }
                    else
                    {
                        Assert.That(stickyError, Is.SameAs(fault));
                    }
                    Assert.That(probe.Moves, Is.EqualTo(1));
                    Assert.That(probe.Disposals, Is.EqualTo(1));
                }
                finally
                {
                    source.Complete();
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void ReusableValueTask_TwoSuccessfulVersions_AreConsumedOncePerMove()
        {
            var source = new ValueSource();
            var probe = new BclProbe { Move = () => source.MoveTask };
            var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
            try
            {
                for (int version = 0; version < 2; version++)
                {
                    if (version != 0)
                    {
                        source.Reset();
                    }
                    var move = iterator.MoveNextAsync();
                    Assert.That(move.IsCompleted, Is.False);
                    source.Complete();
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(37));
                    Assert.That(source.Results, Is.EqualTo(version + 1));
                    Assert.That(source.Registrations, Is.EqualTo(version + 1));
                }
            }
            finally
            {
                source.Complete();
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BclImport_MoveOrCleanupRegistrationReturnPinsDisposal(bool cleanupRegistration)
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var source = new ValueSource { CompleteInRegistration = true, RegistrationEntered = entered,
                    RegistrationRelease = release };
                var probe = new BclProbe();
                if (cleanupRegistration)
                {
                    probe.Cleanup = () => source.CleanupTask;
                }
                else
                {
                    probe.Move = () => source.MoveTask;
                }
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                OnityTask<bool> move = default;
                OnityTask dispose = default;
                if (cleanupRegistration)
                {
                    Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                }
                Task worker = Task.Run(() =>
                {
                    if (cleanupRegistration)
                    {
                        dispose = iterator.DisposeAsync();
                    }
                    else
                    {
                        move = iterator.MoveNextAsync();
                    }
                });
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(source.Results, Is.EqualTo(1), "The callback already consumed its terminal ValueTask.");
                    var shared = iterator.DisposeAsync();
                    Assert.That(shared.IsCompleted, Is.False, "Registration has not returned.");
                    Assert.That(probe.Disposals, Is.EqualTo(cleanupRegistration ? 1 : 0));
                    release.Set();
                    Join(worker);
                    worker.GetAwaiter().GetResult();
                    Wait(() => shared.IsCompleted);
                    Read(shared);
                    if (!cleanupRegistration)
                    {
                        Assert.That(Read(move), Is.False);
                    }
                    Assert.That(source.TimedOut, Is.False);
                    Assert.That(probe.Disposals, Is.EqualTo(1));
                    Assert.That(source.Results, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    Join(worker);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void PostCancelPendingStatusRead_ConcurrentCallbackIsRetriedWithoutLostWakeup()
        {
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var source = new ValueSource { StatusEntered = entered, StatusRelease = release };
                var probe = new BclProbe { Move = () => source.MoveTask };
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                source.BlockNextPendingStatus = true;
                OnityTask disposal = default;
                Task worker = Task.Run(() => { disposal = iterator.DisposeAsync(); });
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    source.Complete();
                    Assert.That(source.Results, Is.Zero, "The pending status-read pin is still held.");
                    release.Set();
                    Join(worker);
                    worker.GetAwaiter().GetResult();
                    Wait(() => disposal.IsCompleted);
                    Assert.That(Read(move), Is.False);
                    Read(disposal);
                    Assert.That(source.Results, Is.EqualTo(1));
                    Assert.That(source.Registrations, Is.EqualTo(1));
                    Assert.That(source.TimedOut, Is.False);
                    Assert.That(probe.Disposals, Is.EqualTo(1));
                }
                finally
                {
                    release.Set();
                    source.Complete();
                    Join(worker);
                    Wait(() => iterator.DisposeAsync().IsCompleted);
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void CallerCancel_SynchronousMoveAndReentrantDispose_WaitsUntilCancelReturns()
        {
            using (var caller = new CancellationTokenSource())
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var input = new TaskCompletionSource<bool>();
                var probe = new BclProbe { Move = () => new ValueTask<bool>(input.Task) };
                IOnityAsyncEnumerator<int> iterator = null;
                OnityTask nestedDisposal = default;
                Exception callbackError = null;
                bool callbackTimedOut = false;
                CancellationTokenRegistration registration = default;
                probe.Acquire = token => registration = token.Register(() =>
                {
                    try
                    {
                        input.TrySetCanceled(token);
                        nestedDisposal = iterator.DisposeAsync();
                        entered.Set();
                        callbackTimedOut = !release.Wait(TimeSpan.FromSeconds(5));
                        // Accessing the owned token while Cancel is active must still be legal.
                        var handle = token.WaitHandle;
                    }
                    catch (Exception exception)
                    {
                        callbackError = exception;
                    }
                });
                iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator(caller.Token);
                var move = iterator.MoveNextAsync();
                Task worker = Task.Run(() => caller.Cancel());
                try
                {
                    Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                    Assert.That(nestedDisposal.IsCompleted, Is.False);
                    Assert.That(probe.Disposals, Is.EqualTo(1), "Consumed Move permits upstream cleanup before quiescence.");
                    release.Set();
                    Join(worker);
                    worker.GetAwaiter().GetResult();
                    Wait(() => nestedDisposal.IsCompleted);
                    Read(nestedDisposal);
                    Assert.That(Read(move), Is.False, "Explicit disposal won before canceled cleanup could commit.");
                    Assert.That(callbackTimedOut, Is.False);
                    Assert.That(callbackError, Is.Null);
                    Assert.DoesNotThrow(() => caller.Cancel());
                }
                finally
                {
                    release.Set();
                    input.TrySetResult(false);
                    Join(worker);
                    registration.Dispose();
                    Read(iterator.DisposeAsync());
                }
            }
        }

        [Test]
        public void CancellationCallbackFailure_DoesNotSkipCleanup_AndLaterCleanupFaultWins()
        {
            var input = new TaskCompletionSource<bool>();
            var callbackFault = new InvalidOperationException("token callback");
            var cleanupFault = new OperationCanceledException("later cleanup fault");
            var probe = new BclProbe
            {
                Move = () => new ValueTask<bool>(input.Task),
                Cleanup = () => new ValueTask(Task.FromException(cleanupFault))
            };
            CancellationTokenRegistration registration = default;
            probe.Acquire = token => registration = token.Register(() =>
            {
                input.TrySetResult(true);
                throw callbackFault;
            });
            var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var disposal = iterator.DisposeAsync();
            try
            {
                Wait(() => disposal.IsCompleted);
                Assert.That(Read(move), Is.False);
                Assert.That(disposal.IsFaulted, Is.True);
                Assert.That(disposal.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(disposal)), Is.SameAs(cleanupFault));
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
            finally
            {
                input.TrySetResult(false);
                registration.Dispose();
                Catch(() => Read(iterator.DisposeAsync()));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Export_MoveFaultedOceOrCancellation_AndStickyCurrentContract(bool canceled)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var fault = new OperationCanceledException("native fault", cancellation.Token);
                var probe = new NativeProbe
                {
                    Move = () => canceled ? OnityTask<bool>.FromCanceled(cancellation.Token)
                        : OnityTask<bool>.FromTask(Task.FromException<bool>(fault))
                };
                var iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
                Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsCanceled, Is.EqualTo(canceled));
                Assert.That(move.IsFaulted, Is.EqualTo(!canceled));
                Exception actual = Catch(() => Read(move));
                if (canceled)
                {
                    Assert.That(((OperationCanceledException)actual).CancellationToken, Is.EqualTo(cancellation.Token));
                }
                else
                {
                    Assert.That(actual, Is.SameAs(fault));
                }
                var sticky = iterator.MoveNextAsync();
                Assert.That(sticky.IsCanceled, Is.EqualTo(canceled));
                Catch(() => Read(sticky));
                Assert.That(probe.Moves, Is.EqualTo(1));
                Read(iterator.DisposeAsync());
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Export_NativePreservedOrTaskMove_IsObservedOnce_AndEarlyDisposeWaitsOwnedCleanup(int kind)
        {
            var gate = new Gate();
            var task = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanup = new OnityTaskCompletionSource();
            OnityTask<bool> input = kind == 2 ? OnityTask<bool>.FromTask(task.Task) : NativeMove(gate);
            if (kind == 1)
            {
                input = input.Preserve();
            }
            var probe = new NativeProbe { Move = () => input, Cleanup = () => cleanup.Task };
            var iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var disposal = iterator.DisposeAsync();
            try
            {
                Assert.That(Read(move), Is.False);
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.That(iterator.DisposeAsync().Equals(disposal), Is.True);
                cleanup.TrySetResult();
                Assert.That(disposal.IsCompleted, Is.False);
                gate.Complete();
                task.TrySetResult(true);
                Wait(() => disposal.IsCompleted);
                Read(disposal);
                Assert.That(probe.Disposals, Is.EqualTo(1));
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            }
            finally
            {
                gate.Complete();
                task.TrySetResult(false);
                cleanup.TrySetResult();
                Wait(() => disposal.IsCompleted);
                Read(iterator.DisposeAsync());
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdapterCleanupFault_ReplacesOrdinaryResult_AndRetainsFaultedOce(bool export)
        {
            var fault = new OperationCanceledException("cleanup actual fault");
            if (export)
            {
                var probe = new NativeProbe
                {
                    Move = () => OnityTask<bool>.FromResult(false),
                    Cleanup = () => OnityTask.FromTask(Task.FromException(fault))
                };
                var iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsFaulted, Is.True);
                Assert.That(move.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
                Catch(() => Read(iterator.DisposeAsync()));
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
            else
            {
                var probe = new BclProbe
                {
                    Move = () => new ValueTask<bool>(false),
                    Cleanup = () => new ValueTask(Task.FromException(fault))
                };
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsFaulted, Is.True);
                Assert.That(move.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
                Catch(() => Read(iterator.DisposeAsync()));
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [Test]
        public void InternalExportObservation_CapturesNeitherCreatorContextNorExecutionContext()
        {
            var local = new AsyncLocal<string>();
            SynchronizationContext previous = SynchronizationContext.Current;
            var context = new CountingContext();
            var source = new OnityTaskCompletionSource<bool>();
            var probe = new NativeProbe { Move = () => source.Task };
            IAsyncEnumerator<int> iterator = null;
            Task worker = null;
            var completed = new TaskCompletionSource<string>();
            try
            {
                local.Value = "creator";
                SynchronizationContext.SetSynchronizationContext(context);
                iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
                ValueTask<bool> move = iterator.MoveNextAsync();
                move.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
                {
                    try
                    {
                        move.GetAwaiter().GetResult();
                        completed.TrySetResult(local.Value);
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                });
                using (ExecutionContext.SuppressFlow())
                {
                    worker = Task.Run(() =>
                    {
                        string prior = local.Value;
                        try
                        {
                            local.Value = "producer";
                            source.TrySetResult(true);
                        }
                        finally
                        {
                            local.Value = prior;
                        }
                    });
                }
                Join(worker);
                worker.GetAwaiter().GetResult();
                Wait(() => completed.Task.IsCompleted);
                Assert.That(completed.Task.GetAwaiter().GetResult(), Is.Not.EqualTo("creator"));
                Assert.That(local.Value, Is.EqualTo("creator"));
                Assert.That(context.Posts, Is.Zero);
            }
            finally
            {
                source.TrySetResult(false);
                if (worker != null)
                {
                    Join(worker);
                }
                if (iterator != null)
                {
                    Read(iterator.DisposeAsync());
                }
                local.Value = null;
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CurrentInvocationOce_IsFaulted_NotCanceled_InEitherAdapter(bool export)
        {
            var fault = new OperationCanceledException("Current is a synchronous fault");
            if (export)
            {
                var probe = new NativeProbe { ReadCurrent = () => throw fault };
                var iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsFaulted, Is.True);
                Assert.That(move.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
                Read(iterator.DisposeAsync());
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
            else
            {
                var probe = new BclProbe { ReadCurrent = () => throw fault };
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsFaulted, Is.True);
                Assert.That(move.IsCanceled, Is.False);
                Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
                Read(iterator.DisposeAsync());
                Assert.That(probe.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingAdapterOverlap_RejectsWithoutSecondUpstreamMove_AndCurrentRemainsInvalid(bool export)
        {
            var input = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (export)
            {
                var probe = new NativeProbe { Move = () => OnityTask<bool>.FromTask(input.Task) };
                var iterator = probe.AsAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                try
                {
                    Assert.That(Catch(() => Read(iterator.MoveNextAsync())), Is.TypeOf<InvalidOperationException>());
                    Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                    Assert.That(probe.Moves, Is.EqualTo(1));
                    input.TrySetResult(true);
                    Wait(() => move.IsCompleted);
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(37));
                }
                finally
                {
                    input.TrySetResult(false);
                    var disposal = iterator.DisposeAsync();
                    Wait(() => disposal.IsCompleted);
                    Read(disposal);
                }
            }
            else
            {
                var probe = new BclProbe { Move = () => new ValueTask<bool>(input.Task) };
                var iterator = probe.AsOnityAsyncEnumerable().GetAsyncEnumerator();
                var move = iterator.MoveNextAsync();
                try
                {
                    Assert.That(Catch(() => Read(iterator.MoveNextAsync())), Is.TypeOf<InvalidOperationException>());
                    Assert.Throws<InvalidOperationException>(() => { var ignored = iterator.Current; });
                    Assert.That(probe.Moves, Is.EqualTo(1));
                    input.TrySetResult(true);
                    Wait(() => move.IsCompleted);
                    Assert.That(Read(move), Is.True);
                    Assert.That(iterator.Current, Is.EqualTo(37));
                }
                finally
                {
                    input.TrySetResult(false);
                    var disposal = iterator.DisposeAsync();
                    Wait(() => disposal.IsCompleted);
                    Read(disposal);
                }
            }
        }

        [UnityTest]
        public IEnumerator ActualReloadDisabledEveryUpdateSessions_RegisterInAwake_AndRetirePendingMove()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0
                || (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) == 0)
            {
                Assert.Ignore("Requires the verification host's existing disabled domain and scene reload settings.");
            }
            object identity = s_domainSentinel;
            Type type = Type.GetType("Onity.Tests.PlayMode.OnityAsyncEnumerableAdapterSessionProbe, Onity.Tests.PlayMode", true);
            Component probe = null;
            try
            {
                for (int session = 0; session < 2; session++)
                {
                    probe = new GameObject("Onity EveryUpdate Session Probe").AddComponent(type);
                    yield return new EnterPlayMode(false);
                    Assert.That(ReferenceEquals(identity, s_domainSentinel), Is.True);
                    double deadline = Time.realtimeSinceStartupAsDouble + 5;
                    while (!(bool)type.GetField("FirstCompleted").GetValue(probe))
                    {
                        Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline));
                        yield return null;
                    }
                    Assert.That((string)type.GetField("Error").GetValue(probe), Is.Null);
                    Assert.That((bool)type.GetField("RegisteredInAwake").GetValue(probe), Is.True);
                    Assert.That((bool)type.GetField("AwakeBeforeEnteredPlay").GetValue(probe), Is.True);
                    type.GetMethod("ArmExitWait").Invoke(probe, null);
                    Assert.That((bool)type.GetField("PendingBeforeExit").GetValue(probe), Is.True);
                    object managed = probe;
                    yield return new ExitPlayMode();
                    Assert.That(ReferenceEquals(identity, s_domainSentinel), Is.True);
                    Assert.That((string)type.GetField("Error").GetValue(managed), Is.Null);
                    Assert.That((bool)type.GetField("ExitCanceled").GetValue(managed), Is.True);
                    if (probe != null)
                    {
                        UnityEngine.Object.DestroyImmediate(probe.gameObject);
                    }
                    probe = null;
                }
            }
            finally
            {
                if (probe != null)
                {
                    type.GetMethod("RepairAfterFailure").Invoke(probe, null);
                    UnityEngine.Object.DestroyImmediate(probe.gameObject);
                }
            }
        }

        [UnityTearDown]
        public IEnumerator ExitAfterFailure()
        {
            if (Application.isPlaying)
            {
                yield return new ExitPlayMode();
            }
        }

        private static async IAsyncEnumerable<int> CompilerIterator(Task release, Action cleanup,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            try
            {
                await release.ConfigureAwait(false);
                yield return 43;
            }
            finally
            {
                cleanup();
            }
        }

        private static async OnityTask<int> ConsumeNative(IOnityAsyncEnumerable<int> source, bool throwBody, Exception fault)
        {
            await foreach (int value in source)
            {
                if (throwBody)
                {
                    throw fault;
                }
                return value;
            }
            return -1;
        }

        private static async Task<int> ConsumeBcl(IAsyncEnumerable<int> source, bool throwBody, Exception fault)
        {
            await foreach (int value in source.ConfigureAwait(false))
            {
                if (throwBody)
                {
                    throw fault;
                }
                return value;
            }
            return -1;
        }

        private static void CheckBody(OnityTask<int> task, bool throws, Exception fault)
        {
            if (throws)
            {
                Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            }
            else
            {
                Assert.That(Read(task), Is.EqualTo(37));
            }
        }

        private static async OnityTask<bool> NativeMove(Gate gate)
        {
            await gate;
            return true;
        }

        private static T Read<T>(OnityTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }
        private static void Read(OnityTask task)
        {
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }
        private static T Read<T>(ValueTask<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }
        private static void Read(ValueTask task)
        {
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }
        private static void Wait(Func<bool> predicate)
        {
            Assert.That(SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(5)), Is.True, "Adapter outcome timed out.");
        }
        private static void Join(Task task)
        {
            Assert.That(SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5)), Is.True, "Worker timed out.");
        }
        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed class CountingContext : SynchronizationContext
        {
            internal int Posts;
            public override void Post(SendOrPostCallback callback, object state) => Interlocked.Increment(ref Posts);
        }

        private sealed class BclProbe : IAsyncEnumerable<int>, IAsyncEnumerator<int>
        {
            internal int Acquires;
            internal int Moves;
            internal int Disposals;
            internal Action<CancellationToken> Acquire;
            internal Func<ValueTask<bool>> Move = () => new ValueTask<bool>(true);
            internal Func<ValueTask> Cleanup = () => default;
            internal Func<int> ReadCurrent = () => 37;
            public int Current => ReadCurrent();
            public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Acquires);
                Acquire?.Invoke(cancellationToken);
                return this;
            }
            public ValueTask<bool> MoveNextAsync()
            {
                Interlocked.Increment(ref Moves);
                return Move();
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref Disposals);
                return Cleanup();
            }
        }

        private sealed class NativeProbe : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            internal int Acquires;
            internal int Disposals;
            internal int Moves;
            internal Func<OnityTask<bool>> Move = () => OnityTask<bool>.FromResult(true);
            internal Func<OnityTask> Cleanup = () => default;
            internal Func<int> ReadCurrent = () => 37;
            public int Current => ReadCurrent();
            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Acquires);
                return this;
            }
            public OnityTask<bool> MoveNextAsync()
            {
                Interlocked.Increment(ref Moves);
                return Move();
            }
            public OnityTask DisposeAsync()
            {
                Interlocked.Increment(ref Disposals);
                return Cleanup();
            }
        }

        private sealed class ValueSource : IValueTaskSource<bool>, IValueTaskSource
        {
            private readonly object m_gate = new object();
            private Action<object> m_callback;
            private object m_state;
            private ValueTaskSourceStatus m_status = ValueTaskSourceStatus.Pending;
            private Exception m_error;
            private short m_version = 1;
            internal int Results;
            internal int Registrations;
            internal ValueTaskSourceOnCompletedFlags Flags;
            internal bool CompleteInRegistration;
            internal bool BlockNextPendingStatus;
            internal bool TimedOut;
            internal ManualResetEventSlim RegistrationEntered;
            internal ManualResetEventSlim RegistrationRelease;
            internal ManualResetEventSlim StatusEntered;
            internal ManualResetEventSlim StatusRelease;
            internal ValueTask<bool> MoveTask => new ValueTask<bool>(this, m_version);
            internal ValueTask CleanupTask => new ValueTask(this, m_version);

            public ValueTaskSourceStatus GetStatus(short token)
            {
                ValueTaskSourceStatus status;
                bool hold;
                lock (m_gate)
                {
                    Validate(token);
                    status = m_status;
                    hold = status == ValueTaskSourceStatus.Pending && BlockNextPendingStatus;
                    BlockNextPendingStatus = false;
                }
                if (hold)
                {
                    StatusEntered.Set();
                    TimedOut |= !StatusRelease.Wait(TimeSpan.FromSeconds(5));
                }
                return status;
            }

            public bool GetResult(short token)
            {
                Exception error;
                lock (m_gate)
                {
                    Validate(token);
                    if (m_status == ValueTaskSourceStatus.Pending)
                    {
                        throw new InvalidOperationException("ValueTask consumed while pending.");
                    }
                    Results++;
                    error = m_error;
                    m_version++;
                }
                if (error != null)
                {
                    throw error;
                }
                return true;
            }

            void IValueTaskSource.GetResult(short token) => GetResult(token);

            public void OnCompleted(Action<object> continuation, object state, short token,
                ValueTaskSourceOnCompletedFlags flags)
            {
                lock (m_gate)
                {
                    Validate(token);
                    Registrations++;
                    Flags = flags;
                    m_callback = continuation;
                    m_state = state;
                }
                if (CompleteInRegistration)
                {
                    Complete();
                    RegistrationEntered.Set();
                    TimedOut |= !RegistrationRelease.Wait(TimeSpan.FromSeconds(5));
                }
            }

            internal void Complete(ValueTaskSourceStatus status = ValueTaskSourceStatus.Succeeded, Exception error = null)
            {
                Action<object> callback;
                object state;
                lock (m_gate)
                {
                    if (m_status != ValueTaskSourceStatus.Pending)
                    {
                        return;
                    }
                    m_status = status;
                    m_error = error;
                    callback = m_callback;
                    state = m_state;
                    m_callback = null;
                }
                callback?.Invoke(state);
            }

            private void Validate(short token)
            {
                if (token != m_version)
                {
                    throw new InvalidOperationException("ValueTask status was read after consumption/version reuse.");
                }
            }

            internal void Reset()
            {
                lock (m_gate)
                {
                    if (m_status == ValueTaskSourceStatus.Pending)
                    {
                        throw new InvalidOperationException("Cannot reset an outstanding version.");
                    }
                    m_status = ValueTaskSourceStatus.Pending;
                    m_error = null;
                    m_callback = null;
                }
            }
        }

        private sealed class Gate : ICriticalNotifyCompletion
        {
            private Action m_continuation;
            public bool IsCompleted => false;
            public Gate GetAwaiter() => this;
            public void GetResult()
            {
            }
            public void OnCompleted(Action continuation) => m_continuation = continuation;
            public void UnsafeOnCompleted(Action continuation) => m_continuation = continuation;
            internal void Complete() => Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }
    }
}
