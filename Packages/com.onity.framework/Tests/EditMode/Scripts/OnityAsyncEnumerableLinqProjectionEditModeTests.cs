using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableLinqProjectionEditModeTests
    {
        private sealed class RecordingObserver : IObserver<int>
        {
            internal readonly List<string> Events = new List<string>();

            public void OnNext(int value) => Events.Add("next:" + value);
            public void OnError(Exception error) => Events.Add("error:" + error.Message);
            public void OnCompleted() => Events.Add("completed");
        }

        private static LinqProbe<int> Items(params int[] items) => LinqTestSupport.Items(items);
        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static void Read(OnityTask task) => LinqTestSupport.Read(task);
        private static T[] ToArray<T>(IOnityAsyncEnumerable<T> source) => LinqTestSupport.ToArray(source);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);

        private static int[][] Lists(IOnityAsyncEnumerable<IList<int>> source)
        {
            IList<int>[] lists = ToArray(source);
            var result = new int[lists.Length][];
            for (int i = 0; i < lists.Length; i++)
            {
                result[i] = lists[i].ToArray();
            }
            return result;
        }

        // ---- admission -------------------------------------------------------------------

        [Test]
        public void Arguments_ValidateSynchronously_AndDescriptionsAreLazy()
        {
            IOnityAsyncEnumerable<object> missingObjects = null;
            IOnityAsyncEnumerable<int> missing = null;
            var source = Items(1, 2, 3);
            var objects = new LinqProbe<object>(1, "a");

            Assert.Throws<ArgumentNullException>(() => missingObjects.OfType<string>());
            Assert.Throws<ArgumentNullException>(() => missingObjects.Cast<string>());
            Assert.Throws<ArgumentNullException>(() => missing.Do(value => { }));
            Assert.Throws<ArgumentNullException>(() => missing.Do(value => { }, error => { }));
            Assert.Throws<ArgumentNullException>(() => missing.Do(value => { }, () => { }));
            Assert.Throws<ArgumentNullException>(() => missing.Do(value => { }, error => { }, () => { }));
            Assert.Throws<ArgumentNullException>(() => missing.Do(new RecordingObserver()));
            Assert.Throws<ArgumentNullException>(() => source.Do((IObserver<int>)null));
            Assert.Throws<ArgumentNullException>(() => missing.DefaultIfEmpty());
            Assert.Throws<ArgumentNullException>(() => missing.DefaultIfEmpty(5));
            Assert.Throws<ArgumentNullException>(() => missing.Pairwise());
            Assert.Throws<ArgumentNullException>(() => missing.Buffer(2));
            Assert.Throws<ArgumentNullException>(() => missing.Buffer(2, 1));
            Assert.Throws<ArgumentNullException>(() => missing.Reverse());
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Buffer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Buffer(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Buffer(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Buffer(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.Buffer(2, -3));

            objects.OfType<string>().GetAsyncEnumerator().DisposeAsync();
            objects.Cast<string>().GetAsyncEnumerator().DisposeAsync();
            source.Do(value => { }).GetAsyncEnumerator().DisposeAsync();
            source.DefaultIfEmpty().GetAsyncEnumerator().DisposeAsync();
            source.Pairwise().GetAsyncEnumerator().DisposeAsync();
            source.Buffer(2).GetAsyncEnumerator().DisposeAsync();
            source.Buffer(2, 1).GetAsyncEnumerator().DisposeAsync();
            source.Reverse().GetAsyncEnumerator().DisposeAsync();
            Assert.That(source.Acquires, Is.Zero);
            Assert.That(source.Moves, Is.Zero);
            Assert.That(objects.Acquires, Is.Zero);
        }

        // ---- OfType / Cast ---------------------------------------------------------------

        [Test]
        public void OfType_KeepsOnlyInstancesOfTheType_AndSkipsNull()
        {
            var source = new LinqProbe<object>(1, "a", null, "b", 2.5, 3);
            Assert.That(ToArray(source.OfType<string>()), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ToArray(source.OfType<int>()), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(ToArray(source.OfType<IComparable>()).Length, Is.EqualTo(5));
            Assert.That(ToArray(source.OfType<object>()).Length, Is.EqualTo(5));
            Assert.That(ToArray(source.OfType<Uri>()), Is.Empty);
            Assert.That(ToArray(new LinqProbe<object>().OfType<int>()), Is.Empty);
            Assert.That(source.Disposals, Is.EqualTo(5));
        }

        [Test]
        public void OfType_AcceptsReferenceTypeStreamsThroughCovariance()
        {
            var animals = new LinqProbe<IComparable>("x", "y");
            Assert.That(ToArray(animals.OfType<string>()), Is.EqualTo(new[] { "x", "y" }));
            Assert.That(ToArray(animals.Cast<string>()), Is.EqualTo(new[] { "x", "y" }));
        }

        [Test]
        public void Cast_ConvertsEveryItem_AndNullStaysNullForReferenceTypes()
        {
            Assert.That(ToArray(new LinqProbe<object>(1, 2, 3).Cast<int>()), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ToArray(new LinqProbe<object>("a", null, "b").Cast<string>()),
                Is.EqualTo(new string[] { "a", null, "b" }));
            Assert.That(ToArray(new LinqProbe<object>().Cast<int>()), Is.Empty);
        }

        [Test]
        public void Cast_InvalidItem_FaultsTheStream_AfterEarlierItems()
        {
            var source = new LinqProbe<object>(1, "x", 3);
            var iterator = source.Cast<int>().GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            var failed = iterator.MoveNextAsync();
            Assert.That(failed.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(failed)), Is.InstanceOf<InvalidCastException>());
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Catch(() => Read(iterator.MoveNextAsync())), Is.InstanceOf<InvalidCastException>());
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Catch(() => ToArray(new LinqProbe<object>(1, null).Cast<int>())), Is.Not.Null);
        }

        // ---- Do --------------------------------------------------------------------------

        [Test]
        public void Do_InvokesCallbacksInOrder_AndYieldsItemsUnchanged()
        {
            var source = Items(1, 2, 3);
            var events = new List<string>();
            int disposalsAtCompletion = -1;
            var stream = source.Do(
                value => events.Add("next:" + value),
                error => events.Add("error"),
                () =>
                {
                    events.Add("completed");
                    disposalsAtCompletion = source.Disposals;
                });
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(events, Is.EqualTo(new[] { "next:1", "next:2", "next:3", "completed" }));
            Assert.That(disposalsAtCompletion, Is.Zero);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_AllOverloads_ObserveNormalCompletion()
        {
            var seen = new List<int>();
            Assert.That(ToArray(Items(1, 2).Do(seen.Add)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(Items(3).Do(seen.Add, error => Assert.Fail("unexpected error"))),
                Is.EqualTo(new[] { 3 }));
            int completed = 0;
            Assert.That(ToArray(Items(4).Do(seen.Add, () => completed++)), Is.EqualTo(new[] { 4 }));
            Assert.That(ToArray(Items(5).Do(seen.Add, null, () => completed++)), Is.EqualTo(new[] { 5 }));
            Assert.That(ToArray(Items(6).Do(null, null, () => completed++)), Is.EqualTo(new[] { 6 }));
            Assert.That(ToArray(Items(7).Do(null, null, null)), Is.EqualTo(new[] { 7 }));
            Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(completed, Is.EqualTo(3));
            Assert.That(ToArray(Items().Do(seen.Add)), Is.Empty);
        }

        [Test]
        public void Do_AfterTake_RunsOnCompletedBeforeTheDownstreamTerminalCompletes()
        {
            var events = new List<string>();
            var source = Items(1, 2, 3, 4);
            var count = source.Take(2).Do(value => events.Add("next"), () => events.Add("completed")).CountAsync();
            Assert.That(Read(count), Is.EqualTo(2));
            Assert.That(events, Is.EqualTo(new[] { "next", "next", "completed" }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_Observer_ReceivesItemsAndCompletion_AndFailure()
        {
            var observer = new RecordingObserver();
            Assert.That(ToArray(Items(1, 2).Do(observer)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(observer.Events, Is.EqualTo(new[] { "next:1", "next:2", "completed" }));

            var failing = new RecordingObserver();
            var source = Items(1, 2);
            source.MoveFailureAt = 1;
            source.MoveFailure = new InvalidOperationException("boom");
            Assert.That(Catch(() => ToArray(source.Do(failing))), Is.SameAs(source.MoveFailure));
            Assert.That(failing.Events, Is.EqualTo(new[] { "next:1", "error:boom" }));
        }

        [Test]
        public void Do_UpstreamFault_ReportsToOnErrorBeforeCleanup_AndPropagatesTheSameFault()
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            Exception reported = null;
            int disposalsAtError = -1;
            int completions = 0;
            var task = source.Do(value => { }, error =>
            {
                reported = error;
                disposalsAtError = source.Disposals;
            }, () => completions++).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(disposalsAtError, Is.Zero);
            Assert.That(completions, Is.Zero);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_PendingUpstreamFault_ReportsToOnErrorWhenItArrives()
        {
            var fault = new InvalidOperationException("late");
            var source = Items(1, 2, 3);
            source.PendingAt = 1;
            source.Pending = new OnityTaskCompletionSource<bool>();
            Exception reported = null;
            var task = source.Do(value => { }, error => reported = error).ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(reported, Is.Null);
            source.Pending.TrySetException(fault);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_PendingUpstreamSuccess_PassesItemsThrough()
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 1;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var task = source.Do(value => { }, error => Assert.Fail("unexpected error")).ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            source.Pending.TrySetResult(true);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_CanceledUpstream_ReportsOperationCanceledToOnError_AndStaysCanceled()
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3);
                source.OnMove = move =>
                {
                    if (move == 1)
                    {
                        cts.Cancel();
                    }
                };
                Exception reported = null;
                var task = source.Do(value => { }, error => reported = error).ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(reported, Is.InstanceOf<OperationCanceledException>());
                Assert.That(((OperationCanceledException)reported).CancellationToken, Is.EqualTo(cts.Token));
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [Test]
        public void Do_OnNextThrows_ReportsToOnError_ThenFaultsWithTheSameException()
        {
            var fault = new InvalidOperationException("next");
            var source = Items(1, 2, 3);
            Exception reported = null;
            var task = source.Do(value =>
            {
                if (value == 2)
                {
                    throw fault;
                }
            }, error => reported = error).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.Moves, Is.EqualTo(2));
        }

        [Test]
        public void Do_OnNextThrowsWithoutOnError_FaultsTheStream()
        {
            var fault = new InvalidOperationException("next");
            var source = Items(1, 2);
            var task = source.Do(value => throw fault).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_OnErrorThatThrows_ReplacesTheFailure()
        {
            var original = new InvalidOperationException("original");
            var replacement = new ArgumentException("replacement");
            var source = Items(1, 2);
            var task = source.Do(value => throw original, error => throw replacement).ToArrayAsync();
            Assert.That(Catch(() => Read(task)), Is.SameAs(replacement));

            var upstream = Items(1, 2);
            upstream.MoveFailureAt = 1;
            upstream.MoveFailure = original;
            var upstreamTask = upstream.Do(value => { }, error => throw replacement).ToArrayAsync();
            Assert.That(Catch(() => Read(upstreamTask)), Is.SameAs(replacement));
            Assert.That(upstream.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_OnCompletedThrows_ReportsToOnError_ThenFaults()
        {
            var fault = new InvalidOperationException("completed");
            Exception reported = null;
            var source = Items(1);
            var task = source.Do(value => { }, error => reported = error, () => throw fault).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(reported, Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Do_IsAReusableDescriptionWithIndependentEnumerations()
        {
            int calls = 0;
            var stream = Items(1, 2).Do(value => calls++);
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(calls, Is.EqualTo(4));
        }

        // ---- DefaultIfEmpty --------------------------------------------------------------

        [Test]
        public void DefaultIfEmpty_YieldsTheDefaultOnlyForAnEmptyStream()
        {
            Assert.That(ToArray(Items().DefaultIfEmpty()), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(Items().DefaultIfEmpty(42)), Is.EqualTo(new[] { 42 }));
            Assert.That(ToArray(Items(1, 2).DefaultIfEmpty(42)), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ToArray(Items(0).DefaultIfEmpty(42)), Is.EqualTo(new[] { 0 }));
            Assert.That(ToArray(new LinqProbe<string>().DefaultIfEmpty()), Is.EqualTo(new string[] { null }));
            Assert.That(ToArray(new LinqProbe<string>().DefaultIfEmpty("x")), Is.EqualTo(new[] { "x" }));
        }

        [Test]
        public void DefaultIfEmpty_DisposesUpstreamBeforeThePublishedDefault_AndEndsAfterIt()
        {
            var source = Items();
            var iterator = source.DefaultIfEmpty(7).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(7));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Assert.That(source.Moves, Is.EqualTo(1));
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- Pairwise --------------------------------------------------------------------

        [Test]
        public void Pairwise_PairsEachItemWithItsPredecessor()
        {
            var pairs = ToArray(Items(1, 2, 3, 4).Pairwise());
            Assert.That(pairs.Select(pair => pair.Previous).ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(pairs.Select(pair => pair.Current).ToArray(), Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(ToArray(Items(1, 2).Pairwise()).Single(), Is.EqualTo((1, 2)));
            Assert.That(ToArray(Items(1).Pairwise()), Is.Empty);
            Assert.That(ToArray(Items().Pairwise()), Is.Empty);
        }

        [Test]
        public void Pairwise_EachEnumerationKeepsItsOwnPredecessor()
        {
            var stream = Items(1, 2, 3).Pairwise();
            Assert.That(ToArray(stream), Is.EqualTo(new[] { (1, 2), (2, 3) }));
            Assert.That(ToArray(stream), Is.EqualTo(new[] { (1, 2), (2, 3) }));
        }

        // ---- Buffer ----------------------------------------------------------------------

        [Test]
        public void Buffer_GroupsConsecutiveItems_AndPublishesThePartialTail()
        {
            Assert.That(Lists(Items(1, 2, 3, 4, 5, 6, 7).Buffer(3)),
                Is.EqualTo(new[] { new[] { 1, 2, 3 }, new[] { 4, 5, 6 }, new[] { 7 } }));
            Assert.That(Lists(Items(1, 2, 3, 4).Buffer(2)), Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3, 4 } }));
            Assert.That(Lists(Items(1, 2).Buffer(5)), Is.EqualTo(new[] { new[] { 1, 2 } }));
            Assert.That(Lists(Items(1, 2, 3).Buffer(1)), Is.EqualTo(new[] { new[] { 1 }, new[] { 2 }, new[] { 3 } }));
            Assert.That(Lists(Items().Buffer(3)), Is.Empty);
            Assert.That(Lists(Items(1, 2, 3).Buffer(int.MaxValue)), Is.EqualTo(new[] { new[] { 1, 2, 3 } }));
        }

        [Test]
        public void Buffer_PublishedListsAreOwnedByTheConsumer()
        {
            var lists = ToArray(Items(1, 2, 3, 4, 5).Buffer(2));
            Assert.That(lists.Length, Is.EqualTo(3));
            Assert.That(lists[0], Is.Not.SameAs(lists[1]));
            Assert.That(lists[0].ToArray(), Is.EqualTo(new[] { 1, 2 }));
            lists[0].Add(99);
            Assert.That(lists[1].ToArray(), Is.EqualTo(new[] { 3, 4 }));
        }

        [Test]
        public void Buffer_DisposesUpstreamBeforeThePublishedPartialTail()
        {
            var source = Items(1, 2, 3);
            var iterator = source.Buffer(2).GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current.ToArray(), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(source.Disposals, Is.Zero);
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current.ToArray(), Is.EqualTo(new[] { 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void BufferWithSkip_MatchesTheReferenceWindowing()
        {
            for (int length = 0; length <= 9; length++)
            {
                int[] data = Enumerable.Range(0, length).ToArray();
                for (int count = 1; count <= 5; count++)
                {
                    for (int skip = 1; skip <= 6; skip++)
                    {
                        var expected = new List<int[]>();
                        for (int start = 0; start < length; start += skip)
                        {
                            expected.Add(data.Skip(start).Take(count).ToArray());
                        }
                        Assert.That(Lists(Items(data).Buffer(count, skip)), Is.EqualTo(expected.ToArray()),
                            "length " + length + " count " + count + " skip " + skip);
                    }
                }
            }
        }

        [Test]
        public void BufferWithSkip_OverlapGapAndEqualSkip()
        {
            Assert.That(Lists(Items(1, 2, 3, 4, 5).Buffer(3, 1)),
                Is.EqualTo(new[] { new[] { 1, 2, 3 }, new[] { 2, 3, 4 }, new[] { 3, 4, 5 }, new[] { 4, 5 }, new[] { 5 } }));
            Assert.That(Lists(Items(0, 1, 2, 3, 4, 5, 6, 7, 8, 9).Buffer(2, 5)),
                Is.EqualTo(new[] { new[] { 0, 1 }, new[] { 5, 6 } }));
            Assert.That(Lists(Items(1, 2, 3, 4, 5).Buffer(2, 2)),
                Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3, 4 }, new[] { 5 } }));
        }

        [Test]
        public void BufferWithSkip_DisposesUpstreamBeforeTheLastPublishedList()
        {
            var source = Items(1, 2, 3, 4, 5);
            var iterator = source.Buffer(3, 1).GetAsyncEnumerator();
            foreach (int[] expected in new[] { new[] { 1, 2, 3 }, new[] { 2, 3, 4 }, new[] { 3, 4, 5 }, new[] { 4, 5 } })
            {
                Assert.That(Read(iterator.MoveNextAsync()), Is.True);
                Assert.That(iterator.Current.ToArray(), Is.EqualTo(expected));
                Assert.That(source.Disposals, Is.Zero);
            }
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current.ToArray(), Is.EqualTo(new[] { 5 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Buffer_OverLargeRun_DoesNotRecurse()
        {
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).Buffer(1000)).Length, Is.EqualTo(100));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 50000).Buffer(2, 1000)).Length, Is.EqualTo(50));
            Assert.That(ToArray(OnityAsyncEnumerable.Range(0, 100000).Reverse()).First(), Is.EqualTo(99999));
        }

        // ---- Reverse ---------------------------------------------------------------------

        [Test]
        public void Reverse_YieldsItemsLastToFirst()
        {
            Assert.That(ToArray(Items(1, 2, 3, 4).Reverse()), Is.EqualTo(new[] { 4, 3, 2, 1 }));
            Assert.That(ToArray(Items(7).Reverse()), Is.EqualTo(new[] { 7 }));
            Assert.That(ToArray(Items().Reverse()), Is.Empty);
        }

        [Test]
        public void Reverse_ReadsTheWholeUpstreamOnTheFirstMove_AndDisposesBeforeTheLastItem()
        {
            var source = Items(1, 2, 3);
            var iterator = source.Reverse().GetAsyncEnumerator();
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(3));
            Assert.That(source.Moves, Is.EqualTo(4));
            Assert.That(source.Disposals, Is.Zero);
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(2));
            Assert.That(Read(iterator.MoveNextAsync()), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(iterator.MoveNextAsync()), Is.False);
            Assert.That(source.Moves, Is.EqualTo(4));
            Read(iterator.DisposeAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Reverse_PendingUpstream_WaitsForTheWholeStream()
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 1;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var task = source.Reverse().ToArrayAsync();
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            source.Pending.TrySetResult(true);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        // ---- cancellation, faults, disposal, reuse ---------------------------------------

        private static IOnityAsyncEnumerable<object> ObjectStream(LinqProbe<object> source, int shape)
        {
            return shape == 0 ? (IOnityAsyncEnumerable<object>)source.OfType<object>().Select(value => value)
                : source.Cast<object>().Select(value => value);
        }

        private static IOnityAsyncEnumerable<int> Build(LinqProbe<int> source, int shape)
        {
            switch (shape)
            {
                case 0: return source.Do(value => { });
                case 1: return source.DefaultIfEmpty(9);
                case 2: return source.Pairwise().Select(pair => pair.Current);
                case 3: return source.Buffer(2).Select(list => list.Count);
                case 4: return source.Buffer(2, 1).Select(list => list.Count);
                case 5: return source.Reverse();
                default: return source.Do(value => { }, error => { }, () => { });
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void CancellationMidEnumeration_IsCanceled_AndUpstreamIsDisposedOnce(int shape)
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4, 5, 6);
                source.OnMove = move =>
                {
                    if (move == 2)
                    {
                        cts.Cancel();
                    }
                };
                var task = Build(source, shape).ToArrayAsync(cts.Token);
                Assert.That(task.IsCompleted, Is.True);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void PreCanceledToken_IsCanceled_AndUpstreamIsDisposedOnce(int shape)
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var source = Items(1, 2, 3);
                var task = Build(source, shape).ToArrayAsync(cts.Token);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void UpstreamFault_PropagatesThroughOperators_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3, 4);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            var task = Build(source, shape).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void ExplicitDisposeWhilePending_DisposesUpstreamOnce(int shape)
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 0;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var iterator = Build(source, shape).GetAsyncEnumerator();
            var move = iterator.MoveNextAsync();
            Assert.That(move.IsCompleted, Is.False);
            var dispose = iterator.DisposeAsync();
            source.Pending.TrySetResult(false);
            LinqTestSupport.Wait(() => dispose.IsCompleted && move.IsCompleted);
            Read(dispose);
            Assert.That(Read(move), Is.False);
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void EarlyConsumerExit_DisposesUpstreamOnce(int shape)
        {
            var source = Items(1, 2, 3, 4, 5, 6);
            Read(Build(source, shape).FirstAsync());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void CleanupFailure_TakesPrecedenceOverTheLastItem(int shape)
        {
            var cleanup = new InvalidOperationException("cleanup");
            var source = Items(1, 2, 3);
            source.DisposeFailure = cleanup;
            var task = Build(source, shape).ToArrayAsync();
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(cleanup));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Descriptions_AreReusableWithIndependentState(int shape)
        {
            var source = Items(1, 2, 3, 4);
            var stream = Build(source, shape);
            int[] first = ToArray(stream);
            int[] second = ToArray(stream);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(source.Disposals, Is.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void OfTypeAndCast_PropagateFaultsCancellationAndDisposal(int shape)
        {
            var fault = new InvalidOperationException("source");
            var failing = new LinqProbe<object>(1, 2, 3);
            failing.MoveFailureAt = 1;
            failing.MoveFailure = fault;
            var task = ObjectStream(failing, shape).ToArrayAsync();
            Assert.That(Catch(() => Read(task)), Is.SameAs(fault));
            Assert.That(failing.Disposals, Is.EqualTo(1));

            using (var cts = new CancellationTokenSource())
            {
                var source = new LinqProbe<object>(1, 2, 3, 4);
                source.OnMove = move =>
                {
                    if (move == 2)
                    {
                        cts.Cancel();
                    }
                };
                var canceled = ObjectStream(source, shape).ToArrayAsync(cts.Token);
                Assert.That(canceled.IsCanceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }

            var pending = new LinqProbe<object>(1, 2);
            pending.PendingAt = 0;
            pending.Pending = new OnityTaskCompletionSource<bool>();
            var iterator = ObjectStream(pending, shape).GetAsyncEnumerator();
            var move2 = iterator.MoveNextAsync();
            var dispose = iterator.DisposeAsync();
            pending.Pending.TrySetResult(false);
            LinqTestSupport.Wait(() => dispose.IsCompleted && move2.IsCompleted);
            Assert.That(Read(move2), Is.False);
            Assert.That(pending.Disposals, Is.EqualTo(1));
        }
    }
}
