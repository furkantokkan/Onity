using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.IL2CPP.CompilerServices;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Root of every Onity-native task source: pooled sources, async-method runners and stateless
    /// PlayerLoop waits. Awaiters dispatch with one class test and one virtual call instead of an
    /// interface test and interface calls. Slow-path consumers keep using <see cref="IOnityTaskSource"/>,
    /// which this class implements by forwarding to the same virtual members.
    /// </summary>
    internal abstract class OnityTaskCore : IOnityTaskSource
    {
        /// <summary>Token-validated status; never consumes and never reads the outcome payload.</summary>
        internal abstract OnityTaskSourceStatus GetCoreStatus(int token);

        /// <summary>Registers the native continuation for the token.</summary>
        internal abstract void OnCoreCompleted(Action continuation, int token);

        /// <summary>Consumes the outcome without a result: rethrows a fault or cancellation.</summary>
        internal abstract void ConsumeCore(int token);

        /// <summary>Returns a .NET task bridge for the token.</summary>
        internal abstract Task AsCoreTask(int token);

        /// <summary>The token a task created from this source right now carries.</summary>
        internal abstract int CoreVersion { get; }

        /// <summary>
        /// True for stateless waits that any number of consumers may observe repeatedly; such
        /// tasks need neither <c>Preserve()</c> nor a <c>Forget()</c> observer.
        /// </summary>
        internal virtual bool IsStatelessWait => false;

        int IOnityTaskSource.Version => CoreVersion;

        OnityTaskSourceStatus IOnityTaskSource.GetStatus(int token)
        {
            return GetCoreStatus(token);
        }

        Task IOnityTaskSource.AsTask(int token)
        {
            return AsCoreTask(token);
        }

        void IOnityTaskSource.OnCompleted(Action continuation, int token)
        {
            OnCoreCompleted(continuation, token);
        }

        void IOnityTaskSource.GetResult(int token)
        {
            ConsumeCore(token);
        }
    }

    /// <summary>
    /// Continuation slot of a pooled source. Clearing it with <c>default</c> compiles to <c>initobj</c>,
    /// which needs no GC write barrier.
    /// </summary>
    internal struct OnityContinuationSlot
    {
        internal object Continuation;
    }

    /// <summary>
    /// Completion protocol shared by the untyped and typed pooled source bases. One <see cref="int"/>
    /// word holds the status, the multi-producer claim, the consumption mode, the registration pin,
    /// the registered and bridge bits and a 24-bit token version. Every transition is one
    /// compare-and-swap or atomic add that validates the word it read, so a stale task value can never
    /// change a later cycle of the same pooled object:
    /// <list type="bullet">
    /// <item>registration: a version-checked claim that pins the cycle, the slot store, then an atomic
    /// publish of the registered bit;</item>
    /// <item>owned completion (runners, main-thread owners): one compare-and-swap;</item>
    /// <item>shared completion (cancellation, retirement, completion sources): a claim, the outcome
    /// stores, then the publishing compare-and-swap;</item>
    /// <item>consumption: one compare-and-swap that retires the version, so exactly one consumer wins.</item>
    /// </list>
    /// A completer reads the registered continuation before its publishing compare-and-swap and touches
    /// no field afterwards. Outcome fields are written before the publishing atomic and read only after
    /// the consumer's own atomic, so plain status reads are safe.
    /// </summary>
    [Il2CppSetOption(Option.NullChecks, false)]
    internal abstract class OnityTaskSourceCore : OnityTaskCore
    {
        private protected const int k_modeNone = 0;
        private protected const int k_modeNative = 1;
        private protected const int k_modeTask = 2;
        private protected const int k_modePreserved = 3;

        private const int k_statusMask = 0x3;
        private const int k_claimedBit = 1 << 2;
        private const int k_modeShift = 3;
        private const int k_modeMask = 0x3 << k_modeShift;
        private const int k_registeringBit = 1 << 5;
        private const int k_registeredBit = 1 << 6;
        private const int k_materializedBit = 1 << 7;
        private const int k_versionShift = 8;
        private const int k_versionMask = 0xFFFFFF;
        private const int k_flagsMask = (1 << k_versionShift) - 1;

        private const int k_succeeded = (int)OnityTaskSourceStatus.Succeeded;
        private const int k_canceled = (int)OnityTaskSourceStatus.Canceled;
        private const int k_faulted = (int)OnityTaskSourceStatus.Faulted;

        private static readonly Action<object> s_cancelCallback = CancelFromToken;

        private int m_state;
        private OnityContinuationSlot m_slot;
        private object m_bridge;
        private Exception m_exception;
        private CancellationTokenRegistration m_cancellationRegistration;
        private CancellationToken m_cancellationToken;
        private int m_cancellationRequested;

        /// <summary>The token of the current cycle.</summary>
        public int Version
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => VersionOf(m_state);
        }

        public bool IsCancellationRequested => Volatile.Read(ref m_cancellationRequested) != 0;

        /// <summary>
        /// True once a native awaiter, a preserved continuation, or a .NET task bridge has claimed the
        /// current cycle. A caller that registers afterwards still goes through the versioned checks.
        /// </summary>
        internal bool HasConsumer => (Volatile.Read(ref m_state) & (k_modeMask | k_registeringBit | k_registeredBit)) != 0;

        internal sealed override int CoreVersion => Version;

        protected bool IsPending
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_state & k_statusMask) == 0;
        }

        private protected Exception CompletionException => m_exception;

        private protected CancellationToken CompletionCancellationToken => m_cancellationToken;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTaskSourceStatus GetStatus(int token)
        {
            int state = m_state;
            if (VersionOf(state) != token)
            {
                ThrowInvalidToken();
            }

            return (OnityTaskSourceStatus)(state & k_statusMask);
        }

        internal sealed override OnityTaskSourceStatus GetCoreStatus(int token)
        {
            return GetStatus(token);
        }

        public void OnCompleted(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            Register(continuation, token, k_modeNative);
        }

        public void OnCompleted(IOnityPreservedTaskContinuation continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            Register(continuation, token, k_modePreserved);
        }

        internal sealed override void OnCoreCompleted(Action continuation, int token)
        {
            OnCompleted(continuation, token);
        }

        public bool TrySetCanceledFromRunner(int token)
        {
            return IsCancellationRequested && TryComplete(k_canceled, null, token, true);
        }

        public bool TrySetRetiredFromRunner(int token)
        {
            return TryComplete(k_canceled, null, token, true);
        }

        /// <summary>
        /// Retires the current token so every later use of an old task value fails its versioned
        /// compare-and-swap, before the source is reused.
        /// </summary>
        protected void InvalidateVersion()
        {
            while (true)
            {
                int state = Volatile.Read(ref m_state);
                int next = (state & k_flagsMask) | (NextVersion(VersionOf(state)) << k_versionShift);
                if (Interlocked.CompareExchange(ref m_state, next, state) == state)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Starts a new cycle without cancellation on a source that no other thread can reach: its
        /// release retired the version and cleared the outcome fields, so only the continuation slot
        /// and the cancellation flag are reset before the pending word with the next version is
        /// stored. The handoff of the rented object to another thread is itself an atomic operation,
        /// which publishes these plain stores.
        /// </summary>
        protected void ResetRetired()
        {
            m_slot = default;
            m_cancellationRequested = 0;
            m_state = NextCycleWord(m_state);
        }

        /// <summary>
        /// Starts a new cycle. The fields are cleared first and the new pending word, with the next
        /// version, is stored last, so a stale caller either fails its version check or sees the
        /// complete new cycle.
        /// </summary>
        protected void Reset(CancellationToken cancellationToken)
        {
            m_slot = default;
            if (m_bridge != null)
            {
                m_bridge = null;
            }

            if (m_exception != null)
            {
                m_exception = null;
            }

            if (m_cancellationToken.CanBeCanceled || cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration = default;
                m_cancellationToken = cancellationToken;
            }

            ClearResult();
            m_cancellationRequested = 0;
            Volatile.Write(ref m_state, NextCycleWord(m_state));

            if (cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration = cancellationToken.Register(s_cancelCallback, this);
            }
        }

        protected bool TrySetException(Exception exception)
        {
            return TryComplete(k_faulted, exception ?? new InvalidOperationException("OnityTask failed."), 0, false);
        }

        protected bool TrySetCanceled()
        {
            return TryComplete(k_canceled, null, 0, false);
        }

        protected bool TrySetCanceled(OperationCanceledException exception)
        {
            return TryComplete(k_canceled, exception, 0, false);
        }

        /// <summary>
        /// Version-checked cancellation for sources that a second thread may complete (an immediate
        /// cancellation): a call for a cycle that already published, and was consumed and released since,
        /// changes nothing.
        /// </summary>
        private protected bool TrySetCanceled(OperationCanceledException exception, int version)
        {
            return TryComplete(k_canceled, exception, version, true);
        }

        /// <summary>Version-checked failure; see <see cref="TrySetCanceled(OperationCanceledException, int)"/>.</summary>
        private protected bool TrySetException(Exception exception, int version)
        {
            return TryComplete(
                k_faulted, exception ?? new InvalidOperationException("OnityTask failed."), version, true);
        }

        /// <summary>Version-checked claim; see <see cref="TrySetCanceled(OperationCanceledException, int)"/>.</summary>
        private protected bool TryClaimCompletion(int version)
        {
            return TryClaim(version, true);
        }

        protected abstract void ReleaseSource();

        /// <summary>Clears a typed result; a no-op for untyped sources.</summary>
        private protected virtual void ClearResult()
        {
        }

        /// <summary>Creates the .NET task bridge of this source type.</summary>
        private protected abstract object CreateBridge();

        /// <summary>Applies a published status to the bridge created by <see cref="CreateBridge"/>.</summary>
        private protected abstract void ApplyStatusToBridge(object bridge, int status);

        /// <summary>
        /// Claims the cycle for one completer. The caller then stores its outcome and calls
        /// <see cref="PublishClaimed"/>.
        /// </summary>
        private protected bool TryClaimCompletion()
        {
            return TryClaim(0, false);
        }

        /// <summary>Publishes the status of a cycle this caller claimed.</summary>
        private protected void PublishClaimed(int status)
        {
            DisposeCancellationRegistration();
            while (true)
            {
                int state = m_state;
                object continuation = ReadRegisteredContinuation(state);
                if (Interlocked.CompareExchange(ref m_state, (state & ~k_claimedBit) | status, state) == state)
                {
                    CompletePublication(state, continuation);
                    return;
                }
            }
        }

        /// <summary>
        /// Publishes success for a cycle whose completion only this caller performs, such as an async
        /// method's runner or a main-thread PlayerLoop owner: one compare-and-swap instead of a claim and
        /// a publish. Returns false when another completer, such as a retirement, already won.
        /// </summary>
        private protected bool TryPublishOwned(int status)
        {
            if (m_cancellationToken.CanBeCanceled)
            {
                DisposeCancellationRegistration();
            }

            while (true)
            {
                int state = m_state;
                if ((state & (k_statusMask | k_claimedBit)) != 0)
                {
                    return false;
                }

                object continuation = ReadRegisteredContinuation(state);
                if (Interlocked.CompareExchange(ref m_state, state | status, state) == state)
                {
                    CompletePublication(state, continuation);
                    return true;
                }
            }
        }

        /// <summary>
        /// The owned publication of <see cref="TryPublishOwned(int)"/> for an owner that may race a
        /// completer on another thread (an immediate cancellation): it also fails when the cycle that
        /// issued <paramref name="version"/> has already been consumed and released.
        /// </summary>
        private protected bool TryPublishOwned(int status, int version)
        {
            if (m_cancellationToken.CanBeCanceled)
            {
                DisposeCancellationRegistration();
            }

            while (true)
            {
                int state = m_state;
                if ((state & (k_statusMask | k_claimedBit)) != 0 || VersionOf(state) != version)
                {
                    return false;
                }

                object continuation = ReadRegisteredContinuation(state);
                if (Interlocked.CompareExchange(ref m_state, state | status, state) == state)
                {
                    CompletePublication(state, continuation);
                    return true;
                }
            }
        }

        /// <summary>
        /// Validates the token and the consumption mode, requires a published completion, and retires
        /// the version in one compare-and-swap, so exactly one consumer wins and the token is retired the
        /// instant the result is taken. Returns the published status. Outcome fields may be read after
        /// this call and must then be cleared with <see cref="ClearCompletionReferences"/>.
        /// </summary>
        private protected int ClaimResult(int token, IOnityPreservedTaskContinuation preserved)
        {
            SpinWait spinner = default;
            while (true)
            {
                int state = m_state;
                if (VersionOf(state) != token)
                {
                    ThrowInvalidToken();
                }

                if ((state & k_registeringBit) != 0)
                {
                    // A registration holds its pin for a few instructions; let it publish first.
                    spinner.SpinOnce();
                    continue;
                }

                int mode = (state & k_modeMask) >> k_modeShift;
                if (mode == k_modePreserved)
                {
                    if (!ReferenceEquals(m_slot.Continuation, preserved))
                    {
                        throw new InvalidOperationException("OnityTask is already being consumed through Preserve().");
                    }
                }
                else if (preserved != null || mode == k_modeTask)
                {
                    throw new InvalidOperationException("OnityTask is already being consumed through AsTask().");
                }

                int status = state & k_statusMask;
                if (status == 0)
                {
                    throw new InvalidOperationException("OnityTask is not completed.");
                }

                if (Interlocked.CompareExchange(ref m_state, NextCycleWord(state), state) == state)
                {
                    return status;
                }
            }
        }

        /// <summary>The exception a consumer of the given status must observe, or null on success.</summary>
        private protected Exception GetCompletionException(int status)
        {
            if (status == k_canceled)
            {
                return m_exception as OperationCanceledException ?? new OperationCanceledException(m_cancellationToken);
            }

            return status == k_faulted ? m_exception : null;
        }

        /// <summary>
        /// Clears the references of a consumed or released cycle. Stores are skipped when the field is
        /// already clear, so the common path performs no write barrier.
        /// </summary>
        private protected void ClearCompletionReferences()
        {
            m_slot = default;
            if (m_bridge != null)
            {
                m_bridge = null;
            }

            if (m_exception != null)
            {
                m_exception = null;
            }

            if (m_cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration = default;
                m_cancellationToken = default;
            }
        }

        /// <summary>
        /// Returns the .NET task bridge for the token, creating and publishing it on first use. A
        /// completer that claimed or published before the bridge was published cannot see it, so this
        /// call applies that outcome and releases the source itself.
        /// </summary>
        private protected object GetOrCreateBridge(int token)
        {
            object bridge = null;
            int snapshot;
            SpinWait spinner = default;
            while (true)
            {
                int state = Volatile.Read(ref m_state);
                if (VersionOf(state) != token)
                {
                    ThrowInvalidToken();
                }

                int mode = (state & k_modeMask) >> k_modeShift;
                if (mode == k_modeNative || mode == k_modePreserved)
                {
                    throw new InvalidOperationException("OnityTask is already being consumed by its native awaiter.");
                }

                if (mode == k_modeTask)
                {
                    if ((state & k_materializedBit) == 0)
                    {
                        spinner.SpinOnce();
                        continue;
                    }

                    object published = Volatile.Read(ref m_bridge);
                    // A release may clear the field and rent the source again between the reads;
                    // validate afterwards so a stale caller cannot return a successor's bridge.
                    if (VersionOf(Volatile.Read(ref m_state)) != token)
                    {
                        ThrowInvalidToken();
                    }

                    return published;
                }

                if (bridge == null)
                {
                    bridge = CreateBridge();
                }

                // Reserve the version before writing any field. The unmaterialized task mode blocks
                // native consumption and bridge release until the bridge is published.
                int claimed = (state & ~k_modeMask) | (k_modeTask << k_modeShift);
                if (Interlocked.CompareExchange(ref m_state, claimed, state) == state)
                {
                    Volatile.Write(ref m_bridge, bridge);
                    while (true)
                    {
                        state = Volatile.Read(ref m_state);
                        if (Interlocked.CompareExchange(ref m_state, state | k_materializedBit, state) == state)
                        {
                            snapshot = state;
                            break;
                        }
                    }

                    break;
                }
            }

            if ((snapshot & (k_claimedBit | k_statusMask)) != 0
                && TryClaimBridgeRelease(VersionOf(snapshot), out int status))
            {
                ApplyStatusToBridge(bridge, status);
                ClearResult();
                ClearCompletionReferences();
                ReleaseSource();
            }

            return bridge;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int VersionOf(int state)
        {
            return (int)((uint)state >> k_versionShift);
        }

        private static int NextVersion(int version)
        {
            int next = (version + 1) & k_versionMask;
            return next == 0 ? 1 : next;
        }

        /// <summary>A pending word without flags that carries the version following the given word's.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextCycleWord(int state)
        {
            return NextVersion(VersionOf(state)) << k_versionShift;
        }

        private static void CancelFromToken(object state)
        {
            OnityTaskSourceCore source = (OnityTaskSourceCore)state;
            Volatile.Write(ref source.m_cancellationRequested, 1);
            OnityTaskRunner.NotifyCancellationRequested();
        }

        private static void ThrowInvalidToken()
        {
            throw new InvalidOperationException(
                "The OnityTask source is no longer valid. Pooled OnityTask instances can be awaited only once.");
        }

        private static void InvokeRegistered(object continuation)
        {
            if (continuation is Action action)
            {
                OnityTaskContinuation.Invoke(action);
            }
            else
            {
                OnityTaskContinuation.Invoke((IOnityPreservedTaskContinuation)continuation);
            }
        }

        private void Register(object continuation, int token, int mode)
        {
            while (true)
            {
                int state = m_state;
                if (VersionOf(state) != token)
                {
                    ThrowInvalidToken();
                }

                if ((state & (k_modeMask | k_registeringBit | k_registeredBit)) != 0)
                {
                    throw new InvalidOperationException("OnityTask supports only one native awaiter.");
                }

                // The pin keeps the cycle from being consumed, released and rented again while the
                // continuation is stored, so a paused caller never writes into a later rental.
                int pinned = state | (mode << k_modeShift) | k_registeringBit;
                if (Interlocked.CompareExchange(ref m_state, pinned, state) == state)
                {
                    break;
                }
            }

            m_slot.Continuation = continuation;
            // Clears the pin and sets the registered bit in one atomic step (bit 5 carries into bit 6).
            int published = Interlocked.Add(ref m_state, k_registeredBit - k_registeringBit);
            if ((published & k_statusMask) == 0)
            {
                return;
            }

            // The completer published while the pin was held and saw no continuation, so run it here.
            // User code may consume and recycle the source: touch no field afterwards.
            if (continuation is Action action)
            {
                action();
            }
            else
            {
                ((IOnityPreservedTaskContinuation)continuation).Complete();
            }
        }

        private bool TryComplete(int status, Exception exception, int expectedVersion, bool checkVersion)
        {
            if (!TryClaim(expectedVersion, checkVersion))
            {
                return false;
            }

            m_exception = exception;
            PublishClaimed(status);
            return true;
        }

        private bool TryClaim(int expectedVersion, bool checkVersion)
        {
            while (true)
            {
                int state = m_state;
                if ((state & (k_statusMask | k_claimedBit)) != 0
                    || (checkVersion && VersionOf(state) != expectedVersion))
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref m_state, state | k_claimedBit, state) == state)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Reads the registered continuation of the given word, or null. The barrier orders the word's
        /// load before the slot's load, pairing with the registrant's atomic publish.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private object ReadRegisteredContinuation(int state)
        {
            if ((state & k_registeredBit) == 0)
            {
                return null;
            }

            Interlocked.MemoryBarrier();
            return m_slot.Continuation;
        }

        /// <summary>
        /// Finishes a publication that replaced <paramref name="replaced"/>: releases a bridged source
        /// and runs the registered continuation, if any.
        /// </summary>
        private void CompletePublication(int replaced, object continuation)
        {
            if ((replaced & k_materializedBit) != 0
                && TryClaimBridgeRelease(VersionOf(replaced), out int status))
            {
                ApplyStatusToBridge(Volatile.Read(ref m_bridge), status);
                ClearResult();
                ClearCompletionReferences();
                ReleaseSource();
            }

            if (continuation != null)
            {
                InvokeRegistered(continuation);
            }
        }

        private void DisposeCancellationRegistration()
        {
            if (m_cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration.Dispose();
                m_cancellationRegistration = default;
            }
        }

        /// <summary>
        /// Claims the single release of a bridged cycle once its status is published, retiring the
        /// version in the same compare-and-swap. False when the cycle moved on or has no bridge.
        /// </summary>
        private bool TryClaimBridgeRelease(int version, out int status)
        {
            SpinWait spinner = default;
            while (true)
            {
                int state = Volatile.Read(ref m_state);
                if (VersionOf(state) != version || (state & k_materializedBit) == 0)
                {
                    status = 0;
                    return false;
                }

                status = state & k_statusMask;
                if (status == 0)
                {
                    // Claimed but not yet published: the claim winner publishes within a few instructions.
                    spinner.SpinOnce();
                    continue;
                }

                if (Interlocked.CompareExchange(ref m_state, NextCycleWord(state), state) == state)
                {
                    return true;
                }
            }
        }
    }
}
