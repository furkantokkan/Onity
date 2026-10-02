using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableSharingEditModeTests
    {
        private sealed class FailingStream : IOnityAsyncEnumerable<int>, IOnityAsyncEnumerator<int>
        {
            private readonly Exception m_failure;
            internal int Acquires;
            internal int Disposals;

            internal FailingStream(Exception failure)
            {
                m_failure = failure;
            }

            public int Current => 0;

            public IOnityAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Acquires++;
                return this;
            }

            public OnityTask<bool> MoveNextAsync() => OnityTask<bool>.FromException(m_failure);

            public OnityTask DisposeAsync()
            {
                Disposals++;
                return OnityTask.Completed;
            }
        }

        // ---- Publish --------------------------------------------------------------------------

        [Test]
        public void Publish_SubscribersBeforeConnect_ReceiveEveryItemAndTheEnd()
        {
            var log = new List<string>();
            var source = MultiTest.Ints("source", log, 1, 2, 3);
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            IOnityAsyncEnumerator<int> first = published.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> second = published.GetAsyncEnumerator();
            Assert.That(source.Acquires, Is.Zero, "nothing is enumerated before Connect");

            IDisposable connection = published.Connect();
            Assert.That(log, Is.EqualTo(new[] { "source.acquire", "source.dispose" }));
            foreach (IOnityAsyncEnumerator<int> subscriber in new[] { first, second })
            {
                MultiTest.ExpectItem(subscriber, 1);
                MultiTest.ExpectItem(subscriber, 2);
                MultiTest.ExpectItem(subscriber, 3);
                MultiTest.ExpectEnd(subscriber);
                MultiTest.Dispose(subscriber);
            }
            Assert.That(published.Connect(), Is.SameAs(connection));
            Assert.That(source.Acquires, Is.EqualTo(1), "the source is never restarted");
        }

        [Test]
        public void Publish_WaitingSubscriberGetsTheNextItem_OthersBufferIt_AndDisconnectEndsEveryone()
        {
            var source = MultiTest.Manual("source", null);
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            IOnityAsyncEnumerator<int> waiting = published.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> idle = published.GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(waiting);
            Assert.That(move.Completed, Is.False);

            IDisposable connection = published.Connect();
            source.Push(1);
            Assert.That(move.Value, Is.True);
            Assert.That(waiting.Current, Is.EqualTo(1));
            source.Push(2);
            MultiTest.ExpectItem(idle, 1);
            MultiTest.ExpectItem(idle, 2);
            MultiTest.ExpectItem(waiting, 2);

            MultiTestResult<bool> end = MultiTest.Move(waiting);
            connection.Dispose();
            Assert.That(source.Token.IsCancellationRequested, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(end.Completed, Is.True);
            Assert.That(end.Canceled, Is.False);
            Assert.That(end.Value, Is.False);
            MultiTest.ExpectEnd(idle);
            Assert.That(published.Connect(), Is.SameAs(connection));
            Assert.That(source.Acquires, Is.EqualTo(1));
            connection.Dispose();
            MultiTest.Dispose(waiting);
            MultiTest.Dispose(idle);
        }

        [Test]
        public void Publish_SourceFault_FaultsEverySubscriberAfterItsBuffer_AndLateSubscribers()
        {
            var source = MultiTest.Manual("source", null);
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            IOnityAsyncEnumerator<int> a = published.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> b = published.GetAsyncEnumerator();
            MultiTestResult<bool> first = MultiTest.Move(a);
            published.Connect();
            source.Push(1);
            Assert.That(first.Value, Is.True);
            MultiTestResult<bool> fault = MultiTest.Move(a);
            source.Fail(new FormatException("source"));
            Assert.That(fault.Error, Is.TypeOf<FormatException>());
            MultiTest.ExpectItem(b, 1);
            Assert.That(MultiTest.Move(b).Error, Is.TypeOf<FormatException>());
            Assert.That(MultiTest.Move(published.GetAsyncEnumerator()).Error, Is.TypeOf<FormatException>());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Publish_ItemsWithoutSubscribersAreDropped_AndLateSubscribersEnd()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            published.Connect();
            IOnityAsyncEnumerator<int> late = published.GetAsyncEnumerator();
            MultiTest.ExpectEnd(late);
            MultiTest.Dispose(late);
        }

        [Test]
        public void Publish_SubscriberDisposalAndToken_AffectOnlyThatSubscriber()
        {
            var source = MultiTest.Manual("s", null);
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> disposed = published.GetAsyncEnumerator();
            IOnityAsyncEnumerator<int> canceled = published.GetAsyncEnumerator(cts.Token);
            IOnityAsyncEnumerator<int> survivor = published.GetAsyncEnumerator();
            IDisposable connection = published.Connect();
            MultiTest.Dispose(disposed);
            MultiTestResult<bool> canceledMove = MultiTest.Move(canceled);
            MultiTestResult<bool> survivorMove = MultiTest.Move(survivor);
            cts.Cancel();
            Assert.That(canceledMove.Canceled, Is.True);
            Assert.That(canceledMove.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(survivorMove.Completed, Is.False);
            source.Push(3);
            Assert.That(survivorMove.Value, Is.True);
            Assert.That(survivor.Current, Is.EqualTo(3));
            Assert.That(source.Disposals, Is.Zero);
            connection.Dispose();
            MultiTest.ExpectEnd(survivor);
            MultiTest.Dispose(survivor);
            MultiTest.Dispose(canceled);
        }

        [Test]
        public void Publish_SourceDisposalFailure_BecomesTheTerminalFault()
        {
            var source = MultiTest.Ints("s", null, 1);
            source.DisposeFailure = new InvalidOperationException("dispose");
            IOnityConnectableAsyncEnumerable<int> published = source.Publish();
            IOnityAsyncEnumerator<int> subscriber = published.GetAsyncEnumerator();
            published.Connect();
            MultiTest.ExpectItem(subscriber, 1);
            Assert.That(MultiTest.Move(subscriber).Error,
                Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("dispose"));
        }

        // ---- Queue ------------------------------------------------------------------------------

        [Test]
        public void Queue_PullsTheSourceEagerly_AndDrainsBeforeTheEnd()
        {
            var source = MultiTest.Manual("s", null, 1, 2, 3);
            IOnityAsyncEnumerator<int> enumerator = source.Queue().GetAsyncEnumerator();
            MultiTest.ExpectItem(enumerator, 1);
            Assert.That(source.Moves, Is.EqualTo(4), "every buffered item was pulled at once");
            source.Push(4);
            Assert.That(source.Moves, Is.EqualTo(5), "the pump keeps one move outstanding");
            MultiTest.ExpectItem(enumerator, 2);
            MultiTest.ExpectItem(enumerator, 3);
            MultiTest.ExpectItem(enumerator, 4);
            MultiTestResult<bool> end = MultiTest.Move(enumerator);
            Assert.That(end.Completed, Is.False);
            source.End();
            Assert.That(end.Value, Is.False);
            Assert.That(end.Completed, Is.True);
            Assert.That(source.Disposals, Is.EqualTo(1));
            MultiTest.Dispose(enumerator);
        }

        [Test]
        public void Queue_FaultIsRelayedAfterBufferedItems()
        {
            var source = MultiTest.Manual("s", null);
            IOnityAsyncEnumerator<int> enumerator = source.Queue().GetAsyncEnumerator();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            source.Push(1);
            Assert.That(move.Value, Is.True);
            source.Push(2);
            source.Fail(new FormatException());
            MultiTest.ExpectItem(enumerator, 2);
            Assert.That(MultiTest.Move(enumerator).Error, Is.TypeOf<FormatException>());
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Queue_TokenCancellationDiscardsTheBuffer_AndDisposalSettlesThePump()
        {
            var source = MultiTest.Manual("s", null, 1, 2, 3);
            source.HonorsToken = false;
            using var cts = new CancellationTokenSource();
            IOnityAsyncEnumerator<int> enumerator = source.Queue().GetAsyncEnumerator(cts.Token);
            MultiTest.ExpectItem(enumerator, 1);
            cts.Cancel();
            MultiTestResult<bool> move = MultiTest.Move(enumerator);
            Assert.That(move.Canceled, Is.True);
            Assert.That(move.CanceledToken, Is.EqualTo(cts.Token));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.HasPendingMove, Is.False);

            var pending = MultiTest.Manual("p", null);
            IOnityAsyncEnumerator<int> idle = pending.Queue().GetAsyncEnumerator();
            MultiTestResult<bool> wait = MultiTest.Move(idle);
            MultiTest.Dispose(idle);
            Assert.That(wait.Value, Is.False);
            Assert.That(pending.Disposals, Is.EqualTo(1));
            Assert.That(pending.HasPendingMove, Is.False);
        }

        [Test]
        public void SharingArguments_AreValidated()
        {
            IOnityAsyncEnumerable<int> missing = null;
            Assert.Throws<ArgumentNullException>(() => missing.Publish());
            Assert.Throws<ArgumentNullException>(() => missing.Queue());
        }

        // ---- BindTo -------------------------------------------------------------------------------

        [Test]
        public void BindTo_AppliesEveryItem_AndDisposesTheStream()
        {
            var values = new List<int>();
            var source = MultiTest.Ints("s", null, 1, 2, 3);
            source.BindTo(values, (target, item) => target.Add(item), CancellationToken.None);
            Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void BindTo_RebindsOnceAfterAnError()
        {
            var values = new List<int>();
            var source = MultiTest.Ints("s", null, 1, 2, 3);
            source.MoveFailureAt = 1;
            source.MoveFailure = new FormatException();
            source.BindTo(values, (target, item) => target.Add(item), CancellationToken.None);
            Assert.That(values, Is.EqualTo(new[] { 1, 1, 2, 3 }));
            Assert.That(source.Acquires, Is.EqualTo(2));
            Assert.That(source.Disposals, Is.EqualTo(2));
        }

        [Test]
        public void BindTo_RepeatedErrorWithoutProgress_IsReported([Values] bool rebindOnError)
        {
            var failing = new FailingStream(new FormatException("bind failure"));
            LogAssert.Expect(LogType.Exception, new Regex("FormatException: bind failure"));
            failing.BindTo(new List<int>(), (target, item) => target.Add(item), CancellationToken.None, rebindOnError);
            Assert.That(failing.Acquires, Is.EqualTo(rebindOnError ? 2 : 1));
            Assert.That(failing.Disposals, Is.EqualTo(failing.Acquires));
        }

        [Test]
        public void BindTo_CancellationEndsSilently()
        {
            var values = new List<int>();
            var source = MultiTest.Manual("s", null);
            using var cts = new CancellationTokenSource();
            source.BindTo(values, (target, item) => target.Add(item), cts.Token);
            source.Push(5);
            Assert.That(values, Is.EqualTo(new[] { 5 }));
            cts.Cancel();
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.Acquires, Is.EqualTo(1));
        }

        [Test]
        public void BindTo_ActionException_IsReported_AndTheStreamIsDisposed()
        {
            var source = MultiTest.Ints("s", null, 1, 2);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: action failure"));
            source.BindTo(0, (target, item) => throw new InvalidOperationException("action failure"),
                CancellationToken.None);
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(source.Moves, Is.EqualTo(1));
        }

        [Test]
        public void BindTo_Arguments_AreValidated()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = MultiTest.Ints("s", null, 1);
            Assert.Throws<ArgumentNullException>(() =>
                missing.BindTo(new List<int>(), (target, item) => target.Add(item), CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() =>
                source.BindTo(new List<int>(), (Action<List<int>, int>)null, CancellationToken.None));
            Assert.Throws<ArgumentNullException>(() => source.BindTo<int, MonoBehaviour>(null, (behaviour, item) => { }));
            Assert.That(source.Acquires, Is.Zero);
        }
    }
}
