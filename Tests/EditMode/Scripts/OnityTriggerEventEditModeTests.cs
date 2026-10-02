using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async.Triggers;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the public waiter-list building block (<see cref="OnityTriggerEvent{T}"/> and
    /// <see cref="IOnityTriggerHandler{T}"/>) with custom handlers, independent of any trigger component.
    /// </summary>
    public sealed class OnityTriggerEventEditModeTests
    {
        private sealed class Holder
        {
            public OnityTriggerEvent<int> Event;
        }

        private sealed class Recorder : IOnityTriggerHandler<int>
        {
            private readonly string m_name;
            private readonly List<string> m_log;

            public Recorder(string name, List<string> log)
            {
                m_name = name;
                m_log = log;
            }

            public Action<int> OnNextAction;
            public Action OnCompletedAction;
            public CancellationToken CanceledToken;
            public Exception Error;

            public IOnityTriggerHandler<int> Prev { get; set; }

            public IOnityTriggerHandler<int> Next { get; set; }

            public void OnNext(int value)
            {
                m_log.Add(m_name + ":next:" + value);
                OnNextAction?.Invoke(value);
            }

            public void OnError(Exception exception)
            {
                Error = exception;
                m_log.Add(m_name + ":error");
            }

            public void OnCompleted()
            {
                m_log.Add(m_name + ":completed");
                OnCompletedAction?.Invoke();
            }

            public void OnCanceled(CancellationToken cancellationToken)
            {
                CanceledToken = cancellationToken;
                m_log.Add(m_name + ":canceled");
            }
        }

        private Holder m_holder;
        private List<string> m_log;

        [SetUp]
        public void SetUp()
        {
            m_holder = new Holder();
            m_log = new List<string>();
        }

        [Test]
        public void SetResult_DeliversToEveryHandlerInRegistrationOrder()
        {
            m_holder.Event.Add(new Recorder("a", m_log));
            m_holder.Event.Add(new Recorder("b", m_log));
            m_holder.Event.Add(new Recorder("c", m_log));

            m_holder.Event.SetResult(7);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:7", "b:next:7", "c:next:7" }));
        }

        [Test]
        public void SetResult_WithoutHandlers_DoesNothing()
        {
            Assert.DoesNotThrow(() => m_holder.Event.SetResult(1));
            Assert.DoesNotThrow(() => m_holder.Event.SetCompleted());
            Assert.That(m_log, Is.Empty);
        }

        [Test]
        public void SetResult_HandlerAddedDuringDelivery_MissesTheCurrentValueOnly()
        {
            Recorder late = new Recorder("late", m_log);
            Recorder first = new Recorder("a", m_log);
            first.OnNextAction = _ => m_holder.Event.Add(late);
            m_holder.Event.Add(first);
            m_holder.Event.Add(new Recorder("b", m_log));

            m_holder.Event.SetResult(1);
            first.OnNextAction = null;
            m_holder.Event.SetResult(2);

            Assert.That(m_log, Is.EqualTo(new[]
            {
                "a:next:1", "b:next:1",
                "a:next:2", "b:next:2", "late:next:2"
            }));
        }

        [Test]
        public void SetResult_HandlerRemovedDuringDelivery_IsSkipped()
        {
            Recorder removed = new Recorder("b", m_log);
            Recorder first = new Recorder("a", m_log);
            first.OnNextAction = _ => m_holder.Event.Remove(removed);
            m_holder.Event.Add(first);
            m_holder.Event.Add(removed);
            m_holder.Event.Add(new Recorder("c", m_log));

            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1", "c:next:1" }));
        }

        [Test]
        public void SetResult_HandlerRemovingItself_DoesNotBreakTheDelivery()
        {
            Recorder self = new Recorder("a", m_log);
            self.OnNextAction = _ => m_holder.Event.Remove(self);
            m_holder.Event.Add(self);
            m_holder.Event.Add(new Recorder("b", m_log));

            m_holder.Event.SetResult(1);
            m_holder.Event.SetResult(2);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1", "b:next:1", "b:next:2" }));
        }

        [Test]
        public void SetResult_RaisedFromAHandler_ThrowsInsideItAndDropsThatHandler()
        {
            Recorder reentrant = new Recorder("a", m_log);
            reentrant.OnNextAction = value => m_holder.Event.SetResult(value + 1);
            m_holder.Event.Add(reentrant);
            m_holder.Event.Add(new Recorder("b", m_log));
            LogAssert.Expect(LogType.Exception, new Regex("Can not trigger itself in iterating"));

            m_holder.Event.SetResult(1);
            m_holder.Event.SetResult(5);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1", "b:next:1", "b:next:5" }));
        }

        [Test]
        public void SetCompleted_NotifiesAndRemovesEveryHandler()
        {
            m_holder.Event.Add(new Recorder("a", m_log));
            m_holder.Event.Add(new Recorder("b", m_log));

            m_holder.Event.SetCompleted();
            m_holder.Event.SetCompleted();
            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:completed", "b:completed" }));
        }

        [Test]
        public void SetCanceled_PassesTheTokenAndRemovesEveryHandler()
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                Recorder a = new Recorder("a", m_log);
                Recorder b = new Recorder("b", m_log);
                m_holder.Event.Add(a);
                m_holder.Event.Add(b);

                m_holder.Event.SetCanceled(source.Token);
                m_holder.Event.SetResult(1);

                Assert.That(m_log, Is.EqualTo(new[] { "a:canceled", "b:canceled" }));
                Assert.That(a.CanceledToken, Is.EqualTo(source.Token));
                Assert.That(b.CanceledToken, Is.EqualTo(source.Token));
            }
        }

        [Test]
        public void SetError_PassesTheExceptionAndRemovesEveryHandler()
        {
            InvalidOperationException failure = new InvalidOperationException("boom");
            Recorder a = new Recorder("a", m_log);
            m_holder.Event.Add(a);
            m_holder.Event.Add(new Recorder("b", m_log));

            m_holder.Event.SetError(failure);
            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:error", "b:error" }));
            Assert.That(a.Error, Is.SameAs(failure));
            Assert.Throws<ArgumentNullException>(() => m_holder.Event.SetError(null));
        }

        [Test]
        public void TerminalCallFromADelivery_StopsItAndRunsAfterUnwinding()
        {
            Recorder first = new Recorder("a", m_log);
            first.OnNextAction = _ => m_holder.Event.SetCompleted();
            m_holder.Event.Add(first);
            m_holder.Event.Add(new Recorder("b", m_log));

            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1", "a:completed", "b:completed" }));
        }

        [Test]
        public void TerminalCallFromADelivery_FirstCallWins()
        {
            Recorder first = new Recorder("a", m_log);
            first.OnNextAction = _ =>
            {
                m_holder.Event.SetCanceled(CancellationToken.None);
                m_holder.Event.SetError(new InvalidOperationException("ignored"));
            };
            m_holder.Event.Add(first);

            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1", "a:canceled" }));
            Assert.That(first.Error, Is.Null);
        }

        [Test]
        public void ThrowingTerminalHandler_IsLoggedAndDoesNotStopTheOthers()
        {
            Recorder thrower = new Recorder("a", m_log);
            thrower.OnCompletedAction = () => throw new InvalidOperationException("terminal boom");
            m_holder.Event.Add(thrower);
            m_holder.Event.Add(new Recorder("b", m_log));
            LogAssert.Expect(LogType.Exception, new Regex("terminal boom"));

            m_holder.Event.SetCompleted();

            Assert.That(m_log, Is.EqualTo(new[] { "a:completed", "b:completed" }));
        }

        [Test]
        public void Add_AndRemove_RejectNull_AndRemoveIgnoresUnregisteredHandlers()
        {
            Assert.Throws<ArgumentNullException>(() => m_holder.Event.Add(null));
            Assert.Throws<ArgumentNullException>(() => m_holder.Event.Remove(null));

            Recorder registered = new Recorder("a", m_log);
            m_holder.Event.Add(registered);
            Assert.DoesNotThrow(() => m_holder.Event.Remove(new Recorder("stranger", m_log)));

            m_holder.Event.SetResult(1);

            Assert.That(m_log, Is.EqualTo(new[] { "a:next:1" }));
        }

        [Test]
        public void Remove_LastHandler_ThenAdd_StartsAFreshList()
        {
            Recorder a = new Recorder("a", m_log);
            Recorder b = new Recorder("b", m_log);
            m_holder.Event.Add(a);
            m_holder.Event.Remove(a);
            m_holder.Event.Add(b);

            m_holder.Event.SetResult(3);

            Assert.That(m_log, Is.EqualTo(new[] { "b:next:3" }));
        }
    }
}
