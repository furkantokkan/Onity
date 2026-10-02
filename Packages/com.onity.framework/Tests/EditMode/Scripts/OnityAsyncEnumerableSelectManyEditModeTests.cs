using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableSelectManyEditModeTests
    {
        [Test]
        public void Arguments_AreValidatedSynchronously()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Func<int, IOnityAsyncEnumerable<int>> selector = null;
            Func<int, int, int> resultSelector = null;
            Func<int, OnityTask<IOnityAsyncEnumerable<int>>> awaitSelector = null;
            Func<int, CancellationToken, OnityTask<IOnityAsyncEnumerable<int>>> cancelSelector = null;
            Assert.Throws<ArgumentNullException>(() => missing.SelectMany(x => source));
            Assert.Throws<ArgumentNullException>(() => source.SelectMany(selector));
            Assert.Throws<ArgumentNullException>(() => source.SelectMany(x => source, resultSelector));
            Assert.Throws<ArgumentNullException>(() => source.SelectManyAwait(awaitSelector));
            Assert.Throws<ArgumentNullException>(() => source.SelectManyAwaitWithCancellation(cancelSelector));
            Assert.That(source.Acquires, Is.Zero);
        }

        [Test]
        public void SelectMany_FlattensInOrder_WithIndexAndResultSelector()
        {
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 1, 2, 3)
                .SelectMany(x => OnityAsyncEnumerable.Range(0, x))), Is.EqualTo(new[] { 0, 0, 1, 0, 1, 2 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 5, 6)
                .SelectMany((x, i) => OnityAsyncEnumerable.Range(i, 2), (x, y) => x * 10 + y)),
                Is.EqualTo(new[] { 50, 51, 61, 62 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 4)
                .SelectMany((x, i) => OnityAsyncEnumerable.Range(x, i + 1))), Is.EqualTo(new[] { 4 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null).SelectMany(x => OnityAsyncEnumerable.Range(0, 3))),
                Is.Empty);
        }

        [Test]
        public void QuerySyntax_UsesTheResultSelectorOverload()
        {
            IOnityAsyncEnumerable<int> query =
                from x in MultiTest.Ints("o", null, 1, 2)
                from y in OnityAsyncEnumerable.Range(10, 2)
                select x * 100 + y;
            Assert.That(MultiTest.Drain(query), Is.EqualTo(new[] { 110, 111, 210, 211 }));
        }

        [Test]
        public void InnerStreams_AreDisposedBeforeTheOuterStreamMovesAgain()
        {
            var log = new List<string>();
            var outer = MultiTest.Ints("outer", log, 1, 2);
            Assert.That(MultiTest.Drain(outer.SelectMany(x => new MultiTestStream<int>("inner" + x, log, x * 10))),
                Is.EqualTo(new[] { 10, 20 }));
            Assert.That(log, Is.EqualTo(new[]
            {
                "outer.acquire", "inner1.acquire", "inner1.dispose", "inner2.acquire", "inner2.dispose", "outer.dispose"
            }));
        }

        [Test]
        public void Faults_FromTheInnerOrOuterStreamOrANullInner_DisposeInnerThenOuter()
        {
            var log = new List<string>();
            var outer = MultiTest.Ints("outer", log, 1);
            IOnityAsyncEnumerable<int> faultingInner = MultiTest.Ints("inner", log, 9);
            ((MultiTestStream<int>)faultingInner).MoveFailureAt = 0;
            ((MultiTestStream<int>)faultingInner).MoveFailure = new FormatException();
            Exception inner = MultiTest.Catch(() => MultiTest.Drain(outer.SelectMany(x => faultingInner)));
            Assert.That(inner, Is.TypeOf<FormatException>());
            Assert.That(log, Is.EqualTo(new[] { "outer.acquire", "inner.acquire", "inner.dispose", "outer.dispose" }));

            var faultingOuter = MultiTest.Ints("outer2", null, 1, 2);
            faultingOuter.MoveFailureAt = 1;
            faultingOuter.MoveFailure = new InvalidOperationException("outer");
            Exception outerFault = MultiTest.Catch(() => MultiTest.Drain(
                faultingOuter.SelectMany(x => OnityAsyncEnumerable.Return(x))));
            Assert.That(outerFault, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("outer"));
            Assert.That(faultingOuter.Disposals, Is.EqualTo(1));

            Exception missing = MultiTest.Catch(() => MultiTest.Drain(
                MultiTest.Ints("o", null, 1).SelectMany(x => (IOnityAsyncEnumerable<int>)null)));
            Assert.That(missing, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void DisposeDuringAnInnerStream_DisposesInnerThenOuterOnce()
        {
            var log = new List<string>();
            var outer = MultiTest.Ints("outer", log, 1);
            var inner = MultiTest.Manual("inner", log);
            IOnityAsyncEnumerator<int> enumerator = outer.SelectMany(x => inner).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            MultiTest.Dispose(enumerator);
            Assert.That(move.Value, Is.False);
            Assert.That(log, Is.EqualTo(new[] { "outer.acquire", "inner.acquire", "inner.dispose", "outer.dispose" }));
        }

        [Test]
        public void EarlyTermination_AcrossInnerStreams()
        {
            var log = new List<string>();
            var outer = MultiTest.Ints("outer", log, 1, 2);
            Assert.That(MultiTest.Drain(outer.SelectMany(x => new MultiTestStream<int>("inner" + x, log, 0, 1, 2)).Take(4)),
                Is.EqualTo(new[] { 0, 1, 2, 0 }));
            Assert.That(log, Is.EqualTo(new[]
            {
                "outer.acquire", "inner1.acquire", "inner1.dispose", "inner2.acquire", "inner2.dispose", "outer.dispose"
            }));
        }

        [Test]
        public void SelectManyAwait_AwaitsTheCollectionAndResultSelectors()
        {
            var gate = new OnityTaskCompletionSource<IOnityAsyncEnumerable<int>>();
            IOnityAsyncEnumerator<int> enumerator = MultiTest.Ints("o", null, 1).SelectManyAwait(x => gate.Task)
                .GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Completed, Is.False);
            gate.TrySetResult(OnityAsyncEnumerable.Range(7, 2));
            Assert.That(move.Value, Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(7));
            MultiTest.ExpectItem(enumerator, 8);
            MultiTest.ExpectEnd(enumerator);
            MultiTest.Dispose(enumerator);

            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 1, 2).SelectManyAwait(
                x => OnityTask.FromResult(OnityAsyncEnumerable.Range(0, 2)),
                (x, y) => OnityTask.FromResult(x * 10 + y))), Is.EqualTo(new[] { 10, 11, 20, 21 }));
            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 3, 4).SelectManyAwait(
                (x, i) => OnityTask.FromResult(OnityAsyncEnumerable.Return(x + i)))), Is.EqualTo(new[] { 3, 5 }));
        }

        [Test]
        public void SelectManyAwaitWithCancellation_DisposalCancelsTheOwnedToken_AndWaitsForTheSelector()
        {
            var gate = new OnityTaskCompletionSource<IOnityAsyncEnumerable<int>>();
            CancellationToken received = default;
            var outer = MultiTest.Ints("outer", null, 1);
            IOnityAsyncEnumerator<int> enumerator = outer.SelectManyAwaitWithCancellation((x, token) =>
            {
                received = token;
                return gate.Task;
            }).GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(received.CanBeCanceled, Is.True);
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(move.Value, Is.False);
            Assert.That(received.IsCancellationRequested, Is.True);
            Assert.That(outer.Disposals, Is.EqualTo(1));
            Assert.That(disposal.Completed, Is.False, "cleanup waits for the pending selector");
            gate.TrySetCanceled(received);
            Assert.That(disposal.Completed, Is.True);
            Assert.That(disposal.Error, Is.Null);

            Assert.That(MultiTest.Drain(MultiTest.Ints("o", null, 2).SelectManyAwaitWithCancellation(
                (x, i, token) => OnityTask.FromResult(OnityAsyncEnumerable.Range(x, 2)),
                (x, y, token) => OnityTask.FromResult(x + y))), Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void PrecanceledDelegateToken_CancelsTheAwaitStep()
        {
            var token = new CancellationToken(true);
            int calls = 0;
            var outer = MultiTest.Ints("o", null, 1);
            outer.HonorsToken = false;
            IOnityAsyncEnumerator<int> enumerator = outer
                .SelectManyAwait(x =>
                {
                    calls++;
                    return OnityTask.FromResult(OnityAsyncEnumerable.Return(x));
                }).GetAsyncEnumerator(token);
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(token));
            Assert.That(calls, Is.Zero);
            Assert.That(outer.Moves, Is.EqualTo(1));
            MultiTest.Dispose(enumerator);
        }
    }
}
