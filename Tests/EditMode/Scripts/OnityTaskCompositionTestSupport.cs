using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>Shared fixtures for the WhenAll, WhenAny and WhenEach composition tests.</summary>
    internal static class OnityTaskCompositionTestSupport
    {
        private const BindingFlags k_private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly TimeSpan k_timeout = TimeSpan.FromSeconds(5);

        internal static OnityTaskCompletionSource<T>[] Sources<T>(int count)
        {
            OnityTaskCompletionSource<T>[] sources = new OnityTaskCompletionSource<T>[count];
            for (int i = 0; i < count; i++)
            {
                sources[i] = new OnityTaskCompletionSource<T>();
            }

            return sources;
        }

        internal static object GetState<T>(OnityTask<T> task)
        {
            return typeof(OnityTask<T>).GetField("m_state", k_private).GetValue(task);
        }

        internal static object GetState(OnityTask task)
        {
            return typeof(OnityTask).GetField("m_state", k_private).GetValue(task);
        }

        /// <summary>Reads a private field declared on the object's type or one of its base types.</summary>
        internal static object GetField(object owner, string name)
        {
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, k_private | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(owner);
                }
            }

            throw new MissingFieldException(owner.GetType().Name, name);
        }

        /// <summary>True when a boxed task value no longer references a source.</summary>
        internal static bool IsReleasedTaskValue(object taskValue)
        {
            return GetField(taskValue, "m_state") == null;
        }

        /// <summary>True when the fault of a completed .NET task was observed.</summary>
        internal static bool FaultObserved(Task task)
        {
            object contingent = typeof(Task).GetField("m_contingentProperties", k_private).GetValue(task);
            object holder = contingent.GetType().GetField("m_exceptionsHolder", k_private).GetValue(contingent);
            return (bool)holder.GetType().GetField("m_isHandled", k_private).GetValue(holder);
        }

        internal static void SetFault<T>(OnityTaskCompletionSource<T> source, Exception fault)
        {
            typeof(OnityTaskCompletionSource<T>).GetMethod("TrySetFault", k_private)
                .Invoke(source, new object[] { fault });
        }

        internal static void Wait(Func<bool> condition)
        {
            Assert.That(SpinWait.SpinUntil(condition, k_timeout), Is.True, "Composition operation timed out.");
        }

        internal static void Join(params Task[] tasks)
        {
            Assert.That(Task.WaitAll(tasks, k_timeout), Is.True, "Owned worker timed out.");
        }

        /// <summary>
        /// Returns a typed native task whose pooled source already moved to a newer token, so every
        /// read of the returned value fails with <see cref="InvalidOperationException"/>.
        /// </summary>
        internal static OnityTask<int> CreateStaleTask()
        {
            OnityTask<int> stale = OnityTask.WhenAny(OnityTask.Completed, OnityTask.Completed);
            object source = GetState(stale);
            source.GetType().BaseType.GetMethod("Reset", k_private)
                .Invoke(source, new object[] { CancellationToken.None });
            return stale;
        }

        /// <summary>A runner-backed single-consumer task that completes when the gate opens.</summary>
        internal static async OnityTask<T> Native<T>(CompositionGate gate, T value)
        {
            await gate;
            return value;
        }

        /// <summary>A runner-backed single-consumer task that completes when the gate opens.</summary>
        internal static async OnityTask NativeUntyped(CompositionGate gate)
        {
            await gate;
        }

        /// <summary>A runner-backed single-consumer task that faults when the gate opens.</summary>
        internal static async OnityTask<T> NativeFault<T>(CompositionGate gate, Exception fault)
        {
            await gate;
            throw fault;
        }
    }

    /// <summary>A manually opened awaitable that holds one continuation.</summary>
    internal sealed class CompositionGate : ICriticalNotifyCompletion
    {
        private Action m_continuation;

        public bool IsCompleted => false;

        public CompositionGate GetAwaiter()
        {
            return this;
        }

        public void GetResult()
        {
        }

        public void OnCompleted(Action continuation)
        {
            m_continuation = continuation;
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            m_continuation = continuation;
        }

        public void Complete()
        {
            Interlocked.Exchange(ref m_continuation, null)?.Invoke();
        }
    }
}
