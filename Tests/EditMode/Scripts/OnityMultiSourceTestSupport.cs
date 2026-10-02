using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    // Scripted stream for multi-source operator tests. Items given to the constructor are served
    // synchronously by every enumeration; after them a non-manual stream ends, while a manual stream
    // stays pending until the test calls Push, End, Fail or Cancel. Disposal settles a pending move
    // with false (as compliant Onity streams do) and is written to the shared log.
    internal sealed class MultiTestStream<T> : IOnityAsyncEnumerable<T>
    {
        private readonly T[] m_items;
        private readonly Queue<T> m_buffer = new Queue<T>();
        private readonly List<string> m_log;
        private bool m_endRequested;
        private Exception m_failRequested;

        internal readonly string Name;
        internal bool Manual;
        internal bool HonorsToken = true;
        internal bool SettlesOnDispose = true;
        internal int Acquires;
        internal int Moves;
        internal int Disposals;
        internal int MoveFailureAt = -1;
        internal Exception MoveFailure;
        internal Exception DisposeFailure;
        internal CancellationToken Token;
        internal Enumerator Active;

        internal MultiTestStream(string name, List<string> log, params T[] items)
        {
            Name = name;
            m_log = log;
            m_items = items;
        }

        internal bool HasPendingMove => Active != null && Active.Pending != null;

        public IOnityAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Acquires++;
            Token = cancellationToken;
            m_log?.Add(Name + ".acquire");
            Active = new Enumerator(this, cancellationToken);
            return Active;
        }

        internal void Push(T item)
        {
            if (HasPendingMove)
            {
                Active.Settle(true, item);
                return;
            }
            m_buffer.Enqueue(item);
        }

        internal void End()
        {
            if (HasPendingMove)
            {
                Active.Settle(false, default);
                return;
            }
            m_endRequested = true;
        }

        internal void Fail(Exception exception)
        {
            if (HasPendingMove)
            {
                Active.Fault(exception);
                return;
            }
            m_failRequested = exception;
        }

        internal sealed class Enumerator : IOnityAsyncEnumerator<T>
        {
            private readonly MultiTestStream<T> m_owner;
            private readonly CancellationToken m_token;
            private CancellationTokenRegistration m_registration;
            private int m_position;
            private T m_current;
            private bool m_disposed;
            internal OnityTaskCompletionSource<bool> Pending;

            internal Enumerator(MultiTestStream<T> owner, CancellationToken token)
            {
                m_owner = owner;
                m_token = token;
            }

            public T Current => m_current;

            public OnityTask<bool> MoveNextAsync()
            {
                int move = m_owner.Moves++;
                if (move == m_owner.MoveFailureAt)
                {
                    return OnityTask<bool>.FromException(m_owner.MoveFailure);
                }
                if (m_disposed)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_owner.HonorsToken && m_token.IsCancellationRequested)
                {
                    return OnityTask<bool>.FromCanceled(m_token);
                }
                if (m_position < m_owner.m_items.Length)
                {
                    m_current = m_owner.m_items[m_position++];
                    return OnityTask<bool>.FromResult(true);
                }
                if (!m_owner.Manual)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                if (m_owner.m_buffer.Count != 0)
                {
                    m_current = m_owner.m_buffer.Dequeue();
                    return OnityTask<bool>.FromResult(true);
                }
                if (m_owner.m_failRequested != null)
                {
                    return OnityTask<bool>.FromException(m_owner.m_failRequested);
                }
                if (m_owner.m_endRequested)
                {
                    return OnityTask<bool>.FromResult(false);
                }
                var pending = new OnityTaskCompletionSource<bool>();
                Pending = pending;
                if (m_owner.HonorsToken && m_token.CanBeCanceled)
                {
                    m_registration = m_token.Register(() => Cancel(pending, m_token));
                }
                return pending.Task;
            }

            public OnityTask DisposeAsync()
            {
                m_owner.Disposals++;
                m_owner.m_log?.Add(m_owner.Name + ".dispose");
                m_disposed = true;
                if (Pending != null && m_owner.SettlesOnDispose)
                {
                    Settle(false, default);
                }
                return m_owner.DisposeFailure != null
                    ? OnityTask.FromException(m_owner.DisposeFailure) : OnityTask.Completed;
            }

            internal void Settle(bool value, T item)
            {
                OnityTaskCompletionSource<bool> pending = Pending;
                Pending = null;
                m_registration.Dispose();
                m_current = value ? item : default;
                pending.TrySetResult(value);
            }

            internal void Fault(Exception exception)
            {
                OnityTaskCompletionSource<bool> pending = Pending;
                Pending = null;
                m_registration.Dispose();
                pending.TrySetException(exception);
            }

            private void Cancel(OnityTaskCompletionSource<bool> pending, CancellationToken token)
            {
                if (ReferenceEquals(Pending, pending))
                {
                    Pending = null;
                }
                pending.TrySetCanceled(token);
            }
        }
    }

    // Records one task outcome (status read before the single GetResult call) and an optional log label.
    internal sealed class MultiTestResult<T>
    {
        internal bool Completed;
        internal bool Canceled;
        internal Exception Error;
        internal T Value;
        internal CancellationToken CanceledToken;

        internal static MultiTestResult<T> Watch(OnityTask<T> task, List<string> log = null, string label = null)
        {
            var result = new MultiTestResult<T>();
            OnityTaskAwaiter<T> awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                result.Read(task, log, label);
            }
            else
            {
                awaiter.UnsafeOnCompleted(() => result.Read(task, log, label));
            }
            return result;
        }

        private void Read(OnityTask<T> task, List<string> log, string label)
        {
            bool canceled = task.IsCanceled;
            try
            {
                Value = task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception) when (canceled)
            {
                Canceled = true;
                CanceledToken = exception.CancellationToken;
            }
            catch (Exception exception)
            {
                Error = exception;
            }
            Completed = true;
            if (log != null)
            {
                log.Add(label);
            }
        }
    }

    internal sealed class MultiTestResult
    {
        internal bool Completed;
        internal bool Canceled;
        internal Exception Error;

        internal static MultiTestResult Watch(OnityTask task, List<string> log = null, string label = null)
        {
            var result = new MultiTestResult();
            OnityTaskAwaiter awaiter = task.GetAwaiter();
            if (awaiter.IsCompleted)
            {
                result.Read(task, log, label);
            }
            else
            {
                awaiter.UnsafeOnCompleted(() => result.Read(task, log, label));
            }
            return result;
        }

        private void Read(OnityTask task, List<string> log, string label)
        {
            bool canceled = task.IsCanceled;
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (canceled)
            {
                Canceled = true;
            }
            catch (Exception exception)
            {
                Error = exception;
            }
            Completed = true;
            if (log != null)
            {
                log.Add(label);
            }
        }
    }

    internal static class MultiTest
    {
        internal static MultiTestStream<int> Ints(string name, List<string> log, params int[] items) =>
            new MultiTestStream<int>(name, log, items);

        internal static MultiTestStream<int> Manual(string name, List<string> log, params int[] items) =>
            new MultiTestStream<int>(name, log, items) { Manual = true };

        internal static T[] Drain<T>(IOnityAsyncEnumerable<T> source, CancellationToken token = default)
        {
            OnityTask<T[]> task = source.ToArrayAsync(token);
            Assert.That(task.IsCompleted, Is.True, "The stream did not complete synchronously.");
            return task.GetAwaiter().GetResult();
        }

        internal static MultiTestResult<bool> Move<T>(IOnityAsyncEnumerator<T> enumerator, List<string> log = null,
            string label = "consumer.move")
        {
            return MultiTestResult<bool>.Watch(enumerator.MoveNextAsync(), log, label);
        }

        internal static void ExpectItem<T>(IOnityAsyncEnumerator<T> enumerator, T expected)
        {
            MultiTestResult<bool> move = Move(enumerator);
            Assert.That(move.Completed, Is.True, "The move is still pending.");
            Assert.That(move.Error, Is.Null);
            Assert.That(move.Canceled, Is.False);
            Assert.That(move.Value, Is.True, "The stream ended early.");
            Assert.That(enumerator.Current, Is.EqualTo(expected));
        }

        internal static void ExpectEnd<T>(IOnityAsyncEnumerator<T> enumerator)
        {
            MultiTestResult<bool> move = Move(enumerator);
            Assert.That(move.Completed, Is.True, "The move is still pending.");
            Assert.That(move.Error, Is.Null);
            Assert.That(move.Canceled, Is.False);
            Assert.That(move.Value, Is.False, "The stream yielded an unexpected item.");
        }

        internal static void Dispose<T>(IOnityAsyncEnumerator<T> enumerator)
        {
            MultiTestResult disposal = MultiTestResult.Watch(enumerator.DisposeAsync());
            Assert.That(disposal.Completed, Is.True, "Disposal is still pending.");
            Assert.That(disposal.Error, Is.Null);
        }

        internal static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
