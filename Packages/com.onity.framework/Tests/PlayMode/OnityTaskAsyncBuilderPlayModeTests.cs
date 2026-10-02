using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine.TestTools;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Covers the pooled builder path in Play: a consumed runner returns to its pool at once on every
    /// backend and is rented again by the next suspension of the same method.
    /// </summary>
    [TestFixture]
    public sealed class OnityTaskAsyncBuilderPlayModeTests
    {
        [UnityTest]
        public IEnumerator SuspendedMethod_ConsumedRunner_IsRentedAgainAfterTheNextFrame()
        {
            ManualAwaitable firstGate = new ManualAwaitable();
            OnityTask<int> first = ReturnAfterAsync(firstGate, 1);
            object runner = GetState(first);
            firstGate.Complete();
            first.GetAwaiter().GetResult();
            Assert.Throws<InvalidOperationException>(() => _ = first.IsCompleted, "The token retires synchronously.");

            // The runner returns to its pool at consumption on every backend.
            ManualAwaitable earlyGate = new ManualAwaitable();
            OnityTask<int> early = ReturnAfterAsync(earlyGate, 2);
            Assert.That(GetState(early), Is.SameAs(runner), "The consumed runner was not pooled immediately.");
            earlyGate.Complete();
            Assert.That(early.GetAwaiter().GetResult(), Is.EqualTo(2));

            yield return null;
            yield return null;

            ManualAwaitable lateGate = new ManualAwaitable();
            OnityTask<int> late = ReturnAfterAsync(lateGate, 3);
            Assert.That(GetState(late), Is.SameAs(runner), "The consumed runner was not pooled.");
            lateGate.Complete();
            Assert.That(late.GetAwaiter().GetResult(), Is.EqualTo(3));
        }

        private static object GetState<T>(OnityTask<T> task)
        {
            FieldInfo field = typeof(OnityTask<T>).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(task);
        }

        private static async OnityTask<int> ReturnAfterAsync(ManualAwaitable gate, int value)
        {
            await gate;
            return value;
        }

        /// <summary>
        /// Critical awaitable that stores its continuation and runs it on the completing thread.
        /// </summary>
        private sealed class ManualAwaitable : ICriticalNotifyCompletion
        {
            private Action m_continuation;

            public bool IsCompleted => false;

            public ManualAwaitable GetAwaiter()
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
                Action continuation = Interlocked.Exchange(ref m_continuation, null);
                if (continuation == null)
                {
                    throw new InvalidOperationException("No continuation was registered.");
                }

                continuation();
            }
        }
    }
}
