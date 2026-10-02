using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// A5: the implicit typed-to-untyped view (P-H4) and native-source tracking of forgotten tasks (P-TRK).
    /// </summary>
    public sealed class OnityTaskNativeTrackingEditModeTests
    {
        private bool m_previousTracking;
        private bool m_previousStackTrace;

        [SetUp]
        public void SetUp()
        {
            m_previousTracking = OnityTaskTracker.IsEnabled;
            m_previousStackTrace = OnityTaskTracker.EnableStackTrace;
            OnityTaskTracker.EnableStackTrace = false;
            OnityTaskTracker.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            OnityTaskTracker.ClearAll();
            OnityTaskTracker.IsEnabled = m_previousTracking;
            OnityTaskTracker.EnableStackTrace = m_previousStackTrace;
        }

        [Test]
        public void ImplicitConversion_SharesTheSourceAndTokenWithoutConsuming()
        {
            OnityTask completed = OnityTask<int>.FromResult(3);
            Assert.That(completed.IsCompletedSuccessfully, Is.True);

            var gate = new OnityTaskCompletionSource();
            OnityTask<int> typed = ReturnAfterAsync(gate, 5);
            OnityTask view = typed;
            Assert.That(State(view), Is.SameAs(State(typed)));
            Assert.That(view.IsCompleted, Is.False);
            gate.TrySetResult();
            Assert.That(view.IsCompletedSuccessfully, Is.True);
            // The view did not consume: the typed task still delivers its result once.
            Assert.That(typed.GetAwaiter().GetResult(), Is.EqualTo(5));
        }

        [Test]
        public void Forget_WithTracking_RecordsANativeRowWithoutABridge()
        {
            OnityTaskTracker.IsEnabled = true;
            var succeed = new OnityTaskCompletionSource();
            var fail = new OnityTaskCompletionSource();
            WaitAsync(succeed).Forget();
            ReturnAfterAsync(fail, 1).Forget(exception => { });

            var rows = new List<OnityTrackedTaskInfo>();
            OnityTaskTracker.GetSnapshot(rows);
            Assert.That(rows.Count, Is.EqualTo(2));
            foreach (OnityTrackedTaskInfo row in rows)
            {
                Assert.That(row.IsNative, Is.True);
                Assert.That(row.TaskId, Is.LessThan(0));
                Assert.That(row.IsCompleted, Is.False);
                Assert.That(row.Status, Is.EqualTo(TaskStatus.WaitingForActivation));
            }

            succeed.TrySetResult();
            fail.TrySetException(new InvalidOperationException("tracked failure"));
            OnityTaskTracker.GetSnapshot(rows);
            Assert.That(rows[0].Source, Is.EqualTo("OnityTaskExtensions.Forget"));
            Assert.That(rows[0].Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(rows[1].Source, Is.EqualTo("OnityTaskExtensions.Forget<T>"));
            Assert.That(rows[1].Status, Is.EqualTo(TaskStatus.Faulted));
            Assert.That(rows[1].ErrorMessage, Is.EqualTo("tracked failure"));
        }

        [Test]
        public void Forget_WithoutTracking_RecordsNothing()
        {
            OnityTaskTracker.IsEnabled = false;
            var gate = new OnityTaskCompletionSource();
            WaitAsync(gate).Forget();
            gate.TrySetResult();
            var rows = new List<OnityTrackedTaskInfo>();
            OnityTaskTracker.GetSnapshot(rows);
            Assert.That(rows, Is.Empty);
        }

        private static async OnityTask WaitAsync(OnityTaskCompletionSource gate)
        {
            await gate.Task;
        }

        private static async OnityTask<int> ReturnAfterAsync(OnityTaskCompletionSource gate, int value)
        {
            await gate.Task;
            return value;
        }

        private static object State(OnityTask task)
        {
            return typeof(OnityTask).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
        }

        private static object State<T>(OnityTask<T> task)
        {
            return typeof(OnityTask<T>).GetField("m_state", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(task);
        }
    }
}
