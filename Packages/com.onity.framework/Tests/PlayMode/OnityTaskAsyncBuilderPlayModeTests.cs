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
    /// Covers the pooled builder path that depends on the PlayerLoop: under IL2CPP a consumed
    /// runner returns to its pool from the task runner's next drain, on Mono it returns at once.
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

#if ENABLE_IL2CPP
            // The deferred return has not run yet, so a rent in the same frame gets another runner.
            ManualAwaitable earlyGate = new ManualAwaitable();
            OnityTask<int> early = ReturnAfterAsync(earlyGate, 2);
            Assert.That(GetState(early), Is.Not.SameAs(runner),
                "Under IL2CPP the runner must not be rented again before the dispatcher drained.");
#endif

            // OnityTaskRunner.Update drains the deferred return; the first frame may create the runner.
            yield return null;
            yield return null;

            ManualAwaitable lateGate = new ManualAwaitable();
            OnityTask<int> late = ReturnAfterAsync(lateGate, 3);
            Assert.That(GetState(late), Is.SameAs(runner), "The consumed runner was not pooled.");

#if ENABLE_IL2CPP
            earlyGate.Complete();
            Assert.That(early.GetAwaiter().GetResult(), Is.EqualTo(2));
#endif
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
