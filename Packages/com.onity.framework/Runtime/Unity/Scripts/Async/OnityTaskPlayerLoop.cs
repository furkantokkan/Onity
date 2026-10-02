using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Onity.Unity.Async
{
    /// <summary>
    /// PlayerLoop positions of Onity task waits. The first three run right after the script callbacks
    /// of their phase and are installed eagerly. The others match UniTask's <c>PlayerLoopTiming</c>:
    /// a plain name runs at the start of its PlayerLoop phase and a <c>Last</c> name at its end; they
    /// are installed on first use or through <see cref="OnityTaskPlayerLoop.Initialize(OnityPlayerLoopTiming[])"/>.
    /// UniTask's <c>Update</c> and <c>FixedUpdate</c> are <see cref="UpdateBegin"/> and
    /// <see cref="FixedUpdateBegin"/> here, because <see cref="Update"/> and <see cref="FixedUpdate"/>
    /// already name the positions after the script callbacks.
    /// </summary>
    public enum OnityPlayerLoopTiming
    {
        /// <summary>Immediately after Update.ScriptRunBehaviourUpdate.</summary>
        Update = 0,
        /// <summary>Immediately after FixedUpdate.ScriptRunBehaviourFixedUpdate.</summary>
        FixedUpdate = 1,
        /// <summary>Immediately after PreLateUpdate.ScriptRunBehaviourLateUpdate.</summary>
        LateUpdate = 2,
        /// <summary>Start of the Initialization phase (UniTask <c>Initialization</c>).</summary>
        Initialization = 3,
        /// <summary>End of the Initialization phase (UniTask <c>LastInitialization</c>).</summary>
        LastInitialization = 4,
        /// <summary>Start of the EarlyUpdate phase (UniTask <c>EarlyUpdate</c>).</summary>
        EarlyUpdate = 5,
        /// <summary>End of the EarlyUpdate phase (UniTask <c>LastEarlyUpdate</c>).</summary>
        LastEarlyUpdate = 6,
        /// <summary>Start of the FixedUpdate phase, before the fixed script callbacks (UniTask <c>FixedUpdate</c>).</summary>
        FixedUpdateBegin = 7,
        /// <summary>End of the FixedUpdate phase (UniTask <c>LastFixedUpdate</c>).</summary>
        LastFixedUpdate = 8,
        /// <summary>Start of the PreUpdate phase (UniTask <c>PreUpdate</c>).</summary>
        PreUpdate = 9,
        /// <summary>End of the PreUpdate phase (UniTask <c>LastPreUpdate</c>).</summary>
        LastPreUpdate = 10,
        /// <summary>Start of the Update phase, before the script callbacks (UniTask <c>Update</c>).</summary>
        UpdateBegin = 11,
        /// <summary>End of the Update phase (UniTask <c>LastUpdate</c>).</summary>
        LastUpdate = 12,
        /// <summary>Start of the PreLateUpdate phase (UniTask <c>PreLateUpdate</c>).</summary>
        PreLateUpdate = 13,
        /// <summary>End of the PreLateUpdate phase (UniTask <c>LastPreLateUpdate</c>).</summary>
        LastPreLateUpdate = 14,
        /// <summary>Start of the PostLateUpdate phase (UniTask <c>PostLateUpdate</c>).</summary>
        PostLateUpdate = 15,
        /// <summary>End of the PostLateUpdate phase (UniTask <c>LastPostLateUpdate</c>).</summary>
        LastPostLateUpdate = 16,
        /// <summary>Start of the TimeUpdate phase (UniTask <c>TimeUpdate</c>).</summary>
        TimeUpdate = 17,
        /// <summary>End of the TimeUpdate phase (UniTask <c>LastTimeUpdate</c>).</summary>
        LastTimeUpdate = 18
    }

    /// <summary>
    /// Work item run at every drain of its PlayerLoop timing until <see cref="MoveNext"/> returns false;
    /// the equivalent of UniTask's <c>IPlayerLoopItem</c>.
    /// </summary>
    public interface IOnityPlayerLoopItem
    {
        /// <summary>Runs one step on Unity's main thread.</summary>
        /// <returns>True to run again at the next drain.</returns>
        bool MoveNext();
    }

    /// <summary>
    /// An Onity-owned PlayerLoop item that must learn about a session retirement, such as a timer that
    /// stops running. Plain <see cref="IOnityPlayerLoopItem"/> instances are dropped silently.
    /// </summary>
    internal interface IOnityRetirablePlayerLoopItem : IOnityPlayerLoopItem
    {
        /// <summary>Called on the main thread when the session retires the queued item.</summary>
        void Retire(Exception failure);
    }

    /// <summary>
    /// A pooled wait polled at every drain of its timing. Its queue entry carries the version of the cycle
    /// that queued it, so an entry left behind by a cycle that published elsewhere (an immediate
    /// cancellation) is dropped instead of polling a later rental of the same object.
    /// </summary>
    internal interface IOnityLoopWait
    {
        /// <summary>True while the cycle that issued <paramref name="version"/> has not published.</summary>
        bool IsAwaitingPublication(int version);

        /// <summary>True when a flag-only cancellation waits for the main thread.</summary>
        bool IsCancellationFlagged { get; }

        /// <summary>Polls the wait on the main thread; returns true to stay queued.</summary>
        bool Poll(int version);

        /// <summary>Publishes the flagged cancellation of the cycle; main thread.</summary>
        void PublishCancellation(int version);

        /// <summary>Publishes the retirement of the cycle: canceled, or the repair failure; main thread.</summary>
        void Retire(int version, Exception failure);
    }

    /// <summary>
    /// Owns Onity's PlayerLoop timing nodes, independently of the legacy runner. In Play, token-less
    /// default frame waits are stateless: a task holds a per-session marker and a target, and a
    /// registered continuation is one entry in a lock-free main-thread queue drained by the node.
    /// </summary>
    [Il2CppEagerStaticClassConstruction]
    [Il2CppSetOption(Option.NullChecks, false)]
    public static class OnityTaskPlayerLoop
    {
        private const int k_kindYield = 0;
        private const int k_kindFrame = 1;
        private const int k_kindSource = 2;
        private const int k_kindItem = 3;
        private const int k_kindLoopWait = 4;
        private const int k_kindPost = 5;
        private const int k_eagerTimingCount = 3;
        private const int k_eagerQueueCapacity = 64;

        private sealed class UpdateMarker
        {
        }

        private sealed class FixedUpdateMarker
        {
        }

        private sealed class LateUpdateMarker
        {
        }

        private sealed class InitializationMarker
        {
        }

        private sealed class LastInitializationMarker
        {
        }

        private sealed class EarlyUpdateMarker
        {
        }

        private sealed class LastEarlyUpdateMarker
        {
        }

        private sealed class FixedUpdateBeginMarker
        {
        }

        private sealed class LastFixedUpdateMarker
        {
        }

        private sealed class PreUpdateMarker
        {
        }

        private sealed class LastPreUpdateMarker
        {
        }

        private sealed class UpdateBeginMarker
        {
        }

        private sealed class LastUpdateMarker
        {
        }

        private sealed class PreLateUpdateMarker
        {
        }

        private sealed class LastPreLateUpdateMarker
        {
        }

        private sealed class PostLateUpdateMarker
        {
        }

        private sealed class LastPostLateUpdateMarker
        {
        }

        private sealed class TimeUpdateMarker
        {
        }

        private sealed class LastTimeUpdateMarker
        {
        }

        /// <summary>One queued wait: a continuation with its target, or a pooled source with its version.</summary>
        private struct Entry
        {
            internal object Item;
            internal int Token;
            internal int Kind;
        }

        /// <summary>
        /// Double-buffered queue of one phase, touched only by Unity's main thread. Registrations append
        /// to the incoming buffer; a drain swaps the buffers and processes the entries registered before
        /// it, so a wait registered during a drain runs at the next occurrence. Slots are cleared with
        /// <c>default</c>, which needs no write barrier.
        /// </summary>
        [Il2CppSetOption(Option.NullChecks, false)]
        private sealed class WaitQueue : System.Collections.ICollection
        {
            private const int k_minimumCapacity = 4;

            internal Entry[] Incoming;
            internal int IncomingCount;
            internal Entry[] Draining;
            internal int DrainingCount;

            internal WaitQueue(int capacity)
            {
                Incoming = capacity == 0 ? Array.Empty<Entry>() : new Entry[capacity];
                Draining = capacity == 0 ? Array.Empty<Entry>() : new Entry[capacity];
            }

            /// <summary>Entries that have not been drained yet, including stale ones.</summary>
            public int Count => IncomingCount + DrainingCount;

            bool System.Collections.ICollection.IsSynchronized => false;

            object System.Collections.ICollection.SyncRoot => this;

            // Diagnostics only (benchmark self-tests read the queues through ICollection).
            void System.Collections.ICollection.CopyTo(Array array, int index)
            {
                foreach (object item in this)
                {
                    array.SetValue(item, index++);
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                var items = new List<object>(Count);
                for (int i = 0; i < DrainingCount; i++)
                {
                    items.Add(Draining[i].Item);
                }

                for (int i = 0; i < IncomingCount; i++)
                {
                    items.Add(Incoming[i].Item);
                }

                return items.GetEnumerator();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal void Add(object item, int token, int kind)
            {
                int count = IncomingCount;
                Entry[] items = Incoming;
                if (count == items.Length)
                {
                    items = Grow();
                }

                items[count].Item = item;
                items[count].Token = token;
                items[count].Kind = kind;
                IncomingCount = count + 1;
            }

            internal void BeginDrain()
            {
                Entry[] drained = Draining;
                Draining = Incoming;
                DrainingCount = IncomingCount;
                Incoming = drained;
                IncomingCount = 0;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private Entry[] Grow()
            {
                Entry[] grown = new Entry[Math.Max(k_minimumCapacity, Incoming.Length * 2)];
                Array.Copy(Incoming, grown, IncomingCount);
                Incoming = grown;
                return grown;
            }
        }

        /// <summary>
        /// Stateless wait of one phase and session. The task token is a target compared against the
        /// stamp the phase drain writes, so a task needs no per-wait object and can be awaited any number
        /// of times. A retired marker freezes its stamp, so tasks of a closed session never complete later.
        /// </summary>
        [Il2CppSetOption(Option.NullChecks, false)]
        private sealed class WaitMarker : OnityTaskCore, IOnityMultiConsumerTaskSource
        {
            private readonly Phase m_phase;
            private readonly int m_kind;
            private Exception m_failure;
            private int m_bridgeToken;
            private Task m_bridge;

            internal int Stamp;
            internal bool Retired;

            internal WaitMarker(Phase phase, int kind, int stamp)
            {
                m_phase = phase;
                m_kind = kind;
                Stamp = stamp;
            }

            internal override bool IsStatelessWait => true;

            internal override int CoreVersion => 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool IsDue(int token)
            {
                return unchecked(Stamp - token) >= 0;
            }

            internal void Retire(Exception failure)
            {
                m_failure = failure;
                Volatile.Write(ref Retired, true);
            }

            internal override OnityTaskSourceStatus GetCoreStatus(int token)
            {
                if (IsDue(token))
                {
                    return OnityTaskSourceStatus.Succeeded;
                }

                if (Retired)
                {
                    return m_failure != null ? OnityTaskSourceStatus.Faulted : OnityTaskSourceStatus.Canceled;
                }

                return OnityTaskSourceStatus.Pending;
            }

            internal override void OnCoreCompleted(Action continuation, int token)
            {
                if (continuation == null)
                {
                    throw new ArgumentNullException(nameof(continuation));
                }

                if (IsDue(token) || Retired)
                {
                    continuation();
                    return;
                }

                if (s_isMainThread)
                {
                    m_phase.Pending.Add(continuation, token, m_kind);
                    return;
                }

                EnqueueFromWorker(m_phase, continuation, token, m_kind);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal override void ConsumeCore(int token)
            {
                if (!IsDue(token))
                {
                    ThrowNotDue();
                }
            }

            internal override Task AsCoreTask(int token)
            {
                if (IsDue(token))
                {
                    return Task.CompletedTask;
                }

                if (s_isMainThread && m_bridge != null && m_bridgeToken == token)
                {
                    return m_bridge;
                }

                TaskCompletionSource<bool> bridge = OnityAsyncExecutionContext.CreateTaskBridge<bool>();
                OnCoreCompleted(() => CompleteBridge(bridge, token), token);
                if (s_isMainThread && !bridge.Task.IsCompleted)
                {
                    m_bridgeToken = token;
                    m_bridge = bridge.Task;
                }

                return bridge.Task;
            }

            private void CompleteBridge(TaskCompletionSource<bool> bridge, int token)
            {
                if (IsDue(token))
                {
                    bridge.TrySetResult(true);
                }
                else if (m_failure != null)
                {
                    bridge.TrySetException(m_failure);
                }
                else
                {
                    bridge.TrySetCanceled();
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void ThrowNotDue()
            {
                if (!Retired)
                {
                    throw new InvalidOperationException("OnityTask is not completed.");
                }

                if (m_failure != null)
                {
                    ExceptionDispatchInfo.Capture(m_failure).Throw();
                }

                throw new OperationCanceledException();
            }
        }

        /// <summary>
        /// One timing: its node placement, its continuation queue, its item queue (user items and pooled
        /// polled waits, run after the continuations of the same drain) and its session markers.
        /// </summary>
        private sealed class Phase
        {
            internal readonly WaitQueue Pending;
            internal readonly WaitQueue Items;
            internal ulong Pass;
            internal int LastDrainFrame;
            internal bool Draining;
            internal WaitMarker FrameMarker;
            internal WaitMarker YieldMarker;
            internal readonly object InboxGate = new object();
            internal List<Entry> Inbox = new List<Entry>(4);
            internal int InboxCount;

            // Node placement: after Anchor inside Parent when Anchor is set, else first or last in Parent.
            internal readonly Type NodeType;
            internal readonly Type Parent;
            internal readonly Type Anchor;
            internal readonly bool AtEnd;
            internal readonly PlayerLoopSystem.UpdateFunction Drainer;

            // Marker stamps before the current drain; a retirement during the drain restores them.
            internal int PreviousFrameStamp;
            internal int PreviousYieldStamp;

            // Installed by Initialize: always for the eager timings, after first use for the others.
            internal bool Requested;

            internal Phase(
                Type nodeType, Type parent, Type anchor, bool atEnd,
                PlayerLoopSystem.UpdateFunction drainer, bool eager)
            {
                NodeType = nodeType;
                Parent = parent;
                Anchor = anchor;
                AtEnd = atEnd;
                Drainer = drainer;
                Requested = eager;
                Pending = new WaitQueue(eager ? k_eagerQueueCapacity : 0);
                Items = new WaitQueue(0);
            }
        }

        internal sealed class TimeoutEntry
        {
            internal readonly int Generation;
            internal readonly int Epoch;
            internal readonly ulong RegisteredPass;
            internal readonly double StartTime;
            internal readonly double Duration;
            internal readonly bool UseUnscaledTime;
            internal IOnityTimeoutTimerSink Sink;
            internal int StopRequested;

            internal TimeoutEntry(
                int generation, int epoch, ulong pass, double startTime, double duration,
                bool useUnscaledTime, IOnityTimeoutTimerSink sink)
            {
                Generation = generation;
                Epoch = epoch;
                RegisteredPass = pass;
                StartTime = startTime;
                Duration = duration;
                UseUnscaledTime = useUnscaledTime;
                Sink = sink;
            }
        }

        private const int k_timingCount = 19;

        private static readonly Phase[] s_phases = CreatePhases();

        // Queue lengths captured before the flagged-cancellation scan publishes anything, so waits
        // registered by those publications wait for a later drain.
        private static readonly int[] s_scanPendingCounts = new int[k_timingCount];
        private static readonly int[] s_scanItemCounts = new int[k_timingCount];
        private static List<TimeoutEntry> s_timers = new List<TimeoutEntry>(32);
        private static List<TimeoutEntry> s_spareTimers = new List<TimeoutEntry>(32);
        private static ulong s_timerPass;
        private static int s_timerGeneration;
        private static int s_drainDepth;
        private static bool s_installPending;
        private static SynchronizationContext s_unitySynchronizationContext;
        private static int s_mainThreadId;
        private static bool s_sessionOpen;
        private static bool s_accepting;
        private static bool s_installed;
        private static bool s_retiring;
        private static bool s_shuttingDown;
        private static OnityEndOfFrameRunner s_endOfFrameRunner;
        private static bool s_retiringEndOfFrame;
        private static Exception s_retirementFailure;
        private static int s_cancellationRequests;
        private static int s_lastStampedFrame;

        // Fast-path state of the default waits, maintained on every session transition.
        private static bool s_defaultWaitsOpen;
        private static WaitMarker s_updateFrameMarker;
        private static WaitMarker s_fixedYieldMarker;
        private static WaitMarker s_lateYieldMarker;

        [ThreadStatic]
        private static bool s_isMainThread;

        /// <summary>Session epoch; it changes whenever pending timing work is retired.</summary>
        internal static int s_epoch;

        private static Phase[] CreatePhases()
        {
            // Eager timings run right after the script callbacks of their phase. The others follow
            // UniTask's injection: a plain timing first in its phase, a Last timing at its end.
            return new[]
            {
                new Phase(typeof(UpdateMarker), typeof(UnityEngine.PlayerLoop.Update),
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate), false, DrainUpdate, true),
                new Phase(typeof(FixedUpdateMarker), typeof(UnityEngine.PlayerLoop.FixedUpdate),
                    typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate), false, () => Drain(1), true),
                new Phase(typeof(LateUpdateMarker), typeof(UnityEngine.PlayerLoop.PreLateUpdate),
                    typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate), false, () => Drain(2), true),
                new Phase(typeof(InitializationMarker), typeof(UnityEngine.PlayerLoop.Initialization), null, false,
                    () => Drain(3), false),
                new Phase(typeof(LastInitializationMarker), typeof(UnityEngine.PlayerLoop.Initialization), null, true,
                    () => Drain(4), false),
                new Phase(typeof(EarlyUpdateMarker), typeof(UnityEngine.PlayerLoop.EarlyUpdate), null, false,
                    () => Drain(5), false),
                new Phase(typeof(LastEarlyUpdateMarker), typeof(UnityEngine.PlayerLoop.EarlyUpdate), null, true,
                    () => Drain(6), false),
                new Phase(typeof(FixedUpdateBeginMarker), typeof(UnityEngine.PlayerLoop.FixedUpdate), null, false,
                    () => Drain(7), false),
                new Phase(typeof(LastFixedUpdateMarker), typeof(UnityEngine.PlayerLoop.FixedUpdate), null, true,
                    () => Drain(8), false),
                new Phase(typeof(PreUpdateMarker), typeof(UnityEngine.PlayerLoop.PreUpdate), null, false,
                    () => Drain(9), false),
                new Phase(typeof(LastPreUpdateMarker), typeof(UnityEngine.PlayerLoop.PreUpdate), null, true,
                    () => Drain(10), false),
                new Phase(typeof(UpdateBeginMarker), typeof(UnityEngine.PlayerLoop.Update), null, false,
                    () => Drain(11), false),
                new Phase(typeof(LastUpdateMarker), typeof(UnityEngine.PlayerLoop.Update), null, true,
                    () => Drain(12), false),
                new Phase(typeof(PreLateUpdateMarker), typeof(UnityEngine.PlayerLoop.PreLateUpdate), null, false,
                    () => Drain(13), false),
                new Phase(typeof(LastPreLateUpdateMarker), typeof(UnityEngine.PlayerLoop.PreLateUpdate), null, true,
                    () => Drain(14), false),
                new Phase(typeof(PostLateUpdateMarker), typeof(UnityEngine.PlayerLoop.PostLateUpdate), null, false,
                    () => Drain(15), false),
                new Phase(typeof(LastPostLateUpdateMarker), typeof(UnityEngine.PlayerLoop.PostLateUpdate), null, true,
                    () => Drain(16), false),
                new Phase(typeof(TimeUpdateMarker), typeof(UnityEngine.PlayerLoop.TimeUpdate), null, false,
                    () => Drain(17), false),
                new Phase(typeof(LastTimeUpdateMarker), typeof(UnityEngine.PlayerLoop.TimeUpdate), null, true,
                    () => Drain(18), false)
            };
        }

        /// <summary>
        /// Installs or repairs Onity's nodes in the current PlayerLoop, retaining pending waits.
        /// </summary>
        /// <remarks>Requires Unity's main thread and active Play/player execution. Call
        /// after an external whole-loop replacement. Missing anchors fault pending waits
        /// and reject new acceptance; restore the anchors and explicitly retry this method.
        /// This method does not restore a cached or default loop.</remarks>
        /// <exception cref="InvalidOperationException">The session is unavailable or
        /// the current PlayerLoop does not contain all required anchors exactly once.</exception>
        public static void Initialize()
        {
            ValidateContext();
            try
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemoveOwned(ref loop);
                for (int i = 0; i < s_phases.Length; i++)
                {
                    Phase phase = s_phases[i];
                    if (!phase.Requested)
                    {
                        continue;
                    }

                    var node = new PlayerLoopSystem { type = phase.NodeType, updateDelegate = phase.Drainer };
                    bool inserted;
                    if (phase.Anchor != null)
                    {
                        ValidateAnchor(loop, phase.Parent, phase.Anchor);
                        inserted = InsertAfter(ref loop, phase.Anchor, node);
                    }
                    else
                    {
                        if (CountType(loop, phase.Parent) != 1)
                        {
                            throw new InvalidOperationException(
                                "Onity timing phase missing or duplicated: " + phase.Parent.FullName);
                        }

                        inserted = InsertInPhase(ref loop, phase.Parent, node, phase.AtEnd);
                    }

                    if (!inserted)
                    {
                        throw new InvalidOperationException("Onity timing node could not be inserted: " + phase.NodeType.Name);
                    }
                }

                PlayerLoop.SetPlayerLoop(loop);
                s_installed = true;
                s_accepting = true;
                EnsureSessionMarkers();
                UpdateDefaultWaitsOpen();
            }
            catch (Exception exception)
            {
                s_accepting = false;
                s_installed = false;
                s_epoch++;
                UpdateDefaultWaitsOpen();
                RemoveNodes();
                Retire(exception);
                throw;
            }
        }

        /// <summary>
        /// Installs or repairs Onity's nodes and additionally installs the nodes of
        /// <paramref name="timings"/>, which otherwise install on first use. The equivalent of UniTask's
        /// <c>PlayerLoopHelper.Initialize</c> with an injection set.
        /// </summary>
        /// <param name="timings">Timings whose nodes are installed now and on every later repair.</param>
        /// <exception cref="ArgumentNullException"><paramref name="timings"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A timing is not defined.</exception>
        /// <exception cref="InvalidOperationException">See <see cref="Initialize()"/>.</exception>
        public static void Initialize(params OnityPlayerLoopTiming[] timings)
        {
            if (timings == null)
            {
                throw new ArgumentNullException(nameof(timings));
            }

            for (int i = 0; i < timings.Length; i++)
            {
                ValidateTiming(timings[i]);
            }

            ValidateContext();
            for (int i = 0; i < timings.Length; i++)
            {
                s_phases[(int)timings[i]].Requested = true;
            }

            Initialize();
        }

        /// <summary>
        /// Installs or repairs Onity's nodes and installs the node of every timing; the equivalent of
        /// UniTask's <c>InjectPlayerLoopTimings.All</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">See <see cref="Initialize()"/>.</exception>
        public static void InitializeAll()
        {
            ValidateContext();
            for (int i = 0; i < s_phases.Length; i++)
            {
                s_phases[i].Requested = true;
            }

            Initialize();
        }

        /// <summary>True when the node of <paramref name="timing"/> is installed in the current PlayerLoop.</summary>
        /// <param name="timing">PlayerLoop timing.</param>
        /// <returns>Whether the node is present.</returns>
        public static bool IsInjected(OnityPlayerLoopTiming timing)
        {
            ValidateTiming(timing);
            return CountType(PlayerLoop.GetCurrentPlayerLoop(), s_phases[(int)timing].NodeType) != 0;
        }

        /// <summary>Describes the current PlayerLoop as an indented tree, marking Onity's nodes.</summary>
        /// <returns>PlayerLoop description.</returns>
        public static string DumpCurrentPlayerLoop()
        {
            var builder = new System.Text.StringBuilder();
            DescribeSystem(PlayerLoop.GetCurrentPlayerLoop(), 0, builder);
            return builder.ToString();
        }

        /// <summary>
        /// Runs <paramref name="item"/> at every drain of <paramref name="timing"/> until its
        /// <see cref="IOnityPlayerLoopItem.MoveNext"/> returns false; a thrown exception is logged and ends
        /// the item. May be called from any thread; the item always runs on Unity's main thread.
        /// </summary>
        /// <param name="timing">PlayerLoop timing.</param>
        /// <param name="item">Work item.</param>
        /// <exception cref="InvalidOperationException">No Play/player session is accepting work.</exception>
        public static void AddAction(OnityPlayerLoopTiming timing, IOnityPlayerLoopItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Enqueue(timing, item, 0, k_kindItem);
        }

        /// <summary>
        /// Queues a pooled polled wait for the drains of <paramref name="timing"/>, first polled in the next
        /// item pass of that timing (the current one when its continuations are running). Main thread only:
        /// callers validate the context first.
        /// </summary>
        internal static void AddLoopWait(OnityPlayerLoopTiming timing, IOnityLoopWait wait, int version)
        {
            Phase phase = s_phases[(int)timing];
            EnsureInstalled(phase);
            phase.Items.Add(wait, version, k_kindLoopWait);
        }

        /// <summary>
        /// Runs <paramref name="continuation"/> once at the next drain of <paramref name="timing"/>; an
        /// exception it throws is logged. May be called from any thread; the continuation runs on Unity's
        /// main thread. A session exit drops pending continuations without running them.
        /// </summary>
        /// <param name="timing">PlayerLoop timing.</param>
        /// <param name="continuation">Callback.</param>
        /// <exception cref="ArgumentNullException"><paramref name="continuation"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="timing"/> is not defined.</exception>
        /// <exception cref="InvalidOperationException">No Play/player session is accepting work.</exception>
        public static void AddContinuation(OnityPlayerLoopTiming timing, Action continuation)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            Enqueue(timing, continuation, 0, k_kindPost);
        }

        /// <summary>True on Unity's main thread.</summary>
        public static bool IsMainThread => s_isMainThread;

        /// <summary>Managed thread id of Unity's main thread.</summary>
        public static int MainThreadId => s_mainThreadId;

        /// <summary>Unity's synchronization context captured on the main thread at startup.</summary>
        public static SynchronizationContext UnitySynchronizationContext => s_unitySynchronizationContext;

        /// <summary>
        /// Creates a token-less wait for <paramref name="frames"/> later rendered frames at the Update
        /// node. In Play on the main thread this is a stateless task: <see cref="Time.frameCount"/> is
        /// read once and no object is rented. Other contexts take the slow path: a worker targets the
        /// frame after the last drained one, Edit Mode keeps the legacy runner, and a closing session
        /// rejects the call.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static OnityTask CreateFrameWait(int frames)
        {
            if (s_defaultWaitsOpen && s_isMainThread)
            {
                return new OnityTask(s_updateFrameMarker, unchecked(Time.frameCount + frames));
            }

            return CreateFrameWaitSlow(frames);
        }

        /// <summary>
        /// Creates a token-less wait for the next drain of a Fixed or Late node, as the legacy
        /// <c>NextFixedFrame</c> and <c>NextLateFrame</c> waits behaved.
        /// </summary>
        internal static OnityTask CreateNextDrainWait(OnityTaskLoopPhase phase)
        {
            if (s_defaultWaitsOpen || TryOpenDefaultWaits())
            {
                WaitMarker marker = phase == OnityTaskLoopPhase.FixedUpdate ? s_fixedYieldMarker : s_lateYieldMarker;
                Phase owner = s_phases[phase == OnityTaskLoopPhase.FixedUpdate ? 1 : 2];
                return new OnityTask(marker, unchecked((int)owner.Pass + 1));
            }

            return CreateLegacyOrReject(phase, 1, default);
        }

        /// <summary>
        /// Creates a cancelable default frame wait: a pooled source on the scheduler for main-thread
        /// Play callers, the legacy runner otherwise (Edit Mode, workers).
        /// </summary>
        internal static OnityTask CreateCancelableDefaultWait(
            OnityTaskLoopPhase phase, int frames, CancellationToken token)
        {
            if (s_isMainThread && (s_defaultWaitsOpen || TryOpenDefaultWaits()))
            {
                OnityPlayerLoopTiming timing = phase == OnityTaskLoopPhase.FixedUpdate
                    ? OnityPlayerLoopTiming.FixedUpdate
                    : phase == OnityTaskLoopPhase.LateUpdate ? OnityPlayerLoopTiming.LateUpdate : OnityPlayerLoopTiming.Update;
                return phase == OnityTaskLoopPhase.Update
                    ? Schedule(timing, frames, false, token)
                    : Schedule(timing, 0, true, token);
            }

            return CreateLegacyOrReject(phase, frames, token);
        }

        /// <summary>
        /// Queues a continuation for the next drain of <paramref name="timing"/>, from any thread. Backs
        /// <see cref="OnityYieldAwaiter"/>.
        /// </summary>
        internal static void EnqueueYield(OnityPlayerLoopTiming timing, Action continuation)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            ValidateYield(timing);
            Phase phase = s_phases[(int)timing];
            if (s_isMainThread && s_installed && phase.Requested)
            {
                // The awaited Yield path: one append to the timing's continuation queue.
                phase.Pending.Add(continuation, 0, k_kindYield);
                return;
            }

            Enqueue(timing, continuation, 0, k_kindYield);
        }

        /// <summary>
        /// Queues an entry for the next drain of <paramref name="timing"/> from any thread: a direct
        /// append on the main thread, the locked inbox from a worker. A main-thread caller installs the
        /// timing's node on first use.
        /// </summary>
        private static void Enqueue(OnityPlayerLoopTiming timing, object item, int token, int kind)
        {
            ValidateYield(timing);
            Phase phase = s_phases[(int)timing];
            if (s_isMainThread)
            {
                EnsureInstalled(phase);
                (kind == k_kindItem ? phase.Items : phase.Pending).Add(item, token, kind);
                return;
            }

            EnsureInstalled(phase);
            EnqueueFromWorker(phase, item, token, kind);
        }

        /// <summary>Validates that a Yield awaitable may be created now.</summary>
        internal static void ValidateYield(OnityPlayerLoopTiming timing)
        {
            ValidateTiming(timing);
            if (!s_accepting || !s_sessionOpen || s_retiring || s_shuttingDown)
            {
                throw new InvalidOperationException("Onity timing waits require an active Play/player session.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateTiming(OnityPlayerLoopTiming timing)
        {
            if ((uint)timing >= k_timingCount)
            {
                throw new ArgumentOutOfRangeException(nameof(timing));
            }
        }

        /// <summary>
        /// Installs a timing's node on its first use. A main-thread request outside Onity's drains
        /// installs now; a request made during a drain, or from a worker, installs when the current or
        /// next outermost drain ends, so Onity never replaces the loop while it iterates its own queues.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureInstalled(Phase phase)
        {
            if (s_installed && phase.Requested)
            {
                return;
            }

            RequestInstall(phase);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RequestInstall(Phase phase)
        {
            phase.Requested = true;
            if (!s_isMainThread || s_drainDepth > 0)
            {
                // The release write publishes Requested to the main thread that reads this flag.
                Volatile.Write(ref s_installPending, true);
                return;
            }

            Initialize();
        }

        /// <summary>Runs an installation deferred by <see cref="RequestInstall"/> at the end of a drain.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InstallDeferred()
        {
            Volatile.Write(ref s_installPending, false);
            if (!s_sessionOpen || !s_accepting || s_retiring || s_shuttingDown)
            {
                // A closed session reinstalls every requested node at its next start; an unrepaired one
                // waits for the explicit Initialize call.
                return;
            }

            try
            {
                Initialize();
            }
            catch (Exception exception)
            {
                // Initialize already retired the session with this failure.
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>A stateless task for the next drain of <paramref name="timing"/>.</summary>
        internal static OnityTask CreateYieldTask(OnityPlayerLoopTiming timing)
        {
            ValidateYield(timing);
            Phase phase = s_phases[(int)timing];
            if (phase.YieldMarker == null || phase.YieldMarker.Retired)
            {
                throw new InvalidOperationException("Onity timing waits require an active Play/player session.");
            }

            EnsureInstalled(phase);
            return new OnityTask(phase.YieldMarker, unchecked((int)phase.Pass + 1));
        }

        /// <summary>Throws the outcome of a retired Yield awaiter: the repair failure or a cancellation.</summary>
        internal static void ThrowRetired()
        {
            Exception failure = s_retirementFailure;
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            throw new OperationCanceledException();
        }

        /// <summary>
        /// Pending timing work: queued waits (including entries not drained yet), worker inboxes and
        /// timers. Diagnostics for tests and benchmarks; main thread only.
        /// </summary>
        internal static int PendingWorkCount
        {
            get
            {
                int count = s_timers.Count;
                for (int i = 0; i < s_phases.Length; i++)
                {
                    Phase phase = s_phases[i];
                    count += phase.Pending.Count + phase.Items.Count + Volatile.Read(ref phase.InboxCount);
                }

                return count;
            }
        }

        /// <summary>Pending work of one timing; see <see cref="PendingWorkCount"/>.</summary>
        internal static int GetPendingCount(OnityPlayerLoopTiming timing)
        {
            Phase phase = s_phases[(int)timing];
            return phase.Pending.Count + phase.Items.Count + Volatile.Read(ref phase.InboxCount);
        }

        /// <summary>Records a flag-only cancellation so the next Update drain publishes it.</summary>
        internal static void NotifyCancellationRequested()
        {
            Interlocked.Increment(ref s_cancellationRequests);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static OnityTask CreateFrameWaitSlow(int frames)
        {
            if (!s_isMainThread)
            {
                if (s_defaultWaitsOpen)
                {
                    // A worker cannot read Time.frameCount; target the frame after the last drained one.
                    return new OnityTask(s_updateFrameMarker, unchecked(Volatile.Read(ref s_lastStampedFrame) + frames));
                }

                return CreateLegacyOrReject(OnityTaskLoopPhase.Update, frames, default);
            }

            if (TryOpenDefaultWaits())
            {
                return new OnityTask(s_updateFrameMarker, unchecked(Time.frameCount + frames));
            }

            return CreateLegacyOrReject(OnityTaskLoopPhase.Update, frames, default);
        }

        /// <summary>
        /// Installs the nodes on first use when a main-thread Play caller arrives before the
        /// BeforeSceneLoad installation. Returns whether the default waits are open.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryOpenDefaultWaits()
        {
            if (!s_defaultWaitsOpen && s_isMainThread && s_sessionOpen && s_accepting && !s_retiring
                && !s_shuttingDown && !s_installed && Application.isPlaying)
            {
                Initialize();
            }

            return s_defaultWaitsOpen;
        }

        /// <summary>
        /// Edit Mode and worker callers keep the legacy runner; a closing or unrepaired Play session
        /// rejects the call like the explicit timing waits.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static OnityTask CreateLegacyOrReject(OnityTaskLoopPhase phase, int frames, CancellationToken token)
        {
            if (s_isMainThread && Application.isPlaying)
            {
                if (s_sessionOpen && !s_accepting && !s_retiring && !s_shuttingDown)
                {
                    throw new InvalidOperationException("The Onity timing loop requires a successful explicit repair.");
                }

                if (!s_sessionOpen || s_retiring || s_shuttingDown)
                {
                    throw new InvalidOperationException("Onity timing waits require an active Play/player session.");
                }
            }

            return new OnityTask(OnityFrameTaskSource.Rent(phase, token, frames));
        }

        private static void EnqueueFromWorker(Phase phase, object item, int token, int kind)
        {
            lock (phase.InboxGate)
            {
                phase.Inbox.Add(new Entry { Item = item, Token = token, Kind = kind });
                Volatile.Write(ref phase.InboxCount, phase.Inbox.Count);
            }
        }

        private static void MergeInbox(Phase phase)
        {
            lock (phase.InboxGate)
            {
                List<Entry> inbox = phase.Inbox;
                for (int i = 0; i < inbox.Count; i++)
                {
                    Entry entry = inbox[i];
                    (entry.Kind == k_kindItem ? phase.Items : phase.Pending).Add(entry.Item, entry.Token, entry.Kind);
                }

                inbox.Clear();
                Volatile.Write(ref phase.InboxCount, 0);
            }
        }

        private static void EnsureSessionMarkers()
        {
            for (int i = 0; i < s_phases.Length; i++)
            {
                Phase phase = s_phases[i];
                if (phase.FrameMarker == null || phase.FrameMarker.Retired)
                {
                    phase.FrameMarker = new WaitMarker(phase, k_kindFrame, phase.LastDrainFrame);
                }

                if (phase.YieldMarker == null || phase.YieldMarker.Retired)
                {
                    phase.YieldMarker = new WaitMarker(phase, k_kindYield, unchecked((int)phase.Pass));
                }
            }

            s_updateFrameMarker = s_phases[0].FrameMarker;
            s_fixedYieldMarker = s_phases[1].YieldMarker;
            s_lateYieldMarker = s_phases[2].YieldMarker;
        }

        private static void UpdateDefaultWaitsOpen()
        {
            s_defaultWaitsOpen = s_sessionOpen && s_accepting && s_installed && !s_retiring && !s_shuttingDown
                && s_updateFrameMarker != null && !s_updateFrameMarker.Retired;
        }

        /// <summary>
        /// Creates an explicit-timing frame wait on the main thread of an active session. A token that
        /// cannot be canceled gives a stateless task on the timing's session marker, which any number of
        /// consumers may await; a cancelable token rents a single-consumer source whose cancellation is
        /// published by the next Update drain, or by the canceling thread when
        /// <paramref name="cancelImmediately"/> is set.
        /// </summary>
        internal static OnityTask Schedule(
            OnityPlayerLoopTiming timing, int frameCount, bool yieldOnly, CancellationToken token,
            bool cancelImmediately = false)
        {
            ValidateTiming(timing);
            if (frameCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            ValidateContext();
            if (token.IsCancellationRequested)
            {
                return OnityTask.FromCanceled(token);
            }
            if (!yieldOnly && frameCount == 0)
            {
                return OnityTask.Completed;
            }
            if (!s_accepting)
            {
                throw new InvalidOperationException("The Onity timing loop requires a successful explicit repair.");
            }

            Phase phase = s_phases[(int)timing];
            EnsureInstalled(phase);
            if (!token.CanBeCanceled)
            {
                return yieldOnly
                    ? new OnityTask(phase.YieldMarker, unchecked((int)phase.Pass + 1))
                    : new OnityTask(phase.FrameMarker, unchecked(Time.frameCount + frameCount));
            }

            OnityPlayerLoopTaskSource source = OnityPlayerLoopTaskSource.Rent(
                phase.Pass, unchecked((uint)Time.frameCount), frameCount, token, cancelImmediately);
            int version = source.Version;
            phase.Pending.Add(source, version, k_kindSource);
            return new OnityTask(source, version);
        }

        /// <summary>
        /// Validates the context of a polled wait (<see cref="AddLoopWait"/>): the timing, Unity's main
        /// thread, an active session and an accepting loop, in that order.
        /// </summary>
        internal static void ValidateLoopWait(OnityPlayerLoopTiming timing)
        {
            ValidateTiming(timing);
            ValidateContext();
            if (!s_accepting)
            {
                throw new InvalidOperationException("The Onity timing loop requires a successful explicit repair.");
            }
        }

        private static void ValidateContext()
        {
            if (Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                throw new InvalidOperationException("Onity timing waits require Unity's main thread.");
            }
            if (!Application.isPlaying || !s_sessionOpen || s_retiring || s_shuttingDown)
            {
                throw new InvalidOperationException("Onity timing waits require an active Play/player session.");
            }
        }

        internal static OnityTask ScheduleEndOfFrame(CancellationToken token, bool cancelImmediately = false)
        {
            ValidateContext();
            if (!s_accepting || s_retiringEndOfFrame)
            {
                throw new InvalidOperationException("The Onity end-of-frame owner is not accepting waits.");
            }
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                throw new PlatformNotSupportedException("End-of-frame waits are unavailable in Editor batch mode.");
            }
#endif
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                throw new PlatformNotSupportedException("End-of-frame waits require a graphics device.");
            }
            if (token.IsCancellationRequested)
            {
                return OnityTask.FromCanceled(token);
            }
            if (!s_installed)
            {
                Initialize();
            }

            if (s_endOfFrameRunner == null)
            {
                var host = new GameObject("Onity End Of Frame")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                UnityEngine.Object.DontDestroyOnLoad(host);
                OnityEndOfFrameRunner runner = host.AddComponent<OnityEndOfFrameRunner>();
                s_endOfFrameRunner = runner;
                try
                {
                    runner.StartPump();
                }
                catch
                {
                    RetireEndOfFrame(runner);
                    throw;
                }
            }
            return s_endOfFrameRunner.Schedule(token, cancelImmediately);
        }

        internal static void RetireEndOfFrame(OnityEndOfFrameRunner runner)
        {
            if (!ReferenceEquals(runner, s_endOfFrameRunner) || s_retiringEndOfFrame)
            {
                return;
            }

            s_retiringEndOfFrame = true;
            s_endOfFrameRunner = null;
            GameObject host = runner.gameObject;
            try
            {
                List<OnityEndOfFrameRunner.PendingWait> pending = runner.Detach();
                OnityEndOfFrameRunner.RetirePending(pending, null);
            }
            finally
            {
                try
                {
                    DestroyEndOfFrameHost(host);
                }
                finally
                {
                    s_retiringEndOfFrame = false;
                }
            }
        }

        private static void DestroyEndOfFrameHost(GameObject host)
        {
            if (host != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(host);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }
        }

        internal static TimeoutEntry RegisterTimeout(
            float seconds, bool useUnscaledTime, IOnityTimeoutTimerSink sink)
        {
            ValidateContext();
            if (!s_accepting)
            {
                throw new InvalidOperationException("The Onity timeout timer requires a successful explicit loop repair.");
            }
            if (!s_installed)
            {
                Initialize();
            }

            int generation = unchecked(++s_timerGeneration);
            if (generation == 0)
            {
                generation = unchecked(++s_timerGeneration);
            }
            double now = useUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;
            var entry = new TimeoutEntry(
                generation, s_epoch, s_timerPass, now, (double)seconds, useUnscaledTime, sink);
            // No callbacks occur here. The caller assigns this identity before observing its producer.
            s_timers.Add(entry);
            return entry;
        }

        /// <summary>
        /// Registers a timeout timer measured with <paramref name="delayType"/> at the drains of
        /// <paramref name="timing"/>. It reports to the sink like the Update timers of
        /// <see cref="RegisterTimeout(float, bool, IOnityTimeoutTimerSink)"/>: expired, stopped (after
        /// <see cref="StopTimeout"/>) or session ended. Main thread of an accepting session.
        /// </summary>
        internal static TimeoutEntry RegisterTimeout(
            TimeSpan duration, OnityDelayType delayType, OnityPlayerLoopTiming timing, IOnityTimeoutTimerSink sink)
        {
            ValidateLoopWait(timing);
            int generation = unchecked(++s_timerGeneration);
            if (generation == 0)
            {
                generation = unchecked(++s_timerGeneration);
            }

            var entry = new TimeoutEntry(generation, s_epoch, s_timerPass, 0d, duration.TotalSeconds, false, sink);
            Phase phase = s_phases[(int)timing];
            EnsureInstalled(phase);
            // No callbacks occur here: the item first runs at a later drain.
            phase.Items.Add(new TimedTimeoutItem(entry, duration, delayType), 0, k_kindItem);
            return entry;
        }

        /// <summary>The PlayerLoop item that measures one timed timeout timer.</summary>
        private sealed class TimedTimeoutItem : IOnityRetirablePlayerLoopItem
        {
            private readonly TimeoutEntry m_entry;
            private OnityLoopClock m_clock;

            internal TimedTimeoutItem(TimeoutEntry entry, TimeSpan duration, OnityDelayType delayType)
            {
                m_entry = entry;
                m_clock.Start(duration, delayType);
            }

            public bool MoveNext()
            {
                if (Volatile.Read(ref m_entry.StopRequested) != 0)
                {
                    AcknowledgeTimer(m_entry, OnityTimeoutTimerOutcome.Stopped, null);
                    return false;
                }

                if (!m_clock.Advance())
                {
                    return true;
                }

                AcknowledgeTimer(m_entry, OnityTimeoutTimerOutcome.Expired, null);
                return false;
            }

            public void Retire(Exception failure)
            {
                AcknowledgeTimer(m_entry, OnityTimeoutTimerOutcome.SessionEnded, failure);
            }
        }

        internal static void StopTimeout(TimeoutEntry entry, int generation)
        {
            // May run on a producer worker. Do not inspect Unity/session/queue state.
            if (entry != null && entry.Generation == generation)
            {
                Volatile.Write(ref entry.StopRequested, 1);
            }
        }

        private static void DrainUpdate()
        {
            Phase update = s_phases[0];
            if (!s_accepting || update.Draining)
            {
                return;
            }

            update.Draining = true;
            s_drainDepth++;
            update.Pass++;
            s_timerPass++;
            int frame = Time.frameCount;
            StampPhase(update, frame);
            Volatile.Write(ref s_lastStampedFrame, frame);
            int epoch = s_epoch;
            int timerCount = s_timers.Count;
            try
            {
                BeginPhaseDrain(update);
                // Flag-only cancellations of every timing publish here, so a fixed wait is canceled even
                // while fixed time is paused. Waits registered while these publish wait for a later drain.
                if (Volatile.Read(ref s_cancellationRequests) != 0)
                {
                    Interlocked.Exchange(ref s_cancellationRequests, 0);
                    PublishFlaggedCancellations(epoch);
                }
                if (epoch == s_epoch)
                {
                    s_endOfFrameRunner?.CancelPending();
                }
                if (epoch == s_epoch)
                {
                    ProcessDrainingEntries(update, frame, epoch);
                }
                if (epoch == s_epoch && update.Items.IncomingCount != 0)
                {
                    ProcessItems(update, epoch);
                }
                if (epoch == s_epoch)
                {
                    DrainTimers(timerCount, epoch);
                }
            }
            finally
            {
                update.Draining = false;
                EndDrain();
            }
        }

        private static void Drain(int index)
        {
            Phase phase = s_phases[index];
            if (!s_accepting || phase.Draining)
            {
                return;
            }

            phase.Draining = true;
            s_drainDepth++;
            phase.Pass++;
            int frame = Time.frameCount;
            StampPhase(phase, frame);
            int epoch = s_epoch;
            try
            {
                BeginPhaseDrain(phase);
                ProcessDrainingEntries(phase, frame, epoch);
                if (epoch == s_epoch && phase.Items.IncomingCount != 0)
                {
                    ProcessItems(phase, epoch);
                }
            }
            finally
            {
                phase.Draining = false;
                EndDrain();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EndDrain()
        {
            if (--s_drainDepth == 0 && Volatile.Read(ref s_installPending))
            {
                InstallDeferred();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StampPhase(Phase phase, int frame)
        {
            phase.LastDrainFrame = frame;
            WaitMarker frameMarker = phase.FrameMarker;
            if (frameMarker != null && !frameMarker.Retired)
            {
                phase.PreviousFrameStamp = frameMarker.Stamp;
                frameMarker.Stamp = frame;
            }

            WaitMarker yieldMarker = phase.YieldMarker;
            if (yieldMarker != null && !yieldMarker.Retired)
            {
                phase.PreviousYieldStamp = yieldMarker.Stamp;
                yieldMarker.Stamp = unchecked((int)phase.Pass);
            }
        }

        private static void BeginPhaseDrain(Phase phase)
        {
            if (Volatile.Read(ref phase.InboxCount) != 0)
            {
                MergeInbox(phase);
            }

            phase.Pending.BeginDrain();
        }

        /// <summary>
        /// Runs or carries every entry registered before this drain. A slot is cleared before its
        /// entry runs, so a session close inside a continuation retires exactly the entries left.
        /// </summary>
        private static void ProcessDrainingEntries(Phase phase, int frame, int epoch)
        {
            WaitQueue queue = phase.Pending;
            Entry[] items = queue.Draining;
            int count = queue.DrainingCount;
            for (int i = 0; i < count; i++)
            {
                Entry entry = items[i];
                if (entry.Item == null)
                {
                    continue;
                }

                items[i] = default;
                if (entry.Kind == k_kindYield || entry.Kind == k_kindPost)
                {
                    OnityTaskContinuation.Invoke((Action)entry.Item);
                }
                else if (entry.Kind == k_kindFrame)
                {
                    if (unchecked(frame - entry.Token) >= 0)
                    {
                        OnityTaskContinuation.Invoke((Action)entry.Item);
                    }
                    else
                    {
                        queue.Add(entry.Item, entry.Token, entry.Kind);
                    }
                }
                else
                {
                    ProcessSource(phase, queue, entry, frame);
                }

                if (epoch != s_epoch)
                {
                    // A continuation closed the session; Retire owns the remaining entries.
                    return;
                }
            }

            queue.DrainingCount = 0;
        }

        private static void ProcessSource(Phase phase, WaitQueue queue, Entry entry, int frame)
        {
            OnityPlayerLoopTaskSource source = (OnityPlayerLoopTaskSource)entry.Item;
            if (!source.IsAwaitingPublication(entry.Token))
            {
                // Already published by a cancellation pass or an immediate cancellation, or consumed and
                // rented again.
                return;
            }

            bool canceled = source.IsCancellationFlagged;
            if (source.RegisteredPass != phase.Pass && (canceled
                || unchecked((uint)frame - source.RegisteredFrame) >= source.MinimumFrames))
            {
                Publish(source, entry.Token, canceled, null);
                return;
            }

            queue.Add(entry.Item, entry.Token, entry.Kind);
        }

        /// <summary>
        /// Runs the item queue of a drain, after its continuations: user items and pooled polled waits.
        /// Items queued before this pass began, including by the continuations of the same drain, run in
        /// it; items queued while it runs wait for the next drain.
        /// </summary>
        private static void ProcessItems(Phase phase, int epoch)
        {
            WaitQueue queue = phase.Items;
            if (queue.IncomingCount == 0)
            {
                return;
            }

            queue.BeginDrain();
            Entry[] items = queue.Draining;
            int count = queue.DrainingCount;
            for (int i = 0; i < count; i++)
            {
                Entry entry = items[i];
                if (entry.Item == null)
                {
                    continue;
                }

                items[i] = default;
                bool keep;
                if (entry.Kind == k_kindLoopWait)
                {
                    IOnityLoopWait wait = (IOnityLoopWait)entry.Item;
                    keep = wait.IsAwaitingPublication(entry.Token) && PollLoopWait(wait, entry.Token);
                }
                else
                {
                    keep = RunItem((IOnityPlayerLoopItem)entry.Item);
                }

                if (epoch != s_epoch)
                {
                    // The item closed the session while it was outside the queue; Retire owns the rest.
                    if (keep)
                    {
                        RetireEntry(entry, s_retirementFailure);
                    }

                    return;
                }

                if (keep)
                {
                    queue.Add(entry.Item, entry.Token, entry.Kind);
                }
            }

            queue.DrainingCount = 0;
        }

        private static bool RunItem(IOnityPlayerLoopItem item)
        {
            try
            {
                return item.MoveNext();
            }
            catch (Exception exception)
            {
                LogFailure(exception);
                return false;
            }
        }

        private static bool PollLoopWait(IOnityLoopWait wait, int version)
        {
            try
            {
                return wait.Poll(version);
            }
            catch (Exception exception)
            {
                // A wait publishes its own failures; this guards the remaining items against a bug.
                LogFailure(exception);
                return false;
            }
        }

        /// <summary>
        /// Publishes every flagged cancellation queued before this Update drain began, in every timing,
        /// including timings whose node does not run, such as FixedUpdate while time is paused.
        /// </summary>
        private static void PublishFlaggedCancellations(int epoch)
        {
            for (int i = 1; i < k_timingCount; i++)
            {
                s_scanPendingCounts[i] = s_phases[i].Pending.IncomingCount;
            }

            for (int i = 0; i < k_timingCount; i++)
            {
                s_scanItemCounts[i] = s_phases[i].Items.IncomingCount;
            }

            // The Update continuations of this drain sit in its draining buffer. A buffer grown by a
            // publication keeps the captured entries at the same positions.
            WaitQueue update = s_phases[0].Pending;
            PublishFlaggedEntries(update.Draining, update.DrainingCount, epoch);
            for (int i = 1; i < k_timingCount && epoch == s_epoch; i++)
            {
                PublishFlaggedEntries(s_phases[i].Pending.Incoming, s_scanPendingCounts[i], epoch);
            }

            for (int i = 0; i < k_timingCount && epoch == s_epoch; i++)
            {
                PublishFlaggedEntries(s_phases[i].Items.Incoming, s_scanItemCounts[i], epoch);
            }
        }

        private static void PublishFlaggedEntries(Entry[] entries, int count, int epoch)
        {
            for (int i = 0; i < count && epoch == s_epoch; i++)
            {
                object item = entries[i].Item;
                int token = entries[i].Token;
                if (item == null)
                {
                    continue;
                }

                // The entries stay queued; their drains skip them once they have published.
                if (entries[i].Kind == k_kindSource)
                {
                    OnityPlayerLoopTaskSource source = (OnityPlayerLoopTaskSource)item;
                    if (source.IsCancellationFlagged && source.IsAwaitingPublication(token))
                    {
                        Publish(source, token, true, null);
                    }
                }
                else if (entries[i].Kind == k_kindLoopWait)
                {
                    IOnityLoopWait wait = (IOnityLoopWait)item;
                    if (wait.IsCancellationFlagged && wait.IsAwaitingPublication(token))
                    {
                        try
                        {
                            wait.PublishCancellation(token);
                        }
                        catch (Exception exception)
                        {
                            LogFailure(exception);
                        }
                    }
                }
            }
        }

        /// <summary>Retires one entry that left its queue: publishes or wakes it, or drops a user item.</summary>
        private static void RetireEntry(Entry entry, Exception failure)
        {
            switch (entry.Kind)
            {
                case k_kindSource:
                    OnityPlayerLoopTaskSource source = (OnityPlayerLoopTaskSource)entry.Item;
                    if (source.IsAwaitingPublication(entry.Token))
                    {
                        Publish(source, entry.Token, failure == null, failure);
                    }

                    break;
                case k_kindLoopWait:
                    try
                    {
                        IOnityLoopWait wait = (IOnityLoopWait)entry.Item;
                        if (wait.IsAwaitingPublication(entry.Token))
                        {
                            wait.Retire(entry.Token, failure);
                        }
                    }
                    catch (Exception exception)
                    {
                        LogFailure(exception);
                    }

                    break;
                case k_kindItem:
                    if (entry.Item is IOnityRetirablePlayerLoopItem retirable)
                    {
                        try
                        {
                            retirable.Retire(failure);
                        }
                        catch (Exception exception)
                        {
                            LogFailure(exception);
                        }
                    }

                    break;
                case k_kindPost:
                    // A posted action belongs to the session that ends; it is dropped, as UniTask does.
                    break;
                default:
                    // The continuation observes the retired marker or the changed epoch.
                    OnityTaskContinuation.Invoke((Action)entry.Item);
                    break;
            }
        }

        private static void LogFailure(Exception exception)
        {
            try
            {
                Debug.LogException(exception);
            }
            catch (Exception)
            {
            }
        }

        private static void DrainTimers(int count, int epoch)
        {
            if (count == 0)
            {
                return;
            }

            double unscaled = Time.unscaledTimeAsDouble;
            double scaled = Time.timeAsDouble;
            for (int i = count - 1; i >= 0 && epoch == s_epoch; i--)
            {
                TimeoutEntry entry = s_timers[i];
                if (entry.RegisteredPass == s_timerPass)
                {
                    continue;
                }
                bool stopped = Volatile.Read(ref entry.StopRequested) != 0;
                double elapsed = (entry.UseUnscaledTime ? unscaled : scaled) - entry.StartTime;
                if (stopped || elapsed >= entry.Duration)
                {
                    int last = s_timers.Count - 1;
                    s_timers[i] = s_timers[last];
                    s_timers.RemoveAt(last);
                    AcknowledgeTimer(entry, stopped ? OnityTimeoutTimerOutcome.Stopped
                        : OnityTimeoutTimerOutcome.Expired, null);
                }
            }
        }

        private static void AcknowledgeTimer(
            TimeoutEntry entry, OnityTimeoutTimerOutcome outcome, Exception failure)
        {
            IOnityTimeoutTimerSink sink = entry.Sink;
            int generation = entry.Generation;
            entry.Sink = null;
            try
            {
                sink?.Acknowledge(entry, generation, outcome, failure);
            }
            catch (Exception exception)
            {
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Publish(OnityPlayerLoopTaskSource source, int version, bool canceled, Exception failure)
        {
            try
            {
                source.Publish(version, canceled, failure);
            }
            catch (Exception exception)
            {
                // A throwing continuation/logger must not abandon other accepted waits.
                LogFailure(exception);
            }
        }

        private static void Retire(Exception failure)
        {
            if (s_retiring)
            {
                return;
            }

            s_retiring = true;
            s_retirementFailure = failure;
            UpdateDefaultWaitsOpen();
            // Retire the stateless markers first: from here on a registration runs inline and observes
            // the cancellation or failure, and no queued task of this session can complete later. A drain
            // in progress returns its markers to the stamps they had before it, so the waits it has not
            // resumed yet report the retirement rather than its success.
            for (int i = 0; i < s_phases.Length; i++)
            {
                Phase phase = s_phases[i];
                if (phase.Draining)
                {
                    if (phase.FrameMarker != null && !phase.FrameMarker.Retired)
                    {
                        phase.FrameMarker.Stamp = phase.PreviousFrameStamp;
                    }

                    if (phase.YieldMarker != null && !phase.YieldMarker.Retired)
                    {
                        phase.YieldMarker.Stamp = phase.PreviousYieldStamp;
                    }
                }

                phase.FrameMarker?.Retire(failure);
                phase.YieldMarker?.Retire(failure);
            }

            // Detach every queue before any continuation can inspect this owner.
            List<Entry> detached = DetachQueuedEntries();
            List<TimeoutEntry> timers = s_timers;
            s_timers = s_spareTimers;
            s_spareTimers = timers;
            OnityEndOfFrameRunner endOfFrameRunner = s_endOfFrameRunner;
            GameObject endOfFrameHost = endOfFrameRunner != null ? endOfFrameRunner.gameObject : null;
            s_endOfFrameRunner = null;
            List<OnityEndOfFrameRunner.PendingWait> endOfFramePending = endOfFrameRunner?.Detach();
            try
            {
                for (int i = 0; i < detached.Count; i++)
                {
                    RetireEntry(detached[i], failure);
                }

                for (int i = timers.Count - 1; i >= 0; i--)
                {
                    TimeoutEntry entry = timers[i];
                    timers.RemoveAt(i);
                    AcknowledgeTimer(entry, OnityTimeoutTimerOutcome.SessionEnded, failure);
                }
                OnityEndOfFrameRunner.RetirePending(endOfFramePending, failure);
            }
            finally
            {
                try
                {
                    DestroyEndOfFrameHost(endOfFrameHost);
                }
                finally
                {
                    s_retiring = false;
                    UpdateDefaultWaitsOpen();
                }
            }
        }

        /// <summary>
        /// Moves every queued entry (draining slots not yet run, incoming entries and worker inboxes)
        /// out of the phase queues, leaving them empty.
        /// </summary>
        private static List<Entry> DetachQueuedEntries()
        {
            var detached = new List<Entry>();
            for (int i = 0; i < s_phases.Length; i++)
            {
                Phase phase = s_phases[i];
                DetachQueue(phase.Pending, detached);
                DetachQueue(phase.Items, detached);
                lock (phase.InboxGate)
                {
                    detached.AddRange(phase.Inbox);
                    phase.Inbox.Clear();
                    Volatile.Write(ref phase.InboxCount, 0);
                }
            }

            return detached;
        }

        private static void DetachQueue(WaitQueue queue, List<Entry> detached)
        {
            for (int j = 0; j < queue.DrainingCount; j++)
            {
                if (queue.Draining[j].Item != null)
                {
                    detached.Add(queue.Draining[j]);
                }

                queue.Draining[j] = default;
            }

            queue.DrainingCount = 0;
            for (int j = 0; j < queue.IncomingCount; j++)
            {
                detached.Add(queue.Incoming[j]);
                queue.Incoming[j] = default;
            }

            queue.IncomingCount = 0;
        }

        /// <summary>
        /// Closes the session without touching the legacy runner (tests and benchmark self-tests call it
        /// through reflection without arguments).
        /// </summary>
        private static void CloseSession()
        {
            EndSession(false);
        }

        /// <summary>
        /// Ends the session. A real close (Play exit, reload, quit) first stops legacy acceptance, so
        /// retirement callbacks cannot create replacement legacy waits; the legacy runner retires its
        /// own waits in its session close.
        /// </summary>
        private static void EndSession(bool closeLegacyAcceptance)
        {
            if (closeLegacyAcceptance)
            {
                OnityTaskRunner.CloseAcceptance();
            }

            s_sessionOpen = false;
            s_accepting = false;
            s_installed = false;
            s_epoch++;
            UpdateDefaultWaitsOpen();
            RemoveNodes();
            Retire(null);
        }

        private static void CloseApplication()
        {
            s_shuttingDown = true;
            EndSession(true);
        }

        private static void BeginSession(bool force)
        {
            if (s_shuttingDown || s_retiring || (!force && s_sessionOpen))
            {
                return;
            }

            int expectedEpoch = unchecked(s_epoch + 1);
            EndSession(false);
            if (s_epoch == expectedEpoch && !s_shuttingDown && !s_retiring)
            {
                int frame = Time.frameCount;
                for (int i = 0; i < s_phases.Length; i++)
                {
                    Phase phase = s_phases[i];
                    phase.Pass = 0;
                    phase.LastDrainFrame = frame;
                    // A session starts with the eager nodes only; other timings install on first use.
                    phase.Requested = i < k_eagerTimingCount;
                }
                s_installPending = false;
                s_timerPass = 0;
                s_lastStampedFrame = frame;
                s_retirementFailure = null;
                EnsureSessionMarkers();
                s_sessionOpen = true;
                s_accepting = true;
                UpdateDefaultWaitsOpen();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_isMainThread = true;
            s_unitySynchronizationContext = SynchronizationContext.Current;
#if !UNITY_EDITOR
            Application.quitting -= CloseApplication;
            Application.quitting += CloseApplication;
#endif
            BeginSession(true);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallRuntime()
        {
            Initialize();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_isMainThread = true;
            s_unitySynchronizationContext = SynchronizationContext.Current;
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            UnityEditor.EditorApplication.quitting -= CloseApplication;
            UnityEditor.EditorApplication.quitting += CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= CloseApplication;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CloseApplication;
            if (Application.isPlaying)
            {
                BeginSession(false);
            }
        }

        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode
                || state == UnityEditor.PlayModeStateChange.ExitingPlayMode
                || state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                // Only leaving Play closes legacy acceptance early; the legacy runner reopens it when
                // its next session begins, whatever the handler order.
                EndSession(state == UnityEditor.PlayModeStateChange.ExitingPlayMode);
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredPlayMode)
            {
                // Subsystem registration and BeforeSceneLoad already accepted Awake work.
                BeginSession(false);
            }
        }
#endif

        private static bool IsOwned(Type type)
        {
            if (type == null)
            {
                return false;
            }

            for (int i = 0; i < s_phases.Length; i++)
            {
                if (type == s_phases[i].NodeType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Inserts <paramref name="node"/> first or last in the subsystems of <paramref name="parent"/>.</summary>
        private static bool InsertInPhase(ref PlayerLoopSystem system, Type parent, PlayerLoopSystem node, bool atEnd)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return false;
            }

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == parent)
                {
                    PlayerLoopSystem[] phaseChildren = children[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                    var expanded = new PlayerLoopSystem[phaseChildren.Length + 1];
                    if (atEnd)
                    {
                        Array.Copy(phaseChildren, expanded, phaseChildren.Length);
                        expanded[phaseChildren.Length] = node;
                    }
                    else
                    {
                        expanded[0] = node;
                        Array.Copy(phaseChildren, 0, expanded, 1, phaseChildren.Length);
                    }

                    children[i].subSystemList = expanded;
                    return true;
                }

                if (InsertInPhase(ref children[i], parent, node, atEnd))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountType(PlayerLoopSystem system, Type type)
        {
            int count = system.type == type ? 1 : 0;
            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    count += CountType(child, type);
                }
            }

            return count;
        }

        private static void DescribeSystem(PlayerLoopSystem system, int depth, System.Text.StringBuilder builder)
        {
            if (system.type != null)
            {
                builder.Append(' ', depth * 2);
                if (IsOwned(system.type))
                {
                    builder.Append("Onity.");
                }

                builder.Append(system.type.Name).Append('\n');
            }

            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    DescribeSystem(child, system.type != null ? depth + 1 : depth, builder);
                }
            }
        }

        private static void RemoveOwned(ref PlayerLoopSystem system)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return;
            }
            var retained = new List<PlayerLoopSystem>(children.Length);
            for (int i = 0; i < children.Length; i++)
            {
                if (IsOwned(children[i].type))
                {
                    continue;
                }
                PlayerLoopSystem child = children[i];
                RemoveOwned(ref child);
                retained.Add(child);
            }
            system.subSystemList = retained.ToArray();
        }

        private static void RemoveNodes()
        {
            try
            {
                PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
                RemoveOwned(ref loop);
                PlayerLoop.SetPlayerLoop(loop);
            }
            catch (Exception exception)
            {
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                }
            }
        }

        private static void ValidateAnchor(PlayerLoopSystem system, Type parent, Type anchor)
        {
            if (CountAnchor(system, anchor) != 1 || CountPlacement(system, parent, anchor) != 1)
            {
                throw new InvalidOperationException("Onity timing anchor missing or duplicated: " + anchor.FullName);
            }
        }

        private static int CountPlacement(PlayerLoopSystem system, Type parent, Type anchor)
        {
            int count = 0;
            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    if (system.type == parent && child.type == anchor)
                    {
                        count++;
                    }
                    count += CountPlacement(child, parent, anchor);
                }
            }
            return count;
        }

        private static int CountAnchor(PlayerLoopSystem system, Type anchor)
        {
            int count = system.type == anchor ? 1 : 0;
            if (system.subSystemList != null)
            {
                foreach (PlayerLoopSystem child in system.subSystemList)
                {
                    count += CountAnchor(child, anchor);
                }
            }
            return count;
        }

        private static bool InsertAfter(ref PlayerLoopSystem system, Type anchor, PlayerLoopSystem node)
        {
            PlayerLoopSystem[] children = system.subSystemList;
            if (children == null)
            {
                return false;
            }
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].type == anchor)
                {
                    var expanded = new PlayerLoopSystem[children.Length + 1];
                    Array.Copy(children, 0, expanded, 0, i + 1);
                    expanded[i + 1] = node;
                    Array.Copy(children, i + 1, expanded, i + 2, children.Length - i - 1);
                    system.subSystemList = expanded;
                    return true;
                }
                if (InsertAfter(ref children[i], anchor, node))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
