// Covers the PERF-4 fallback gate; compiled only with ONITY_RUNNER_RETURN_GATE (see OnityRunnerReturnGate).
#if ONITY_RUNNER_RETURN_GATE
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the return-on-unwind handshake that the IL2CPP state machine runners use to pool a
    /// runner whose result is consumed while a <c>MoveNext</c> of the same rental may still be on a
    /// stack. The gate compiles on every backend, so these tests drive it directly on Mono with a
    /// runner double that follows the IL2CPP runner shape: a release with no frame pools at once,
    /// a release inside a frame defers to the last frame's unwind, a nested release waits for the
    /// outer frame, a faulting frame still pools, and racing releases and unwinds on separate
    /// threads pool the runner exactly once with the word back at zero.
    /// </summary>
    [TestFixture]
    public sealed class OnityRunnerReturnGateEditModeTests
    {
        private const int k_raceIterations = 10000;
        private const int k_waitTimeoutMilliseconds = 10000;

        private delegate void FrameAction(ref int state);

        private delegate bool FrameDecision(ref int state);

        private static FrameAction s_enterFrame;
        private static FrameDecision s_exitFrame;
        private static FrameDecision s_requestReturn;
        private static int s_returnPending;

        [OneTimeSetUp]
        public void BindGate()
        {
            Type gateType = typeof(OnityTask).Assembly.GetType("Onity.Unity.Async.OnityRunnerReturnGate", true);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            s_enterFrame = (FrameAction)Delegate.CreateDelegate(
                typeof(FrameAction), gateType.GetMethod("EnterFrame", flags), true);
            s_exitFrame = (FrameDecision)Delegate.CreateDelegate(
                typeof(FrameDecision), gateType.GetMethod("ExitFrame", flags), true);
            s_requestReturn = (FrameDecision)Delegate.CreateDelegate(
                typeof(FrameDecision), gateType.GetMethod("RequestReturn", flags), true);
            s_returnPending = (int)gateType.GetField("k_returnPending", flags).GetRawConstantValue();
        }

        [Test]
        public void ReleaseWithNoFrameOnAnyStack_PoolsAtOnce()
        {
            RunnerDouble runner = new RunnerDouble();
            runner.MoveNext(null);
            Assert.That(runner.Returns, Is.Zero, "An unwind without a release must not pool.");
            Assert.That(runner.State, Is.Zero);

            runner.Release();

            Assert.That(runner.Returns, Is.EqualTo(1));
            Assert.That(runner.State, Is.Zero, "The next rental must count frames from zero.");
        }

        [Test]
        public void ReleaseInsideTheProducerFrame_PoolsAtItsUnwind_AndNotBefore()
        {
            RunnerDouble runner = new RunnerDouble();
            int stateAfterRelease = 0;
            int returnsAfterRelease = -1;

            // An inline continuation consumes the result inside the producer's MoveNext; a re-rent
            // there must not find this runner in the pool.
            runner.MoveNext(() =>
            {
                runner.Release();
                stateAfterRelease = runner.State;
                returnsAfterRelease = runner.Returns;
            });

            Assert.That(stateAfterRelease, Is.EqualTo(1 | s_returnPending));
            Assert.That(returnsAfterRelease, Is.Zero, "The runner was pooled under a running MoveNext.");
            Assert.That(runner.Returns, Is.EqualTo(1), "The unwinding frame did not pool the runner.");
            Assert.That(runner.State, Is.Zero);
        }

        [Test]
        public void ReleaseInsideANestedFrame_PoolsOnlyAtTheOuterUnwind()
        {
            RunnerDouble runner = new RunnerDouble();
            int returnsAfterInnerUnwind = -1;
            int stateAfterInnerUnwind = 0;

            runner.MoveNext(() =>
            {
                runner.MoveNext(runner.Release);
                returnsAfterInnerUnwind = runner.Returns;
                stateAfterInnerUnwind = runner.State;
            });

            Assert.That(returnsAfterInnerUnwind, Is.Zero, "The inner unwind pooled while the outer frame ran.");
            Assert.That(stateAfterInnerUnwind, Is.EqualTo(1 | s_returnPending));
            Assert.That(runner.Returns, Is.EqualTo(1));
            Assert.That(runner.State, Is.Zero);
        }

        [Test]
        public void FrameThatThrowsAfterTheRelease_StillPoolsExactlyOnce()
        {
            RunnerDouble runner = new RunnerDouble();

            Assert.Throws<InvalidOperationException>(() => runner.MoveNext(() =>
            {
                runner.Release();
                throw new InvalidOperationException("MoveNextCore fault");
            }));

            Assert.That(runner.Returns, Is.EqualTo(1));
            Assert.That(runner.State, Is.Zero);
        }

        [Test]
        public void FrameThatThrowsBeforeTheRelease_LeavesTheWordAtZero_AndTheLaterReleasePools()
        {
            RunnerDouble runner = new RunnerDouble();

            Assert.Throws<InvalidOperationException>(() => runner.MoveNext(
                () => throw new InvalidOperationException("MoveNextCore fault")));
            Assert.That(runner.State, Is.Zero);
            Assert.That(runner.Returns, Is.Zero);

            runner.Release();

            Assert.That(runner.Returns, Is.EqualTo(1));
            Assert.That(runner.State, Is.Zero);
        }

        [Test]
        public void SuccessiveRentals_EachPoolOnce_WhicheverSideFinishesLast()
        {
            RunnerDouble runner = new RunnerDouble();
            for (int rental = 0; rental < 4; rental++)
            {
                bool releaseInsideFrame = (rental & 1) == 0;
                runner.MoveNext(releaseInsideFrame ? runner.Release : (Action)null);
                if (!releaseInsideFrame)
                {
                    runner.Release();
                }

                Assert.That(runner.Returns, Is.EqualTo(rental + 1), "Rental " + rental + " did not pool exactly once.");
                Assert.That(runner.State, Is.Zero);
            }
        }

        [Test]
        public void ReleaseRacingTheProducerUnwindOnAnotherThread_PoolsExactlyOnce()
        {
            // A worker completed the method inside its MoveNext and is unwinding while the
            // consumer releases on another thread.
            RunnerDouble runner = new RunnerDouble();
            RunRace(runner, 1, new Func<RunnerDouble, bool>[] { ExitOneFrame, Release });
        }

        [Test]
        public void ReleaseRacingTwoFramesUnwindingOnTwoThreads_PoolsExactlyOnce()
        {
            // The thread that registered the last await is still unwinding its MoveNext while a
            // worker resumed, completed, and is unwinding a second frame of the same rental.
            RunnerDouble runner = new RunnerDouble();
            RunRace(runner, 2, new Func<RunnerDouble, bool>[] { ExitOneFrame, ExitOneFrame, Release });
        }

        private static bool ExitOneFrame(RunnerDouble runner)
        {
            return s_exitFrame(ref runner.State);
        }

        private static bool Release(RunnerDouble runner)
        {
            return s_requestReturn(ref runner.State);
        }

        /// <summary>
        /// Runs each participant on its own thread once per round after the frames were entered,
        /// with a deterministic per-round spin so every interleaving order occurs, and requires
        /// exactly one pooling decision and a zero word at the end of every round.
        /// </summary>
        private static void RunRace(RunnerDouble runner, int framesPerRound, Func<RunnerDouble, bool>[] participants)
        {
            RaceState race = new RaceState();
            Thread[] threads = new Thread[participants.Length];
            for (int i = 0; i < participants.Length; i++)
            {
                int index = i;
                Func<RunnerDouble, bool> participant = participants[i];
                threads[i] = new Thread(() => RunParticipant(race, runner, index, participant))
                {
                    IsBackground = true,
                    Name = "Onity return gate race " + i
                };
                threads[i].Start();
            }

            try
            {
                for (int round = 0; round < k_raceIterations; round++)
                {
                    Assert.That(Volatile.Read(ref runner.State), Is.Zero, "Round " + round + " started with a busy word.");
                    Volatile.Write(ref runner.Returns, 0);
                    for (int frame = 0; frame < framesPerRound; frame++)
                    {
                        s_enterFrame(ref runner.State);
                    }

                    Volatile.Write(ref race.Finished, 0);
                    Volatile.Write(ref race.Round, round);
                    bool finished = WaitForParticipants(race, participants.Length);

                    Assert.That(race.Failure, Is.Null, "A race participant failed.");
                    Assert.That(finished, Is.True, "Round " + round + " timed out.");
                    Assert.That(Volatile.Read(ref runner.Returns), Is.EqualTo(1), "Round " + round + " did not pool exactly once.");
                    Assert.That(Volatile.Read(ref runner.State), Is.Zero, "Round " + round + " left a non-zero word.");
                }
            }
            finally
            {
                Volatile.Write(ref race.Round, k_raceIterations);
                for (int i = 0; i < threads.Length; i++)
                {
                    threads[i].Join(k_waitTimeoutMilliseconds);
                }
            }
        }

        /// <summary>
        /// Waits for every participant of the current round. Yields instead of using
        /// <see cref="SpinWait"/>, whose sleep fallback costs a timer tick per round on Windows.
        /// </summary>
        private static bool WaitForParticipants(RaceState race, int participantCount)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (Volatile.Read(ref race.Finished) != participantCount && race.Failure == null)
            {
                if (elapsed.ElapsedMilliseconds > k_waitTimeoutMilliseconds)
                {
                    return false;
                }

                Thread.Yield();
            }

            return true;
        }

        private static void RunParticipant(RaceState race, RunnerDouble runner, int index, Func<RunnerDouble, bool> participant)
        {
            try
            {
                for (int round = 0; round < k_raceIterations; round++)
                {
                    // Yields for the same reason as WaitForParticipants; the main thread always
                    // publishes a final round, so this loop ends.
                    while (Volatile.Read(ref race.Round) < round)
                    {
                        Thread.Yield();
                    }

                    if (Volatile.Read(ref race.Round) >= k_raceIterations)
                    {
                        return;
                    }

                    uint mixed = unchecked((uint)(round + 1) * (uint)(index * 2 + 1) * 2654435761u);
                    Thread.SpinWait((int)(mixed >> 26));
                    if (participant(runner))
                    {
                        Interlocked.Increment(ref runner.Returns);
                    }

                    Interlocked.Increment(ref race.Finished);
                }
            }
            catch (Exception exception)
            {
                race.Failure = exception;
            }
        }

        /// <summary>
        /// Follows the IL2CPP runner shape: <c>MoveNext</c> counts its frame around the body and
        /// pools at the unwind the gate selects; the release pools when the gate says no frame is
        /// on a stack. Pooling is counted instead of performed.
        /// </summary>
        private sealed class RunnerDouble
        {
            public int State;
            public int Returns;

            public void MoveNext(Action body)
            {
                s_enterFrame(ref State);
                try
                {
                    body?.Invoke();
                }
                finally
                {
                    if (s_exitFrame(ref State))
                    {
                        Interlocked.Increment(ref Returns);
                    }
                }
            }

            public void Release()
            {
                if (s_requestReturn(ref State))
                {
                    Interlocked.Increment(ref Returns);
                }
            }
        }

        private sealed class RaceState
        {
            public int Round = -1;
            public int Finished;
            public volatile Exception Failure;
        }
    }
}
#endif
