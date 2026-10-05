using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;
using UnityEngine.TestTools;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Proves the order and ownership of <see cref="OnityCoveredSceneTransition" /> with fake covers, a fake scene
    /// change and a fake readiness wait: the change runs only once the cover shows, the cover hides only after the
    /// new scene is ready, one change runs at a time, a failed change reveals the old scene, a cancelled caller or
    /// disposal ends exactly what it owns, and the cover is chosen per change.
    /// </summary>
    [TestFixture]
    public sealed class OnityCoveredSceneTransitionTests
    {
        private const float k_timeoutSeconds = 5f;

        private List<string> m_log;
        private FakeCover m_defaultCover;
        private FakeReadiness m_readiness;
        private OnityCoveredSceneTransition m_transition;

        [SetUp]
        public void SetUp()
        {
            m_log = new List<string>();
            m_defaultCover = new FakeCover("default", m_log);
            m_readiness = new FakeReadiness(m_log);
            m_transition = new OnityCoveredSceneTransition(m_defaultCover, m_readiness);
        }

        [TearDown]
        public void TearDown()
        {
            m_transition.Dispose();
        }

        [UnityTest]
        public IEnumerator RunAsync_WithCover_ShowsChangesWaitsForTheSceneAndHides()
        {
            FakeChange change = new FakeChange(m_log);
            using CancellationTokenSource caller = new CancellationTokenSource();
            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, caller.Token);

            Assert.That(m_log, Is.EqualTo(new[] { "default show" }), "nothing changes before the cover shows");
            Assert.That(m_transition.IsRunning, Is.True);

            m_defaultCover.CompleteShow();
            yield return WaitUntil(() => m_log.Count == 2);
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "change" }));
            Assert.That(change.Token.CanBeCanceled, Is.True);
            Assert.That(change.Token, Is.Not.EqualTo(caller.Token), "the change gets the transition's own token");

            change.Complete();
            yield return WaitUntil(() => m_log.Count == 3);
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "change", "ready" }),
                "the cover holds until the new scene is ready");

            m_readiness.Complete();
            yield return WaitUntil(() => run.IsCompleted);
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "change", "ready", "default hide" }));
            Assert.That(run.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(run.Result, Is.True);
            Assert.That(m_transition.IsRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator RunAsync_WhileAChangeRuns_ReturnsFalseAndRunsNothing()
        {
            FakeChange first = new FakeChange(m_log);
            FakeChange second = new FakeChange(m_log);
            Task<bool> running = m_transition.RunAsync(first.RunAsync, true, CancellationToken.None);

            Task<bool> ignored = m_transition.RunAsync(second.RunAsync, true, CancellationToken.None);

            Assert.That(ignored.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(ignored.Result, Is.False);
            m_defaultCover.CompleteShow();
            first.Complete();
            m_readiness.Complete();
            yield return WaitUntil(() => running.IsCompleted);
            Assert.That(running.Result, Is.True);
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(0), "the ignored change never runs");
        }

        [Test]
        public void RunAsync_AfterDispose_ReturnsFalseAndRunsNothing()
        {
            FakeChange change = new FakeChange(m_log);
            m_transition.Dispose();

            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, CancellationToken.None);

            Assert.That(run.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(run.Result, Is.False);
            Assert.That(change.Calls, Is.EqualTo(0));
            Assert.That(m_log, Is.Empty);
        }

        [UnityTest]
        public IEnumerator RunAsync_FailedChange_HidesTheCoverAndReportsTheFailure()
        {
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, CancellationToken.None);
            m_defaultCover.CompleteShow();
            yield return WaitUntil(() => change.Calls == 1);

            change.Fail(new InvalidOperationException("scene missing"));
            yield return WaitUntil(() => run.IsCompleted);

            Assert.That(m_log, Is.EqualTo(new[] { "default show", "change", "default hide" }),
                "no readiness wait for the old scene");
            Assert.That(run.IsFaulted, Is.True);
            Assert.That(run.Exception.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(m_transition.IsRunning, Is.False, "the caller can offer the action again");

            FakeChange retry = new FakeChange(m_log);
            m_transition.RunAsync(retry.RunAsync, true, CancellationToken.None);
            Assert.That(m_transition.IsRunning, Is.True, "a later request runs");
        }

        [UnityTest]
        public IEnumerator RunAsync_CallerCancelled_EndsOnlyTheCallersWait()
        {
            FakeChange change = new FakeChange(m_log);
            using CancellationTokenSource caller = new CancellationTokenSource();
            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, caller.Token);
            m_defaultCover.CompleteShow();
            yield return WaitUntil(() => change.Calls == 1);

            // The load disposes the scene that asked, which cancels its token.
            caller.Cancel();
            yield return WaitUntil(() => run.IsCompleted);
            Assert.That(run.IsCanceled, Is.True, "the caller's wait ends");
            Assert.That(change.Token.IsCancellationRequested, Is.False, "the change keeps its own token");
            Assert.That(m_transition.IsRunning, Is.True);

            change.Complete();
            m_readiness.Complete();
            yield return WaitUntil(() => m_transition.IsRunning == false);
            Assert.That(m_log, Is.EqualTo(new[] { "default show", "change", "ready", "default hide" }),
                "the new scene is still revealed");
        }

        [Test]
        public void RunAsync_PrecancelledCaller_StartsNothing()
        {
            FakeChange change = new FakeChange(m_log);

            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, new CancellationToken(true));

            Assert.That(run.IsCanceled, Is.True);
            Assert.That(m_log, Is.Empty);
            Assert.That(m_transition.IsRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator Dispose_StopsTheRunningChangesWaits()
        {
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, true, CancellationToken.None);

            m_transition.Dispose();
            yield return WaitUntil(() => run.IsCompleted);

            Assert.That(run.IsCanceled, Is.True, "the cover stopped on the lifetime token");
            Assert.That(change.Calls, Is.EqualTo(0));
            Assert.That(m_transition.IsRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator RunAsync_WithoutCover_RunsTheSameFlowWithNoCover()
        {
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, false, CancellationToken.None);
            yield return WaitUntil(() => change.Calls == 1);

            Task<bool> concurrent = m_transition.RunAsync(new FakeChange(m_log).RunAsync, false, CancellationToken.None);
            Assert.That(concurrent.Result, Is.False, "one change at a time, cover or not");

            change.Complete();
            yield return WaitUntil(() => m_log.Count == 2);
            Assert.That(m_log, Is.EqualTo(new[] { "change", "ready" }), "the readiness wait still runs");

            m_readiness.Complete();
            yield return WaitUntil(() => run.IsCompleted);
            Assert.That(run.Result, Is.True);
            Assert.That(m_defaultCover.ShowCalls, Is.EqualTo(0));
            Assert.That(m_defaultCover.HideCalls, Is.EqualTo(0));
            Assert.That(m_defaultCover.IsVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator RunAsync_WithoutCover_FailedChangeReportsTheFailure()
        {
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, false, CancellationToken.None);
            yield return WaitUntil(() => change.Calls == 1);

            change.Fail(new InvalidOperationException("scene missing"));
            yield return WaitUntil(() => run.IsCompleted);

            Assert.That(run.IsFaulted, Is.True);
            Assert.That(run.Exception.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(m_log, Is.EqualTo(new[] { "change" }));
            Assert.That(m_defaultCover.HideCalls, Is.EqualTo(0));
            Assert.That(m_transition.IsRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator RunAsync_WithAnotherCover_UsesOnlyThatCover()
        {
            FakeCover iris = new FakeCover("iris", m_log);
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, iris, CancellationToken.None);

            iris.CompleteShow();
            yield return WaitUntil(() => change.Calls == 1);
            change.Complete();
            m_readiness.Complete();
            yield return WaitUntil(() => run.IsCompleted);

            Assert.That(run.Result, Is.True);
            Assert.That(m_log, Is.EqualTo(new[] { "iris show", "change", "ready", "iris hide" }));
            Assert.That(m_defaultCover.ShowCalls, Is.EqualTo(0), "the default cover stays untouched");
            Assert.That(m_defaultCover.HideCalls, Is.EqualTo(0));
            Assert.That(m_transition.DefaultCover, Is.SameAs(m_defaultCover));
        }

        [UnityTest]
        public IEnumerator RunAsync_WithNullCover_RunsWithNoCover()
        {
            FakeChange change = new FakeChange(m_log);
            Task<bool> run = m_transition.RunAsync(change.RunAsync, (IOnitySceneCover)null, CancellationToken.None);
            yield return WaitUntil(() => change.Calls == 1);
            change.Complete();
            m_readiness.Complete();
            yield return WaitUntil(() => run.IsCompleted);

            Assert.That(run.Result, Is.True);
            Assert.That(m_log, Is.EqualTo(new[] { "change", "ready" }));
            Assert.That(m_defaultCover.ShowCalls, Is.EqualTo(0));
        }

        [Test]
        public void Constructor_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new OnityCoveredSceneTransition(null, m_readiness));
            Assert.Throws<ArgumentNullException>(() => new OnityCoveredSceneTransition(m_defaultCover, null));
            Assert.Throws<ArgumentNullException>(() => m_transition.RunAsync(null, true, CancellationToken.None));
        }

        // Continuations resume through the Editor's synchronization context, so each step yields until it has run.
        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + k_timeoutSeconds;

            while (condition() == false && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(condition(), Is.True, "the step never ran");
        }

        private sealed class FakeCover : IOnitySceneCover
        {
            private readonly string m_name;
            private readonly List<string> m_log;
            private TaskCompletionSource<bool> m_show;

            public FakeCover(string name, List<string> log)
            {
                m_name = name;
                m_log = log;
            }

            public bool IsVisible { get; private set; }

            public int ShowCalls { get; private set; }

            public int HideCalls { get; private set; }

            public Task ShowAsync(CancellationToken cancellationToken)
            {
                ShowCalls++;
                m_log.Add($"{m_name} show");
                IsVisible = true;
                m_show = new TaskCompletionSource<bool>();
                cancellationToken.Register(() => m_show.TrySetCanceled());
                return m_show.Task;
            }

            public Task HideAsync(CancellationToken cancellationToken)
            {
                HideCalls++;
                m_log.Add($"{m_name} hide");
                IsVisible = false;
                return Task.CompletedTask;
            }

            public void CompleteShow()
            {
                m_show.TrySetResult(true);
            }
        }

        private sealed class FakeReadiness : IOnitySceneReadiness
        {
            private readonly List<string> m_log;
            private readonly TaskCompletionSource<bool> m_ready = new TaskCompletionSource<bool>();

            public FakeReadiness(List<string> log)
            {
                m_log = log;
            }

            public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
            {
                m_log.Add("ready");
                return m_ready.Task;
            }

            public void Complete()
            {
                m_ready.TrySetResult(true);
            }
        }

        private sealed class FakeChange
        {
            private readonly List<string> m_log;
            private readonly TaskCompletionSource<bool> m_done = new TaskCompletionSource<bool>();

            public FakeChange(List<string> log)
            {
                m_log = log;
            }

            public int Calls { get; private set; }

            public CancellationToken Token { get; private set; }

            public Task RunAsync(CancellationToken cancellationToken)
            {
                Calls++;
                Token = cancellationToken;
                m_log.Add("change");
                return m_done.Task;
            }

            public void Complete()
            {
                m_done.TrySetResult(true);
            }

            public void Fail(Exception exception)
            {
                m_done.TrySetException(exception);
            }
        }
    }
}
