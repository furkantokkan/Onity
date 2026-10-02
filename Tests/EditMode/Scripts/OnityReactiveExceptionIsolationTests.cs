using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Reactive;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityReactiveExceptionIsolationTests
    {
        private Action<Exception> m_originalHandler;

        [SetUp]
        public void SetUp()
        {
            m_originalHandler = OnityObservableExceptionHandler.Handler;
        }

        [TearDown]
        public void TearDown()
        {
            OnityObservableExceptionHandler.Handler = m_originalHandler;
        }

        [Test]
        public void OnNext_MiddleSubscriberThrows_OtherSubscribersStillReceiveValue()
        {
            List<Exception> caughtExceptions = new List<Exception>();
            OnityObservableExceptionHandler.Handler = caughtExceptions.Add;

            using Subject<int> subject = new Subject<int>();
            ValueRecorder first = new ValueRecorder();
            ValueRecorder third = new ValueRecorder();
            InvalidOperationException thrown = new InvalidOperationException("middle observer failed");

            IDisposable firstSubscription = subject.Subscribe(first.Record);
            IDisposable middleSubscription = subject.Subscribe(value => throw thrown);
            IDisposable thirdSubscription = subject.Subscribe(third.Record);

            subject.OnNext(42);

            Assert.That(first.Values, Is.EqualTo(new[] { 42 }), "First subscriber must still receive the value.");
            Assert.That(third.Values, Is.EqualTo(new[] { 42 }), "Third subscriber must still receive the value.");
            Assert.That(caughtExceptions.Count, Is.EqualTo(1), "Handler must fire exactly once for the throwing subscriber.");
            Assert.That(caughtExceptions[0], Is.SameAs(thrown), "Handler must receive the exact exception thrown by the subscriber.");

            firstSubscription.Dispose();
            middleSubscription.Dispose();
            thirdSubscription.Dispose();
        }

        [Test]
        public void OnNext_ThrowingSubscriber_DoesNotStopSubsequentEmissions()
        {
            int handlerInvocations = 0;
            OnityObservableExceptionHandler.Handler = _ => handlerInvocations++;

            using Subject<int> subject = new Subject<int>();
            ValueRecorder first = new ValueRecorder();
            ValueRecorder third = new ValueRecorder();

            IDisposable firstSubscription = subject.Subscribe(first.Record);
            IDisposable middleSubscription = subject.Subscribe(value => throw new InvalidOperationException());
            IDisposable thirdSubscription = subject.Subscribe(third.Record);

            subject.OnNext(1);
            subject.OnNext(2);

            Assert.That(first.Values, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(third.Values, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(handlerInvocations, Is.EqualTo(2));

            firstSubscription.Dispose();
            middleSubscription.Dispose();
            thirdSubscription.Dispose();
        }

        [Test]
        public void OnNext_HandlerItselfThrows_RemainingSubscribersStillReceiveValue()
        {
            OnityObservableExceptionHandler.Handler = _ => throw new InvalidOperationException("handler failed");

            using Subject<int> subject = new Subject<int>();
            ValueRecorder third = new ValueRecorder();

            IDisposable middleSubscription = subject.Subscribe(value => throw new InvalidOperationException("subscriber failed"));
            IDisposable thirdSubscription = subject.Subscribe(third.Record);

            Assert.That(() => subject.OnNext(7), Throws.Nothing, "A throwing handler must not surface from OnNext.");
            Assert.That(third.Values, Is.EqualTo(new[] { 7 }), "A throwing handler must not stop sibling subscribers.");

            middleSubscription.Dispose();
            thirdSubscription.Dispose();
        }

        [Test]
        public void Handler_SetToNull_RestoresNoOpDefault()
        {
            OnityObservableExceptionHandler.Handler = _ => { };
            OnityObservableExceptionHandler.Handler = null;

            Assert.That(OnityObservableExceptionHandler.Handler, Is.Not.Null, "Null assignment must restore a non-null no-op handler.");

            using Subject<int> subject = new Subject<int>();
            ValueRecorder observer = new ValueRecorder();

            IDisposable throwingSubscription = subject.Subscribe(value => throw new InvalidOperationException());
            IDisposable observerSubscription = subject.Subscribe(observer.Record);

            Assert.That(() => subject.OnNext(5), Throws.Nothing, "Default no-op handler must swallow subscriber exceptions.");
            Assert.That(observer.Values, Is.EqualTo(new[] { 5 }));

            throwingSubscription.Dispose();
            observerSubscription.Dispose();
        }

        private sealed class ValueRecorder
        {
            private readonly List<int> m_values = new List<int>();

            public IReadOnlyList<int> Values => m_values;

            public void Record(int value)
            {
                m_values.Add(value);
            }
        }
    }
}
