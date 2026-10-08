using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableSourcesEditModeTests
    {
        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static void Read(OnityTask task) => LinqTestSupport.Read(task);
        private static T[] ToArray<T>(IOnityAsyncEnumerable<T> source) => LinqTestSupport.ToArray(source);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);
        private static void Wait(Func<bool> done) => LinqTestSupport.Wait(done);

        // ---- Never -----------------------------------------------------------------------

        [Test]
        public void Never_IsAReusableSingletonPerItemType()
        {
            Assert.That(OnityAsyncEnumerable.Never<int>(), Is.SameAs(OnityAsyncEnumerable.Never<int>()));
            Assert.That(OnityAsyncEnumerable.Never<int>(), Is.Not.SameAs((object)OnityAsyncEnumerable.Never<string>()));
        }

        [Test]
        public void Never_StaysPendingUntilTheTokenIsCanceled_ThenCancelsTheMove()
        {
            using (var cts = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.Never<int>().GetAsyncEnumerator(cts.Token);
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsCompleted, Is.False);
                cts.Cancel();
                Assert.That(move.IsCanceled, Is.True);
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void Never_PreCanceledToken_CancelsTheFirstMove()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var iterator = OnityAsyncEnumerable.Never<int>().GetAsyncEnumerator(cts.Token);
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsCanceled, Is.True);
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void Never_DisposeWhilePending_EndsTheMoveWithFalse_AndDisposalIsIdempotent()
        {
            var iterator = OnityAsyncEnumerable.Never<int>().GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            var dispose = iterator.DisposeAsync();
            Read(dispose);
            Assert.That(Read(move), Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
        }

        [Test]
        public void Never_UnderOperators_CancelsThroughTheToken()
        {
            using (var cts = new CancellationTokenSource())
            {
                var task = OnityAsyncEnumerable.Never<int>().Where(value => true).ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.False);
                cts.Cancel();
                Wait(() => task.IsCompleted);
                Assert.That(task.IsCanceled, Is.True);
            }
        }

        [Test]
        public void Never_AfterCancellationTheRegistrationIsReleased()
        {
            using (var cts = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.Never<int>().GetAsyncEnumerator(cts.Token);
                var move = iterator.MoveNextAsync();
                Read(iterator.DisposeAsync());
                Assert.That(Read(move), Is.False);
                Assert.DoesNotThrow(() => cts.Cancel());
            }
        }

        // ---- Throw -----------------------------------------------------------------------

        [Test]
        public void Throw_FaultsTheFirstMoveWithTheSameException_AndStaysTerminal()
        {
            var fault = new InvalidOperationException("thrown");
            var iterator = OnityAsyncEnumerable.Throw<int>(fault).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(move)), Is.SameAs(fault));
            Assert.That(Catch(() => Read(iterator.MoveNextAsync())), Is.SameAs(fault));
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Throw_IsReusable_AndUnderOperatorsFaultsOnce()
        {
            var fault = new InvalidOperationException("thrown");
            var stream = OnityAsyncEnumerable.Throw<int>(fault);
            Assert.That(Catch(() => ToArray(stream)), Is.SameAs(fault));
            Assert.That(Catch(() => ToArray(stream.Select(value => value))), Is.SameAs(fault));
            Assert.That(Catch(() => Read(stream.FirstAsync())), Is.SameAs(fault));
        }

        [Test]
        public void Throw_PreCanceledToken_CancelsInsteadOfFaulting()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var move = OnityAsyncEnumerable.Throw<int>(new InvalidOperationException("thrown"))
                    .GetAsyncEnumerator(cts.Token).MoveNextAsync();
                Assert.That(move.IsCanceled, Is.True);
            }
        }

        [Test]
        public void Throw_OperationCanceledException_StaysAFault()
        {
            var task = OnityAsyncEnumerable.Throw<int>(new OperationCanceledException("as fault")).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
        }

        [Test]
        public void Throw_NullException_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.Throw<int>(null));
        }

        // ---- Repeat ----------------------------------------------------------------------

        [Test]
        public void Repeat_YieldsTheElementTheGivenNumberOfTimes()
        {
            Assert.That(ToArray(OnityAsyncEnumerable.Repeat(7, 3)), Is.EqualTo(new[] { 7, 7, 7 }));
            Assert.That(ToArray(OnityAsyncEnumerable.Repeat("x", 1)), Is.EqualTo(new[] { "x" }));
            var shared = new object();
            object[] items = ToArray(OnityAsyncEnumerable.Repeat(shared, 2));
            Assert.That(items[0], Is.SameAs(shared));
            Assert.That(items[1], Is.SameAs(shared));
            Assert.That(ToArray(OnityAsyncEnumerable.Repeat<string>(null, 2)), Is.EqualTo(new string[] { null, null }));
        }

        [Test]
        public void Repeat_ZeroCountIsEmpty_AndNegativeCountIsRejected()
        {
            Assert.That(ToArray(OnityAsyncEnumerable.Repeat(1, 0)), Is.Empty);
            Assert.That(OnityAsyncEnumerable.Repeat(1, 0), Is.SameAs(OnityAsyncEnumerable.Empty<int>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => OnityAsyncEnumerable.Repeat(1, -1));
        }

        [Test]
        public void Repeat_Cancellation_BeforeAndDuringTheEnumeration()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.That(OnityAsyncEnumerable.Repeat(1, 3).ToArrayAsync(cts.Token).IsCanceled, Is.True);
            }
            using (var cts = new CancellationTokenSource())
            {
                int seen = 0;
                var task = OnityAsyncEnumerable.Repeat(1, 10).Do(value =>
                {
                    if (++seen == 3)
                    {
                        cts.Cancel();
                    }
                }).ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(seen, Is.EqualTo(3));
            }
        }

        [Test]
        public void Repeat_EndWinsOverACanceledToken_AndLargeCountsDoNotRecurse()
        {
            using (var cts = new CancellationTokenSource())
            {
                var iterator = OnityAsyncEnumerable.Repeat(1, 1).GetAsyncEnumerator(cts.Token);
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                cts.Cancel();
                Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            }
            Assert.That(Read(OnityAsyncEnumerable.Repeat(1, 100000).CountAsync()), Is.EqualTo(100000));
        }

        // ---- Create ----------------------------------------------------------------------

        // Completes on a thread-pool thread. A native source resumes its awaiter on the completing thread, so the
        // producer continues on a worker even when the caller runs under Unity's synchronization context.
        private static OnityTask WorkerHop()
        {
            var gate = new OnityTaskCompletionSource();
            ThreadPool.QueueUserWorkItem(state => ((OnityTaskCompletionSource)state).TrySetResult(), gate);
            return gate.Task;
        }

        private static async OnityTask CountingProducer(IOnityAsyncWriter<int> writer, int count, List<string> log)
        {
            for (int i = 0; i < count; i++)
            {
                log.Add("yield:" + i);
                await writer.YieldAsync(i);
                log.Add("resumed:" + i);
            }
            log.Add("done");
        }

        [Test]
        public void Create_Arguments_AreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => OnityAsyncEnumerable.Create<int>(null));
        }

        [Test]
        public void Create_YieldsItemsInOrder_AndEndsWhenTheProducerCompletes()
        {
            var log = new List<string>();
            var stream = OnityAsyncEnumerable.Create<int>((writer, token) => CountingProducer(writer, 3, log));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(log, Is.EqualTo(new[]
            {
                "yield:0", "resumed:0", "yield:1", "resumed:1", "yield:2", "resumed:2", "done"
            }));
        }

        [Test]
        public void Create_ProducerRunsOnlyWhenTheConsumerPulls()
        {
            var log = new List<string>();
            int invocations = 0;
            var stream = OnityAsyncEnumerable.Create<int>((writer, token) =>
            {
                invocations++;
                return CountingProducer(writer, 3, log);
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(invocations, Is.Zero);
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(0));
            Assert.That(log, Is.EqualTo(new[] { "yield:0" }));
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            Assert.That(log, Is.EqualTo(new[] { "yield:0", "resumed:0", "yield:1" }));
            Assert.That(invocations, Is.EqualTo(1));
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Create_EachEnumerationRunsItsOwnProducer()
        {
            int invocations = 0;
            var stream = OnityAsyncEnumerable.Create<int>((writer, token) =>
            {
                invocations++;
                return CountingProducer(writer, 2, new List<string>());
            });
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(invocations, Is.EqualTo(2));
        }

        [Test]
        public void Create_ProducerThatCompletesWithoutYielding_IsEmpty()
        {
            var stream = OnityAsyncEnumerable.Create<int>((writer, token) => OnityTask.Completed);
            Assert.That(ToArray(stream), Is.Empty);
        }

        [Test]
        public void Create_ProducerFault_ArrivesAfterEarlierItems_AndDisposalDoesNotRepeatIt()
        {
            var fault = new InvalidOperationException("producer");
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                await writer.YieldAsync(1);
                throw fault;
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            var failed = iterator.MoveNextAsync();
            Assert.That(failed.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(failed)), Is.SameAs(fault));
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Create_SynchronousProducerFailure_FaultsTheFirstMove()
        {
            var fault = new InvalidOperationException("sync");
            var stream = OnityAsyncEnumerable.Create<int>((writer, token) => throw fault);
            var task = stream.ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
        }

        [Test]
        public void Create_ProducerThatThrowsBeforeItsFirstAwait_FaultsTheFirstMove()
        {
            var fault = new InvalidOperationException("early");
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                if (writer != null)
                {
                    throw fault;
                }
                await writer.YieldAsync(1);
            });
            Assert.That(Catch(() => ToArray(stream)), Is.SameAs(fault));
        }

        [Test]
        public void Create_EarlyConsumerExit_UnwindsTheParkedProducer_BeforeDisposalCompletes()
        {
            var log = new List<string>();
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                try
                {
                    for (int i = 0; ; i++)
                    {
                        await writer.YieldAsync(i);
                    }
                }
                finally
                {
                    log.Add("finally");
                }
            });
            Assert.That(Read(stream.FirstAsync()), Is.EqualTo(0));
            Assert.That(log, Is.EqualTo(new[] { "finally" }));
            Assert.That(ToArray(stream.Take(3)), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(log, Is.EqualTo(new[] { "finally", "finally" }));
        }

        [Test]
        public void Create_ProducerGetsACancelableToken_ThatIsCanceledOnDisposal()
        {
            CancellationToken seen = default;
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                seen = token;
                await writer.YieldAsync(1);
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(seen.CanBeCanceled, Is.True);
            Assert.That(seen.IsCancellationRequested, Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(seen.IsCancellationRequested, Is.True);
        }

        [Test]
        public void Create_DisposeWhileTheProducerAwaitsExternalWork_CancelsItsTokenAndWaitsForIt()
        {
            var gate = new OnityTaskCompletionSource();
            var log = new List<string>();
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                token.Register(() => gate.TrySetCanceled(token));
                try
                {
                    await gate.Task;
                }
                catch (OperationCanceledException)
                {
                    log.Add("canceled");
                    throw;
                }
            });
            var iterator = stream.GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            var dispose = iterator.DisposeAsync();
            Assert.That(dispose.IsCompleted, Is.True);
            Read(dispose);
            Assert.That(Read(move), Is.False);
            Assert.That(log, Is.EqualTo(new[] { "canceled" }));
        }

        [Test]
        public void Create_DisposeRetainsCleanupWhileAnUncooperativeProducerKeepsRunning()
        {
            var gate = new OnityTaskCompletionSource();
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) => { await gate.Task; });
            var iterator = stream.GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            Assert.That(dispose.IsCompleted, Is.False);
            gate.TrySetResult();
            Wait(() => dispose.IsCompleted);
            Read(dispose);
            Assert.That(Read(move), Is.False);
        }

        [Test]
        public void Create_ProducerFaultThatNoMoveObserved_IsReportedByDisposal()
        {
            var fault = new InvalidOperationException("unwind");
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                try
                {
                    await writer.YieldAsync(1);
                }
                catch (OperationCanceledException)
                {
                    throw fault;
                }
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            var dispose = iterator.DisposeAsync();
            Assert.That(dispose.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(dispose)), Is.SameAs(fault));
        }

        [Test]
        public void Create_PreCanceledToken_CancelsWithoutStartingTheProducer()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                int invocations = 0;
                var stream = OnityAsyncEnumerable.Create<int>((writer, token) =>
                {
                    invocations++;
                    return OnityTask.Completed;
                });
                var task = stream.ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(invocations, Is.Zero);
            }
        }

        [Test]
        public void Create_TokenCanceledWhileTheProducerIsParked_ReleasesItWithoutADisposeCall()
        {
            using (var cts = new CancellationTokenSource())
            {
                var log = new List<string>();
                var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
                {
                    try
                    {
                        await writer.YieldAsync(1);
                        log.Add("resumed");
                    }
                    catch (OperationCanceledException)
                    {
                        log.Add("unwound");
                        throw;
                    }
                });
                var iterator = stream.GetAsyncEnumerator(cts.Token);
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                cts.Cancel();
                Assert.That(log, Is.EqualTo(new[] { "unwound" }));
                var move = iterator.MoveNextAsync();
                Assert.That(move.IsCanceled, Is.True);
                Read(iterator.DisposeAsync());
            }
        }

        [Test]
        public void Create_TokenCanceledWhileAMoveIsPending_CancelsTheMove_ForACooperativeProducer()
        {
            using (var cts = new CancellationTokenSource())
            {
                var gate = new OnityTaskCompletionSource();
                var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
                {
                    token.Register(() => gate.TrySetCanceled(token));
                    await gate.Task;
                });
                var task = stream.ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.False);
                cts.Cancel();
                Wait(() => task.IsCompleted);
                Assert.That(task.IsCanceled, Is.True);
            }
        }

        [Test]
        public void Create_SecondYieldBeforeTheFirstCompletes_FaultsTheProducer()
        {
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                OnityTask first = writer.YieldAsync(1);
                OnityTask second = writer.YieldAsync(2);
                await first;
                await second;
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            var failed = iterator.MoveNextAsync();
            Assert.That(failed.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(failed)), Is.InstanceOf<InvalidOperationException>());
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Create_YieldAfterDisposal_ReturnsACanceledTask()
        {
            IOnityAsyncWriter<int> captured = null;
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                captured = writer;
                await writer.YieldAsync(1);
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Read(iterator.DisposeAsync());
            Assert.That(captured.YieldAsync(2).IsCanceled, Is.True);
        }

        [Test]
        public void Create_ProducerThatEndsWhileAMoveIsPending_EndsTheStream()
        {
            var gate = new OnityTaskCompletionSource();
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                await writer.YieldAsync(1);
                await gate.Task;
            });
            var iterator = stream.GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            var pending = iterator.MoveNextAsync();
            Assert.That(pending.IsCompleted, Is.False);
            gate.TrySetResult();
            Assert.That(Read(pending), Is.False);
            Read(iterator.DisposeAsync());
        }

        [Test]
        public void Create_FeedsOperators()
        {
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                for (int i = 1; i <= 6; i++)
                {
                    await writer.YieldAsync(i);
                }
            });
            Assert.That(ToArray(stream.Where(value => value % 2 == 0).Select(value => value * 10)),
                Is.EqualTo(new[] { 20, 40, 60 }));
            Assert.That(ToArray(stream.Pairwise()).Length, Is.EqualTo(5));
            Assert.That(ToArray(stream.Buffer(4)).Length, Is.EqualTo(2));
            Assert.That(ToArray(stream.Reverse()), Is.EqualTo(new[] { 6, 5, 4, 3, 2, 1 }));
            Assert.That(Read(stream.CountAsync()), Is.EqualTo(6));
        }

        [Test]
        public void Create_ProducerThatYieldsFromWorkerThreads_DeliversEveryItemInOrder()
        {
            for (int round = 0; round < 10; round++)
            {
                var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
                {
                    for (int i = 0; i < 300; i++)
                    {
                        await WorkerHop();
                        await writer.YieldAsync(i);
                    }
                });
                var task = stream.ToArrayAsync();
                Wait(() => task.IsCompleted);
                int[] items = Read(task);
                Assert.That(items.Length, Is.EqualTo(300));
                for (int i = 0; i < items.Length; i++)
                {
                    Assert.That(items[i], Is.EqualTo(i));
                }
            }
        }

        [Test]
        public void Create_ConsumerThatPullsFromWorkerThreads_DeliversEveryItemInOrder()
        {
            var stream = OnityAsyncEnumerable.Create<int>(async (writer, token) =>
            {
                for (int i = 0; i < 300; i++)
                {
                    await writer.YieldAsync(i);
                }
            });
            var results = new List<int>();
            var done = new OnityTaskCompletionSource();
            Task.Run(async () =>
            {
                var iterator = stream.GetAsyncEnumerator();
                while (await iterator.MoveNextAsync().AsTask())
                {
                    results.Add(iterator.Current);
                    await Task.Yield();
                }
                await iterator.DisposeAsync().AsTask();
                done.TrySetResult();
            });
            Wait(() => done.Task.IsCompleted);
            Assert.That(results.Count, Is.EqualTo(300));
            for (int i = 0; i < results.Count; i++)
            {
                Assert.That(results[i], Is.EqualTo(i));
            }
        }
    }
}
