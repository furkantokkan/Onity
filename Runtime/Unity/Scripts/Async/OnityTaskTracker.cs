using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Tracks Onity task activity for editor diagnostics.
    /// </summary>
    public static class OnityTaskTracker
    {
        private const int k_maxTrackedTasks = 1024;

        private static readonly object s_gate = new object();
        private static readonly Dictionary<int, TrackedTaskEntry> s_entryByTaskId = new Dictionary<int, TrackedTaskEntry>(256);
        private static readonly Queue<int> s_taskOrder = new Queue<int>(256);
        private static bool s_isEnabled = true;
        private static bool s_enableStackTrace;

        /// <summary>
        /// Enables or disables task tracking.
        /// </summary>
        public static bool IsEnabled
        {
            get => s_isEnabled;
            set => s_isEnabled = value;
        }

        /// <summary>
        /// Captures stack traces for tracked tasks.
        /// </summary>
        public static bool EnableStackTrace
        {
            get => s_enableStackTrace;
            set => s_enableStackTrace = value;
        }

        /// <summary>
        /// Tracks task lifecycle and returns the same task.
        /// </summary>
        /// <param name="task">Task to track.</param>
        /// <param name="source">Optional source label.</param>
        /// <returns>The same task instance.</returns>
        public static Task Track(Task task, string source = null)
        {
            if (task == null || s_isEnabled == false)
            {
                return task;
            }

            TrackInternal(task, source);
            return task;
        }

        /// <summary>
        /// Tracks task lifecycle and returns the same task.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="task">Task to track.</param>
        /// <param name="source">Optional source label.</param>
        /// <returns>The same task instance.</returns>
        public static Task<T> Track<T>(Task<T> task, string source = null)
        {
            if (task == null || s_isEnabled == false)
            {
                return task;
            }

            TrackInternal(task, source);
            return task;
        }

        /// <summary>
        /// Copies tracker snapshot rows into the destination list.
        /// </summary>
        /// <param name="results">Destination list.</param>
        /// <param name="includeCompleted">Include completed tasks in snapshot.</param>
        public static void GetSnapshot(List<OnityTrackedTaskInfo> results, bool includeCompleted = true)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            lock (s_gate)
            {
                DateTime now = DateTime.UtcNow;

                foreach (int taskId in s_taskOrder)
                {
                    if (s_entryByTaskId.TryGetValue(taskId, out TrackedTaskEntry entry) == false)
                    {
                        continue;
                    }

                    if (includeCompleted == false && entry.IsCompleted)
                    {
                        continue;
                    }

                    results.Add(entry.ToInfo(now));
                }
            }
        }

        /// <summary>
        /// Clears only completed task entries.
        /// </summary>
        public static void ClearCompleted()
        {
            lock (s_gate)
            {
                List<int> remainingTaskIds = new List<int>(s_taskOrder.Count);

                while (s_taskOrder.Count > 0)
                {
                    int taskId = s_taskOrder.Dequeue();

                    if (s_entryByTaskId.TryGetValue(taskId, out TrackedTaskEntry entry) == false)
                    {
                        continue;
                    }

                    if (entry.IsCompleted)
                    {
                        s_entryByTaskId.Remove(taskId);
                        continue;
                    }

                    remainingTaskIds.Add(taskId);
                }

                for (int i = 0; i < remainingTaskIds.Count; i++)
                {
                    s_taskOrder.Enqueue(remainingTaskIds[i]);
                }
            }
        }

        /// <summary>
        /// Clears all task tracker entries.
        /// </summary>
        public static void ClearAll()
        {
            lock (s_gate)
            {
                s_entryByTaskId.Clear();
                s_taskOrder.Clear();
            }
        }

        private static void TrackInternal(Task task, string source)
        {
            int taskId = task.Id;
            string stackTrace = s_enableStackTrace ? Environment.StackTrace : string.Empty;

            lock (s_gate)
            {
                if (s_entryByTaskId.ContainsKey(taskId) == false)
                {
                    s_entryByTaskId.Add(
                        taskId,
                        new TrackedTaskEntry(
                            taskId,
                            source,
                            DateTime.UtcNow,
                            task.Status,
                            stackTrace));

                    s_taskOrder.Enqueue(taskId);
                    TrimOverflow_NoAlloc();
                }
            }

            if (task.IsCompleted)
            {
                CompleteTrackedTask(taskId, task);
                return;
            }

            task.ContinueWith(
                completedTask => CompleteTrackedTask(taskId, completedTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void CompleteTrackedTask(int taskId, Task task)
        {
            lock (s_gate)
            {
                if (s_entryByTaskId.TryGetValue(taskId, out TrackedTaskEntry entry) == false)
                {
                    return;
                }

                entry.Status = task.Status;
                entry.IsCompleted = true;
                entry.CompletedAtUtc = DateTime.UtcNow;

                if (task.IsFaulted && task.Exception != null)
                {
                    entry.ErrorMessage = task.Exception.GetBaseException().Message;
                }
                else if (task.IsCanceled)
                {
                    entry.ErrorMessage = "Canceled";
                }

                s_entryByTaskId[taskId] = entry;
            }
        }

        private static void TrimOverflow_NoAlloc()
        {
            while (s_taskOrder.Count > k_maxTrackedTasks)
            {
                int oldestTaskId = s_taskOrder.Dequeue();
                s_entryByTaskId.Remove(oldestTaskId);
            }
        }

        private struct TrackedTaskEntry
        {
            public readonly int TaskId;
            public readonly string Source;
            public readonly DateTime StartedAtUtc;
            public readonly string StackTrace;
            public TaskStatus Status;
            public bool IsCompleted;
            public DateTime CompletedAtUtc;
            public string ErrorMessage;

            public TrackedTaskEntry(
                int taskId,
                string source,
                DateTime startedAtUtc,
                TaskStatus status,
                string stackTrace)
            {
                TaskId = taskId;
                Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
                StartedAtUtc = startedAtUtc;
                StackTrace = stackTrace ?? string.Empty;
                Status = status;
                IsCompleted = false;
                CompletedAtUtc = default;
                ErrorMessage = string.Empty;
            }

            public OnityTrackedTaskInfo ToInfo(DateTime nowUtc)
            {
                DateTime endTime = IsCompleted && CompletedAtUtc != default ? CompletedAtUtc : nowUtc;
                double elapsedMilliseconds = (endTime - StartedAtUtc).TotalMilliseconds;

                return new OnityTrackedTaskInfo(
                    TaskId,
                    Source,
                    Status,
                    IsCompleted,
                    StartedAtUtc,
                    elapsedMilliseconds,
                    ErrorMessage,
                    StackTrace);
            }
        }
    }

    /// <summary>
    /// Snapshot row used by editor task tracker.
    /// </summary>
    public readonly struct OnityTrackedTaskInfo
    {
        /// <summary>
        /// Task identifier.
        /// </summary>
        public int TaskId { get; }

        /// <summary>
        /// Source label assigned by tracker callers.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Current task status.
        /// </summary>
        public TaskStatus Status { get; }

        /// <summary>
        /// True when task has completed.
        /// </summary>
        public bool IsCompleted { get; }

        /// <summary>
        /// Task start timestamp in UTC.
        /// </summary>
        public DateTime StartedAtUtc { get; }

        /// <summary>
        /// Elapsed milliseconds from start to completion or now.
        /// </summary>
        public double ElapsedMilliseconds { get; }

        /// <summary>
        /// Fault or cancel details.
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Captured stack trace.
        /// </summary>
        public string StackTrace { get; }

        /// <summary>
        /// Initializes a snapshot row.
        /// </summary>
        /// <param name="taskId">Task id.</param>
        /// <param name="source">Source label.</param>
        /// <param name="status">Task status.</param>
        /// <param name="isCompleted">Completion flag.</param>
        /// <param name="startedAtUtc">Start time UTC.</param>
        /// <param name="elapsedMilliseconds">Elapsed milliseconds.</param>
        /// <param name="errorMessage">Error details.</param>
        public OnityTrackedTaskInfo(
            int taskId,
            string source,
            TaskStatus status,
            bool isCompleted,
            DateTime startedAtUtc,
            double elapsedMilliseconds,
            string errorMessage,
            string stackTrace)
        {
            TaskId = taskId;
            Source = source ?? "Unknown";
            Status = status;
            IsCompleted = isCompleted;
            StartedAtUtc = startedAtUtc;
            ElapsedMilliseconds = elapsedMilliseconds;
            ErrorMessage = errorMessage ?? string.Empty;
            StackTrace = stackTrace ?? string.Empty;
        }
    }
}
