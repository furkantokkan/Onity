using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Task helpers for fire-and-forget usage in Unity gameplay code.
    /// </summary>
    public static class OnityTaskExtensions
    {
        /// <summary>
        /// Executes task without awaiting and routes exceptions to callback or Unity log.
        /// </summary>
        /// <param name="task">Task instance.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static async void Forget(this Task task, Action<Exception> exceptionHandler = null)
        {
            if (task == null)
            {
                return;
            }

            task = OnityTaskTracker.Track(task, "OnityTaskExtensions.Forget");

            try
            {
                await task;
            }
            catch (Exception exception)
            {
                if (exceptionHandler != null)
                {
                    exceptionHandler(exception);
                    return;
                }

                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Executes generic task without awaiting and routes exceptions to callback or Unity log.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="task">Task instance.</param>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public static async void Forget<T>(this Task<T> task, Action<Exception> exceptionHandler = null)
        {
            if (task == null)
            {
                return;
            }

            task = OnityTaskTracker.Track(task, "OnityTaskExtensions.Forget<T>");

            try
            {
                await task;
            }
            catch (Exception exception)
            {
                if (exceptionHandler != null)
                {
                    exceptionHandler(exception);
                    return;
                }

                Debug.LogException(exception);
            }
        }
    }
}
