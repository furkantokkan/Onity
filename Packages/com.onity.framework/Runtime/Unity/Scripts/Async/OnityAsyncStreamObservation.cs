using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Onity.Unity.Async
{
    // Awaits a task and returns its outcome instead of throwing it, so async helpers keep fault and
    // cancellation apart (a rethrown OperationCanceledException would otherwise become cancellation).
    // The task is observed exactly once.
    internal readonly struct OnityStreamOutcomeAwaiter : ICriticalNotifyCompletion
    {
        private readonly OnityTask m_task;

        internal OnityStreamOutcomeAwaiter(OnityTask task)
        {
            m_task = task;
        }

        public bool IsCompleted => m_task.IsCompleted;

        public OnityStreamOutcomeAwaiter GetAwaiter() => this;

        public OnityAsyncStreamOutcome GetResult()
        {
            try
            {
                return OnityAsyncStreamOutcome.Read(m_task);
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
        }

        public void OnCompleted(Action continuation) => Register(continuation);

        public void UnsafeOnCompleted(Action continuation) => Register(continuation);

        private void Register(Action continuation)
        {
            try
            {
                m_task.RegisterWhenAnyObserver(continuation);
            }
            catch (Exception)
            {
                // A rejected registration still resumes the caller; GetResult then reports why.
                continuation();
            }
        }
    }

    // Maps operator-internal tasks to the bool input of OnityAsyncEnumeratorBase: success becomes
    // true after the optional value handler ran, fault and cancellation keep their status.
    internal static class OnityStageTasks
    {
        internal static readonly OnityTask<bool> True = OnityTask<bool>.FromResult(true);
        internal static readonly OnityTask<bool> False = OnityTask<bool>.FromResult(false);

        internal static OnityTask<bool> Continue(OnityTask task)
        {
            return OnityAwaitStreamCompletion.Map(task);
        }

        internal static OnityTask<bool> Continue<TValue>(OnityTask<TValue> task, Action<TValue> store)
        {
            if (task.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome;
                TValue value;
                try
                {
                    outcome = OnityAwaitStreamObserver<TValue>.Read(task, out value);
                }
                catch (Exception exception)
                {
                    outcome = OnityAsyncStreamOutcome.Failure(exception);
                    value = default;
                }
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    store(value);
                    return True;
                }
                return Failed(outcome);
            }
            var output = new OnityTaskCompletionSource<bool>();
            new OnityAwaitStreamObserver<TValue>(task, new ValueRelay<TValue>(store, output).Invoke).Register();
            return output.Task;
        }

        internal static OnityTask<bool> Failed(OnityAsyncStreamOutcome outcome)
        {
            var failed = new OnityTaskCompletionSource<bool>();
            outcome.Publish(failed, false);
            return failed.Task;
        }

        internal static OnityTask Fail(OnityAsyncStreamOutcome outcome)
        {
            var failed = new OnityTaskCompletionSource();
            outcome.Publish<bool>(failed, true);
            return failed.Task;
        }

        internal static OnityTask<TValue> FromOutcome<TValue>(OnityAsyncStreamOutcome outcome, TValue value)
        {
            if (outcome.Status == OnityTaskSourceStatus.Succeeded)
            {
                return OnityTask<TValue>.FromResult(value);
            }
            var failed = new OnityTaskCompletionSource<TValue>();
            outcome.Publish(failed, value);
            return failed.Task;
        }

        internal static OnityAsyncStreamOutcome Canceled(CancellationToken token)
        {
            return new OnityAsyncStreamOutcome { Status = OnityTaskSourceStatus.Canceled, Token = token };
        }

        // Maps a never-throwing cleanup run to a cleanup task without allocating for the
        // synchronous successful path.
        internal static OnityTask FromRun(OnityTask<OnityAsyncStreamOutcome> run)
        {
            if (run.IsCompleted)
            {
                OnityAsyncStreamOutcome outcome = Read(run);
                return outcome.Status == OnityTaskSourceStatus.Succeeded ? OnityTask.Completed : Fail(outcome);
            }
            var output = new OnityTaskCompletionSource();
            new OnityAwaitStreamObserver<OnityAsyncStreamOutcome>(run, (observed, outcome) =>
            {
                if (observed.Status != OnityTaskSourceStatus.Succeeded)
                {
                    outcome = observed;
                }
                outcome.Publish<bool>(output, true);
            }).Register();
            return output.Task;
        }

        private static OnityAsyncStreamOutcome Read(OnityTask<OnityAsyncStreamOutcome> run)
        {
            try
            {
                OnityAsyncStreamOutcome observed = OnityAwaitStreamObserver<OnityAsyncStreamOutcome>.Read(
                    run, out OnityAsyncStreamOutcome outcome);
                return observed.Status == OnityTaskSourceStatus.Succeeded ? outcome : observed;
            }
            catch (Exception exception)
            {
                return OnityAsyncStreamOutcome.Failure(exception);
            }
        }

        private sealed class ValueRelay<TValue>
        {
            private readonly Action<TValue> m_store;
            private readonly OnityTaskCompletionSource<bool> m_output;

            internal ValueRelay(Action<TValue> store, OnityTaskCompletionSource<bool> output)
            {
                m_store = store;
                m_output = output;
            }

            internal void Invoke(OnityAsyncStreamOutcome outcome, TValue value)
            {
                if (outcome.Status == OnityTaskSourceStatus.Succeeded)
                {
                    try
                    {
                        m_store(value);
                    }
                    catch (Exception exception)
                    {
                        outcome = OnityAsyncStreamOutcome.Failure(exception);
                    }
                }
                outcome.Publish(m_output, true);
            }
        }
    }

    // Token registration that never captures the execution context (as the stream operators do).
    internal static class OnityStreamTokens
    {
        internal static CancellationTokenRegistration Register(CancellationToken token,
            Action<object> callback, object state)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return token.Register(callback, state, false);
            }
            AsyncFlowControl flow = ExecutionContext.SuppressFlow();
            try
            {
                return token.Register(callback, state, false);
            }
            finally
            {
                flow.Undo();
            }
        }
    }
}
