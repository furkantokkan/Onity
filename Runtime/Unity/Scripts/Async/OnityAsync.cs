using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onity.Core;
using Onity.Messaging;
using Onity.Reactive;
using Onity.Unity.Reactive;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Onity-owned awaitable facade for Unity gameplay async flows.
    /// Pooled Unity operations are single-consumer and must be awaited only once.
    /// </summary>
    [AsyncMethodBuilder(typeof(OnityTaskMethodBuilder))]
    public readonly struct OnityTask
    {
        /// <summary>
        /// Completed Onity task.
        /// </summary>
        public static readonly OnityTask CompletedTask = default;

        private readonly object m_state;
        private readonly int m_token;

        /// <summary>
        /// Initializes a task wrapper.
        /// </summary>
        /// <param name="task">Wrapped task.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTask(Task task)
        {
            m_state = task ?? throw new ArgumentNullException(nameof(task));
            m_token = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTask(IOnityTaskSource source)
        {
            m_state = source ?? throw new ArgumentNullException(nameof(source));
            m_token = source.Version;
        }

        /// <summary>
        /// Completed Onity task.
        /// </summary>
        public static OnityTask Completed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => CompletedTask;
        }

        /// <summary>
        /// Creates a typed task completed with a result.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="result">Result value.</param>
        /// <returns>Completed typed task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OnityTask<T> FromResult<T>(T result)
        {
            return new OnityTask<T>(result);
        }

        /// <summary>
        /// True when the wrapped task completed.
        /// </summary>
        public bool IsCompleted => m_state == null
            || (m_state is IOnityTaskSource source
                ? source.GetStatus(m_token) != OnityTaskSourceStatus.Pending
                : ((Task)m_state).IsCompleted);

        /// <summary>
        /// True when the wrapped task completed successfully.
        /// </summary>
        public bool IsCompletedSuccessfully =>
            m_state == null
            || (m_state is IOnityTaskSource source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Succeeded
                : ((Task)m_state).IsCompletedSuccessfully);

        /// <summary>
        /// True when the wrapped task is canceled.
        /// </summary>
        public bool IsCanceled => m_state != null
            && (m_state is IOnityTaskSource source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Canceled
                : ((Task)m_state).IsCanceled);

        /// <summary>
        /// True when the wrapped task is faulted.
        /// </summary>
        public bool IsFaulted => m_state != null
            && (m_state is IOnityTaskSource source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Faulted
                : ((Task)m_state).IsFaulted);

        /// <summary>
        /// Wraps a task as an Onity task.
        /// </summary>
        /// <param name="task">Task to wrap.</param>
        /// <returns>Onity task.</returns>
        public static OnityTask FromTask(Task task)
        {
            return new OnityTask(task);
        }

        /// <summary>
        /// Creates a canceled Onity task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Canceled task.</returns>
        public static OnityTask FromCanceled(CancellationToken cancellationToken)
        {
            return new OnityTask(Task.FromCanceled(cancellationToken));
        }

        /// <summary>
        /// Creates a faulted Onity task.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        /// <returns>Faulted task.</returns>
        public static OnityTask FromException(Exception exception)
        {
            return new OnityTask(Task.FromException(exception));
        }

        /// <summary>
        /// Returns the wrapped task for interop.
        /// </summary>
        /// <returns>Wrapped task.</returns>
        public Task AsTask()
        {
            if (m_state == null)
            {
                return Task.CompletedTask;
            }

            return m_state is IOnityTaskSource source ? source.AsTask(m_token) : (Task)m_state;
        }

        /// <summary>
        /// Returns the task awaiter.
        /// </summary>
        /// <returns>Task awaiter.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTaskAwaiter GetAwaiter()
        {
            return new OnityTaskAwaiter(m_state, m_token);
        }

        /// <summary>
        /// Runs task without awaiting and routes exceptions to callback or Unity log.
        /// </summary>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public void Forget(Action<Exception> exceptionHandler = null)
        {
            AsTask().Forget(exceptionHandler);
        }

        /// <summary>
        /// Awaits one rendered frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask NextFrame(CancellationToken cancellationToken = default)
        {
            return cancellationToken.IsCancellationRequested
                ? FromCanceled(cancellationToken)
                : new OnityTask(OnityFrameTaskSource.Rent(OnityTaskLoopPhase.Update, cancellationToken));
        }

        /// <summary>
        /// Awaits one fixed update frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask NextFixedFrame(CancellationToken cancellationToken = default)
        {
            return cancellationToken.IsCancellationRequested
                ? FromCanceled(cancellationToken)
                : new OnityTask(OnityFrameTaskSource.Rent(OnityTaskLoopPhase.FixedUpdate, cancellationToken));
        }

        /// <summary>
        /// Awaits one late update frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask NextLateFrame(CancellationToken cancellationToken = default)
        {
            return cancellationToken.IsCancellationRequested
                ? FromCanceled(cancellationToken)
                : new OnityTask(OnityFrameTaskSource.Rent(OnityTaskLoopPhase.LateUpdate, cancellationToken));
        }

        /// <summary>
        /// Awaits a scaled delay in seconds.
        /// </summary>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask Delay(float delaySeconds, CancellationToken cancellationToken = default)
        {
            return Delay(delaySeconds, false, cancellationToken);
        }

        /// <summary>
        /// Awaits a delay in seconds.
        /// </summary>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask Delay(
            float delaySeconds,
            bool useUnscaledTime,
            CancellationToken cancellationToken = default)
        {
            if (delaySeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            if (delaySeconds <= 0f)
            {
                return Completed;
            }

            return new OnityTask(
                OnityDelayTaskSource.Rent(delaySeconds, useUnscaledTime, cancellationToken));
        }

        /// <summary>
        /// Awaits an unscaled delay in seconds.
        /// </summary>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask DelayUnscaled(float delaySeconds, CancellationToken cancellationToken = default)
        {
            return Delay(delaySeconds, true, cancellationToken);
        }

        /// <summary>
        /// Awaits a delay using a time provider.
        /// </summary>
        /// <param name="delay">Delay duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask Delay(
            TimeSpan delay,
            OnityTimeProvider timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (timeProvider != null)
            {
                return FromTask(OnityAsync.DelayAsync(delay, timeProvider, cancellationToken));
            }

            double totalSeconds = delay.TotalSeconds;
            if (totalSeconds > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            return Delay((float)totalSeconds, false, cancellationToken);
        }

        /// <summary>
        /// Awaits until predicate returns true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask WaitUntil(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            return predicate()
                ? Completed
                : new OnityTask(OnityPredicateTaskSource.Rent(predicate, false, cancellationToken));
        }

        /// <summary>
        /// Awaits while predicate remains true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask WaitWhile(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return FromCanceled(cancellationToken);
            }

            return predicate() == false
                ? Completed
                : new OnityTask(OnityPredicateTaskSource.Rent(predicate, true, cancellationToken));
        }

        /// <summary>
        /// Awaits a Unit observable.
        /// </summary>
        /// <param name="observable">Source observable.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask AwaitObservable(
            IOnityObservable<Unit> observable,
            CancellationToken cancellationToken = default)
        {
            return FromTask(OnityAsync.AwaitObservable(observable, cancellationToken));
        }

        /// <summary>
        /// Awaits completion of all Onity tasks.
        /// </summary>
        /// <param name="tasks">Task list.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask WhenAll(params OnityTask[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            Task[] taskArray = new Task[tasks.Length];

            for (int i = 0; i < tasks.Length; i++)
            {
                taskArray[i] = tasks[i].AsTask();
            }

            return FromTask(OnityAsync.WhenAll(taskArray));
        }

        /// <summary>
        /// Loads a scene in single mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask LoadScene(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(OnitySceneLoader.LoadSingleAsync(sceneName, onProgress, cancellationToken));
        }

        /// <summary>
        /// Loads a scene in single mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask LoadScene(
            string sceneName,
            CancellationToken cancellationToken)
        {
            return LoadScene(sceneName, null, cancellationToken);
        }

        /// <summary>
        /// Loads a scene in additive mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask LoadSceneAdditive(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(OnitySceneLoader.LoadAdditiveAsync(sceneName, onProgress, cancellationToken));
        }

        /// <summary>
        /// Loads a scene in additive mode and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask LoadSceneAdditive(
            string sceneName,
            CancellationToken cancellationToken)
        {
            return LoadSceneAdditive(sceneName, null, cancellationToken);
        }

        /// <summary>
        /// Loads a scene and returns the underlying async operation.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="loadSceneMode">Load mode.</param>
        /// <param name="activateOnLoad">Scene activation flag.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Loaded operation task.</returns>
        public static OnityTask<AsyncOperation> LoadSceneAsync(
            string sceneName,
            LoadSceneMode loadSceneMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTask<AsyncOperation>.FromTask(
                OnitySceneLoader.LoadAsync(
                    sceneName,
                    loadSceneMode,
                    activateOnLoad,
                    onProgress,
                    cancellationToken));
        }

        /// <summary>
        /// Activates a prepared scene load operation.
        /// </summary>
        /// <param name="operation">Prepared scene load operation.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask ActivateScene(
            AsyncOperation operation,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(OnitySceneLoader.ActivateAsync(operation, onProgress, cancellationToken));
        }

        /// <summary>
        /// Activates a prepared scene load operation.
        /// </summary>
        /// <param name="operation">Prepared scene load operation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask ActivateScene(
            AsyncOperation operation,
            CancellationToken cancellationToken)
        {
            return ActivateScene(operation, null, cancellationToken);
        }

        /// <summary>
        /// Unloads a scene and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="onProgress">Optional normalized progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask UnloadScene(
            string sceneName,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return FromTask(OnitySceneLoader.UnloadAsync(sceneName, onProgress, cancellationToken));
        }

        /// <summary>
        /// Unloads a scene and awaits completion.
        /// </summary>
        /// <param name="sceneName">Scene name from Build Settings.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask UnloadScene(
            string sceneName,
            CancellationToken cancellationToken)
        {
            return UnloadScene(sceneName, null, cancellationToken);
        }

        /// <summary>
        /// Sends a Unity web request and returns the completed request.
        /// </summary>
        /// <param name="request">Request instance. Caller owns disposal.</param>
        /// <param name="onProgress">Optional download progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completed request task.</returns>
        public static OnityTask<UnityWebRequest> Send(
            UnityWebRequest request,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return OnityTask<UnityWebRequest>.FromTask(
                OnityTaskTracker.Track(
                    SendInternalAsync(request, onProgress, cancellationToken),
                    "OnityTask.Send"));
        }

        /// <summary>
        /// Sends a Unity web request and returns the completed request.
        /// </summary>
        /// <param name="request">Request instance. Caller owns disposal.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completed request task.</returns>
        public static OnityTask<UnityWebRequest> Send(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            return Send(request, null, cancellationToken);
        }

        /// <summary>
        /// Sends a GET request and deserializes a JSON response.
        /// </summary>
        /// <typeparam name="TResponse">Response DTO type.</typeparam>
        /// <param name="url">Request URL.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response DTO task.</returns>
        public static OnityTask<TResponse> GetJson<TResponse>(
            string url,
            CancellationToken cancellationToken = default)
        {
            return GetJson<TResponse>(url, null, cancellationToken);
        }

        /// <summary>
        /// Sends a GET request and deserializes a JSON response.
        /// </summary>
        /// <typeparam name="TResponse">Response DTO type.</typeparam>
        /// <param name="url">Request URL.</param>
        /// <param name="onProgress">Optional download progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response DTO task.</returns>
        public static OnityTask<TResponse> GetJson<TResponse>(
            string url,
            Action<float> onProgress,
            CancellationToken cancellationToken = default)
        {
            return OnityTask<TResponse>.FromTask(
                OnityTaskTracker.Track(
                    GetJsonInternalAsync<TResponse>(url, onProgress, cancellationToken),
                    "OnityTask.GetJson"));
        }

        /// <summary>
        /// Sends a JSON POST request and deserializes a JSON response.
        /// </summary>
        /// <typeparam name="TRequest">Request DTO type.</typeparam>
        /// <typeparam name="TResponse">Response DTO type.</typeparam>
        /// <param name="url">Request URL.</param>
        /// <param name="request">Request DTO.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response DTO task.</returns>
        public static OnityTask<TResponse> PostJson<TRequest, TResponse>(
            string url,
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            return PostJson<TRequest, TResponse>(url, request, null, cancellationToken);
        }

        /// <summary>
        /// Sends a JSON POST request and deserializes a JSON response.
        /// </summary>
        /// <typeparam name="TRequest">Request DTO type.</typeparam>
        /// <typeparam name="TResponse">Response DTO type.</typeparam>
        /// <param name="url">Request URL.</param>
        /// <param name="request">Request DTO.</param>
        /// <param name="onProgress">Optional download progress callback (0..1).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Response DTO task.</returns>
        public static OnityTask<TResponse> PostJson<TRequest, TResponse>(
            string url,
            TRequest request,
            Action<float> onProgress,
            CancellationToken cancellationToken = default)
        {
            return OnityTask<TResponse>.FromTask(
                OnityTaskTracker.Track(
                    PostJsonInternalAsync<TRequest, TResponse>(
                        url,
                        request,
                        onProgress,
                        cancellationToken),
                    "OnityTask.PostJson"));
        }

        private static async Task<UnityWebRequest> SendInternalAsync(
            UnityWebRequest request,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            CancellationTokenRegistration registration = default;

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(request.Abort);
            }

            try
            {
                onProgress?.Invoke(0f);

                while (operation.isDone == false)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    onProgress?.Invoke(GetRequestProgress(request, operation));
                    await Task.Yield();
                }

                cancellationToken.ThrowIfCancellationRequested();
                onProgress?.Invoke(1f);

                if (IsFailed(request))
                {
                    throw new OnityUnityWebRequestException(request);
                }

                return request;
            }
            finally
            {
                registration.Dispose();
            }
        }

        private static async Task<TResponse> GetJsonInternalAsync<TResponse>(
            string url,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            ValidateUrl(url);

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequest completedRequest = await SendInternalAsync(
                request,
                onProgress,
                cancellationToken);

            return DeserializeJson<TResponse>(completedRequest.downloadHandler?.text);
        }

        private static async Task<TResponse> PostJsonInternalAsync<TRequest, TResponse>(
            string url,
            TRequest requestDto,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            ValidateUrl(url);

            string json = SerializeJson(requestDto);
            byte[] payload = Encoding.UTF8.GetBytes(json);

            using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequest completedRequest = await SendInternalAsync(
                request,
                onProgress,
                cancellationToken);

            return DeserializeJson<TResponse>(completedRequest.downloadHandler?.text);
        }

        private static string SerializeJson<T>(T value)
        {
            if (value == null)
            {
                return "{}";
            }

            return JsonUtility.ToJson(value);
        }

        private static T DeserializeJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            return JsonUtility.FromJson<T>(json);
        }

        private static void ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be empty.", nameof(url));
            }
        }

        private static float GetRequestProgress(
            UnityWebRequest request,
            UnityWebRequestAsyncOperation operation)
        {
            if (request.downloadProgress >= 0f)
            {
                return Mathf.Clamp01(request.downloadProgress);
            }

            return Mathf.Clamp01(operation.progress);
        }

        private static bool IsFailed(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.ConnectionError
                || request.result == UnityWebRequest.Result.ProtocolError
                || request.result == UnityWebRequest.Result.DataProcessingError;
        }
    }

    /// <summary>
    /// Onity-owned awaitable facade for Unity gameplay async flows with a typed result.
    /// Pooled Unity operations are single-consumer and must be awaited only once.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    [AsyncMethodBuilder(typeof(OnityTaskMethodBuilder<>))]
    public readonly struct OnityTask<T>
    {
        private readonly object m_state;
        private readonly T m_result;
        private readonly int m_token;

        /// <summary>
        /// Initializes a typed task wrapper.
        /// </summary>
        /// <param name="task">Wrapped task.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTask(Task<T> task)
        {
            m_state = task ?? throw new ArgumentNullException(nameof(task));
            m_result = default;
            m_token = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTask(IOnityTaskSource<T> source)
        {
            m_state = source ?? throw new ArgumentNullException(nameof(source));
            m_result = default;
            m_token = source.Version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTask(T result)
        {
            m_state = null;
            m_result = result;
            m_token = 0;
        }

        /// <summary>
        /// True when the wrapped task completed.
        /// </summary>
        public bool IsCompleted => m_state == null
            || (m_state is IOnityTaskSource<T> source
                ? source.GetStatus(m_token) != OnityTaskSourceStatus.Pending
                : ((Task<T>)m_state).IsCompleted);

        /// <summary>
        /// True when the wrapped task completed successfully.
        /// </summary>
        public bool IsCompletedSuccessfully =>
            m_state == null
            || (m_state is IOnityTaskSource<T> source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Succeeded
                : ((Task<T>)m_state).IsCompletedSuccessfully);

        /// <summary>
        /// True when the wrapped task is canceled.
        /// </summary>
        public bool IsCanceled => m_state != null
            && (m_state is IOnityTaskSource<T> source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Canceled
                : ((Task<T>)m_state).IsCanceled);

        /// <summary>
        /// True when the wrapped task is faulted.
        /// </summary>
        public bool IsFaulted => m_state != null
            && (m_state is IOnityTaskSource<T> source
                ? source.GetStatus(m_token) == OnityTaskSourceStatus.Faulted
                : ((Task<T>)m_state).IsFaulted);

        /// <summary>
        /// Wraps a typed task as an Onity task.
        /// </summary>
        /// <param name="task">Task to wrap.</param>
        /// <returns>Onity task.</returns>
        public static OnityTask<T> FromTask(Task<T> task)
        {
            return new OnityTask<T>(task);
        }

        /// <summary>
        /// Creates a typed task completed with a result.
        /// </summary>
        /// <param name="result">Result value.</param>
        /// <returns>Completed task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OnityTask<T> FromResult(T result)
        {
            return new OnityTask<T>(result);
        }

        /// <summary>
        /// Creates a canceled typed Onity task.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Canceled task.</returns>
        public static OnityTask<T> FromCanceled(CancellationToken cancellationToken)
        {
            return new OnityTask<T>(Task.FromCanceled<T>(cancellationToken));
        }

        /// <summary>
        /// Creates a faulted typed Onity task.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        /// <returns>Faulted task.</returns>
        public static OnityTask<T> FromException(Exception exception)
        {
            return new OnityTask<T>(Task.FromException<T>(exception));
        }

        /// <summary>
        /// Returns the wrapped task for interop.
        /// </summary>
        /// <returns>Wrapped task.</returns>
        public Task<T> AsTask()
        {
            if (m_state == null)
            {
                return EqualityComparer<T>.Default.Equals(m_result, default) ? DefaultTaskCache.Value : Task.FromResult(m_result);
            }

            return m_state is IOnityTaskSource<T> source ? source.AsTask(m_token) : (Task<T>)m_state;
        }

        /// <summary>
        /// Returns the task awaiter.
        /// </summary>
        /// <returns>Task awaiter.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public OnityTaskAwaiter<T> GetAwaiter()
        {
            return new OnityTaskAwaiter<T>(m_state, m_result, m_token);
        }

        /// <summary>
        /// Runs task without awaiting and routes exceptions to callback or Unity log.
        /// </summary>
        /// <param name="exceptionHandler">Optional exception callback.</param>
        public void Forget(Action<Exception> exceptionHandler = null)
        {
            AsTask().Forget(exceptionHandler);
        }

        private static class DefaultTaskCache
        {
            internal static readonly Task<T> Value = Task.FromResult(default(T));
        }
    }

    /// <summary>
    /// Awaiter for <see cref="OnityTask"/>.
    /// </summary>
    public readonly struct OnityTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly object m_state;
        private readonly int m_token;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTaskAwaiter(object state, int token)
        {
            m_state = state;
            m_token = token;
        }

        /// <summary>
        /// True when the awaited operation completed.
        /// </summary>
        public bool IsCompleted => m_state == null
            || (m_state is IOnityTaskSource source
                ? source.GetStatus(m_token) != OnityTaskSourceStatus.Pending
                : ((Task)m_state).IsCompleted);

        /// <summary>
        /// Completes the await and throws if the operation failed or was canceled.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetResult()
        {
            object state = m_state;
            if (state == null)
            {
                return;
            }

            GetResultSlow(state, m_token);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void GetResultSlow(object state, int token)
        {
            if (state is IOnityTaskSource source)
            {
                source.GetResult(token);
                return;
            }

            ((Task)state).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Registers a continuation.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void OnCompleted(Action continuation)
        {
            if (m_state == null)
            {
                continuation?.Invoke();
                return;
            }

            if (m_state is IOnityTaskSource source)
            {
                source.OnCompleted(continuation, m_token);
                return;
            }

            ((Task)m_state).GetAwaiter().OnCompleted(continuation);
        }

        /// <summary>
        /// Registers a continuation without flowing execution context.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }

    /// <summary>
    /// Awaiter for <see cref="OnityTask{T}"/>.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public readonly struct OnityTaskAwaiter<T> : ICriticalNotifyCompletion
    {
        private readonly object m_state;
        private readonly T m_result;
        private readonly int m_token;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OnityTaskAwaiter(
            object state,
            T result,
            int token)
        {
            m_state = state;
            m_result = result;
            m_token = token;
        }

        /// <summary>
        /// True when the awaited operation completed.
        /// </summary>
        public bool IsCompleted => m_state == null
            || (m_state is IOnityTaskSource<T> source
                ? source.GetStatus(m_token) != OnityTaskSourceStatus.Pending
                : ((Task<T>)m_state).IsCompleted);

        /// <summary>
        /// Completes the await and returns the result.
        /// </summary>
        /// <returns>Awaited result.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetResult()
        {
            object state = m_state;
            if (state == null)
            {
                return m_result;
            }

            return GetResultSlow(state, m_token);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T GetResultSlow(object state, int token)
        {
            if (state is IOnityTaskSource<T> source)
            {
                return source.GetResult(token);
            }

            return ((Task<T>)state).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Registers a continuation.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void OnCompleted(Action continuation)
        {
            if (m_state == null)
            {
                continuation?.Invoke();
                return;
            }

            if (m_state is IOnityTaskSource<T> source)
            {
                source.OnCompleted(continuation, m_token);
                return;
            }

            ((Task<T>)m_state).GetAwaiter().OnCompleted(continuation);
        }

        /// <summary>
        /// Registers a continuation without flowing execution context.
        /// </summary>
        /// <param name="continuation">Continuation callback.</param>
        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }
    }

    internal enum OnityTaskSourceStatus
    {
        Pending,
        Succeeded,
        Canceled,
        Faulted
    }

    internal enum OnityTaskLoopPhase
    {
        Update,
        FixedUpdate,
        LateUpdate
    }

    internal interface IOnityTaskSource
    {
        int Version { get; }

        OnityTaskSourceStatus GetStatus(int token);

        Task AsTask(int token);

        void OnCompleted(Action continuation, int token);

        void GetResult(int token);
    }

    internal interface IOnityTaskSource<T>
    {
        int Version { get; }

        OnityTaskSourceStatus GetStatus(int token);

        Task<T> AsTask(int token);

        void OnCompleted(Action continuation, int token);

        T GetResult(int token);
    }

    internal interface IOnityTaskTickSource
    {
        int Version { get; }

        bool IsCancellationRequested { get; }

        bool TrySetCanceledFromRunner(int token);

        bool Tick(float deltaTime, float unscaledDeltaTime);
    }

    internal abstract class OnityTaskSourceBase : IOnityTaskSource
    {
        private const int k_noConsumption = 0;
        private const int k_nativeConsumption = 1;
        private const int k_taskConsumption = 2;

        private static readonly Action<object> s_cancelCallback = CancelFromToken;

        private Action m_continuation;
        private TaskCompletionSource<bool> m_taskCompletionSource;
        private CancellationTokenRegistration m_cancellationRegistration;
        private CancellationToken m_cancellationToken;
        private Exception m_exception;
        private int m_status;
        private int m_version;
        private int m_consumptionMode;
        private int m_consumed;
        private int m_cancellationRequested;
        private int m_taskMaterialized;
        private int m_released;

        public int Version => Volatile.Read(ref m_version);

        public bool IsCancellationRequested => Volatile.Read(ref m_cancellationRequested) != 0;

        protected bool IsPending => Volatile.Read(ref m_status) == (int)OnityTaskSourceStatus.Pending;

        public OnityTaskSourceStatus GetStatus(int token)
        {
            int versionBefore = Volatile.Read(ref m_version);
            if (token != versionBefore)
            {
                ThrowInvalidToken();
            }

            OnityTaskSourceStatus status = (OnityTaskSourceStatus)Volatile.Read(ref m_status);
            if (token != Volatile.Read(ref m_version))
            {
                ThrowInvalidToken();
            }

            return status;
        }

        public Task AsTask(int token)
        {
            Task task;
            bool releaseSource;

            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_released != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been materialized and released.");
                }

                if (m_consumptionMode == k_nativeConsumption)
                {
                    throw new InvalidOperationException(
                        "OnityTask is already being consumed by its native awaiter.");
                }

                m_consumptionMode = k_taskConsumption;
                Volatile.Write(ref m_taskMaterialized, 1);

                if (m_taskCompletionSource == null)
                {
                    m_taskCompletionSource =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ApplyStatusToTask(m_taskCompletionSource);
                }

                task = m_taskCompletionSource.Task;
                releaseSource = m_status != (int)OnityTaskSourceStatus.Pending
                    && TryClaimTaskReleaseUnsafe();

                if (releaseSource)
                {
                    ClearCompletionReferencesUnsafe();
                }
            }

            if (releaseSource)
            {
                ReleaseSource();
            }

            return task;
        }

        public void OnCompleted(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            bool invokeNow;
            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_consumptionMode != k_noConsumption)
                {
                    throw new InvalidOperationException("OnityTask supports only one native awaiter.");
                }

                m_consumptionMode = k_nativeConsumption;
                invokeNow = m_status != (int)OnityTaskSourceStatus.Pending;
                if (invokeNow == false)
                {
                    m_continuation = continuation;
                }
            }

            if (invokeNow)
            {
                continuation();
            }
        }

        public void GetResult(int token)
        {
            Exception exception;

            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_consumptionMode == k_taskConsumption)
                {
                    throw new InvalidOperationException(
                        "OnityTask is already being consumed through AsTask().");
                }

                if (m_status == (int)OnityTaskSourceStatus.Pending)
                {
                    throw new InvalidOperationException("OnityTask is not completed.");
                }

                m_consumptionMode = k_nativeConsumption;
                m_consumed = 1;
                exception = GetCompletionExceptionUnsafe();
                ClearCompletionReferencesUnsafe();
            }

            ReleaseAfterResult();

            if (exception != null)
            {
                throw exception;
            }
        }

        public bool TrySetCanceledFromRunner(int token)
        {
            return token == Volatile.Read(ref m_version)
                && IsCancellationRequested
                && TrySetStatus(OnityTaskSourceStatus.Canceled, null);
        }

        protected void Reset(CancellationToken cancellationToken)
        {
            lock (this)
            {
                m_continuation = null;
                m_taskCompletionSource = null;
                m_cancellationToken = cancellationToken;
                m_exception = null;
                int nextVersion = unchecked(m_version + 1);
                Volatile.Write(ref m_version, nextVersion == 0 ? 1 : nextVersion);
                Volatile.Write(ref m_status, (int)OnityTaskSourceStatus.Pending);
                m_consumptionMode = k_noConsumption;
                m_consumed = 0;
                Volatile.Write(ref m_cancellationRequested, 0);
                Volatile.Write(ref m_taskMaterialized, 0);
                Volatile.Write(ref m_released, 0);
            }

            if (cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration = cancellationToken.Register(s_cancelCallback, this);
            }
            else
            {
                m_cancellationRegistration = default;
            }
        }

        protected bool TrySetResult()
        {
            return TrySetStatus(OnityTaskSourceStatus.Succeeded, null);
        }

        protected bool TrySetException(Exception exception)
        {
            return TrySetStatus(
                OnityTaskSourceStatus.Faulted,
                exception ?? new InvalidOperationException("OnityTask failed."));
        }

        protected bool TrySetCanceled()
        {
            return TrySetStatus(OnityTaskSourceStatus.Canceled, null);
        }

        protected abstract void ReleaseSource();

        private static void CancelFromToken(object state)
        {
            OnityTaskSourceBase source = (OnityTaskSourceBase)state;
            Volatile.Write(ref source.m_cancellationRequested, 1);
            OnityTaskRunner.NotifyCancellationRequested();
        }

        private bool TrySetStatus(OnityTaskSourceStatus status, Exception exception)
        {
            Action continuation;
            TaskCompletionSource<bool> taskCompletionSource;
            bool releaseSource;

            lock (this)
            {
                if (m_status != (int)OnityTaskSourceStatus.Pending)
                {
                    return false;
                }

                m_exception = exception;
                continuation = m_continuation;
                taskCompletionSource = m_taskCompletionSource;
                m_continuation = null;
                m_cancellationRegistration.Dispose();
                m_cancellationRegistration = default;
                Volatile.Write(ref m_status, (int)status);
                releaseSource = TryClaimTaskReleaseUnsafe();
            }

            ApplyStatusToTask(taskCompletionSource);
            if (releaseSource)
            {
                lock (this)
                {
                    ClearCompletionReferencesUnsafe();
                }

                ReleaseSource();
            }

            continuation?.Invoke();
            return true;
        }

        private void ApplyStatusToTask(TaskCompletionSource<bool> taskCompletionSource)
        {
            int status = Volatile.Read(ref m_status);
            if (taskCompletionSource == null || status == (int)OnityTaskSourceStatus.Pending)
            {
                return;
            }

            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                taskCompletionSource.TrySetResult(true);
            }
            else if (status == (int)OnityTaskSourceStatus.Canceled)
            {
                taskCompletionSource.TrySetCanceled(m_cancellationToken);
            }
            else
            {
                taskCompletionSource.TrySetException(m_exception);
            }
        }

        private Exception GetCompletionExceptionUnsafe()
        {
            int status = m_status;
            if (status == (int)OnityTaskSourceStatus.Canceled)
            {
                return new OperationCanceledException(m_cancellationToken);
            }

            return status == (int)OnityTaskSourceStatus.Faulted ? m_exception : null;
        }

        private void ValidateToken(int token)
        {
            if (token != m_version)
            {
                ThrowInvalidToken();
            }
        }

        private static void ThrowInvalidToken()
        {
            throw new InvalidOperationException(
                "The OnityTask source is no longer valid. Pooled OnityTask instances can be awaited only once.");
        }

        private void ReleaseAfterResult()
        {
            if (Volatile.Read(ref m_taskMaterialized) == 0
                && Interlocked.Exchange(ref m_released, 1) == 0)
            {
                ReleaseSource();
            }
        }

        private bool TryClaimTaskReleaseUnsafe()
        {
            if (m_taskMaterialized == 0 || m_released != 0)
            {
                return false;
            }

            m_released = 1;
            return true;
        }

        private void ClearCompletionReferencesUnsafe()
        {
            m_taskCompletionSource = null;
            m_cancellationRegistration = default;
            m_cancellationToken = default;
            m_exception = null;
        }
    }

    internal abstract class OnityTaskSourceBase<T> : IOnityTaskSource<T>
    {
        private const int k_noConsumption = 0;
        private const int k_nativeConsumption = 1;
        private const int k_taskConsumption = 2;

        private static readonly Action<object> s_cancelCallback = CancelFromToken;

        private Action m_continuation;
        private TaskCompletionSource<T> m_taskCompletionSource;
        private CancellationTokenRegistration m_cancellationRegistration;
        private CancellationToken m_cancellationToken;
        private Exception m_exception;
        private int m_status;
        private T m_result;
        private int m_version;
        private int m_consumptionMode;
        private int m_consumed;
        private int m_cancellationRequested;
        private int m_taskMaterialized;
        private int m_released;

        public int Version => Volatile.Read(ref m_version);

        public bool IsCancellationRequested => Volatile.Read(ref m_cancellationRequested) != 0;

        protected bool IsPending => Volatile.Read(ref m_status) == (int)OnityTaskSourceStatus.Pending;

        public OnityTaskSourceStatus GetStatus(int token)
        {
            int versionBefore = Volatile.Read(ref m_version);
            if (token != versionBefore)
            {
                ThrowInvalidToken();
            }

            OnityTaskSourceStatus status = (OnityTaskSourceStatus)Volatile.Read(ref m_status);
            if (token != Volatile.Read(ref m_version))
            {
                ThrowInvalidToken();
            }

            return status;
        }

        public Task<T> AsTask(int token)
        {
            Task<T> task;
            bool releaseSource;

            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_released != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been materialized and released.");
                }

                if (m_consumptionMode == k_nativeConsumption)
                {
                    throw new InvalidOperationException(
                        "OnityTask is already being consumed by its native awaiter.");
                }

                m_consumptionMode = k_taskConsumption;
                Volatile.Write(ref m_taskMaterialized, 1);

                if (m_taskCompletionSource == null)
                {
                    m_taskCompletionSource =
                        new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ApplyStatusToTask(m_taskCompletionSource);
                }

                task = m_taskCompletionSource.Task;
                releaseSource = m_status != (int)OnityTaskSourceStatus.Pending
                    && TryClaimTaskReleaseUnsafe();

                if (releaseSource)
                {
                    ClearCompletionReferencesUnsafe();
                }
            }

            if (releaseSource)
            {
                ReleaseSource();
            }

            return task;
        }

        public void OnCompleted(Action continuation, int token)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException(nameof(continuation));
            }

            bool invokeNow;
            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_consumptionMode != k_noConsumption)
                {
                    throw new InvalidOperationException("OnityTask supports only one native awaiter.");
                }

                m_consumptionMode = k_nativeConsumption;
                invokeNow = m_status != (int)OnityTaskSourceStatus.Pending;
                if (invokeNow == false)
                {
                    m_continuation = continuation;
                }
            }

            if (invokeNow)
            {
                continuation();
            }
        }

        public T GetResult(int token)
        {
            Exception exception;
            T result;

            lock (this)
            {
                ValidateToken(token);

                if (m_consumed != 0)
                {
                    throw new InvalidOperationException("OnityTask has already been consumed.");
                }

                if (m_consumptionMode == k_taskConsumption)
                {
                    throw new InvalidOperationException(
                        "OnityTask is already being consumed through AsTask().");
                }

                int status = m_status;
                if (status == (int)OnityTaskSourceStatus.Pending)
                {
                    throw new InvalidOperationException("OnityTask is not completed.");
                }

                m_consumptionMode = k_nativeConsumption;
                m_consumed = 1;
                exception = status == (int)OnityTaskSourceStatus.Canceled
                    ? new OperationCanceledException(m_cancellationToken)
                    : status == (int)OnityTaskSourceStatus.Faulted
                        ? m_exception
                        : null;
                result = m_result;
                ClearCompletionReferencesUnsafe();
            }

            ReleaseAfterResult();

            if (exception != null)
            {
                throw exception;
            }

            return result;
        }

        public bool TrySetCanceledFromRunner(int token)
        {
            return token == Volatile.Read(ref m_version)
                && IsCancellationRequested
                && TrySetStatus(OnityTaskSourceStatus.Canceled, default, null);
        }

        protected void Reset(CancellationToken cancellationToken)
        {
            lock (this)
            {
                m_continuation = null;
                m_taskCompletionSource = null;
                m_cancellationToken = cancellationToken;
                m_exception = null;
                m_result = default;
                int nextVersion = unchecked(m_version + 1);
                Volatile.Write(ref m_version, nextVersion == 0 ? 1 : nextVersion);
                Volatile.Write(ref m_status, (int)OnityTaskSourceStatus.Pending);
                m_consumptionMode = k_noConsumption;
                m_consumed = 0;
                Volatile.Write(ref m_cancellationRequested, 0);
                Volatile.Write(ref m_taskMaterialized, 0);
                Volatile.Write(ref m_released, 0);
            }

            if (cancellationToken.CanBeCanceled)
            {
                m_cancellationRegistration = cancellationToken.Register(s_cancelCallback, this);
            }
            else
            {
                m_cancellationRegistration = default;
            }
        }

        protected bool TrySetResult(T result)
        {
            return TrySetStatus(OnityTaskSourceStatus.Succeeded, result, null);
        }

        protected bool TrySetException(Exception exception)
        {
            return TrySetStatus(
                OnityTaskSourceStatus.Faulted,
                default,
                exception ?? new InvalidOperationException("OnityTask failed."));
        }

        protected bool TrySetCanceled()
        {
            return TrySetStatus(OnityTaskSourceStatus.Canceled, default, null);
        }

        protected abstract void ReleaseSource();

        private static void CancelFromToken(object state)
        {
            OnityTaskSourceBase<T> source = (OnityTaskSourceBase<T>)state;
            Volatile.Write(ref source.m_cancellationRequested, 1);
            OnityTaskRunner.NotifyCancellationRequested();
        }

        private bool TrySetStatus(OnityTaskSourceStatus status, T result, Exception exception)
        {
            Action continuation;
            TaskCompletionSource<T> taskCompletionSource;
            bool releaseSource;

            lock (this)
            {
                if (m_status != (int)OnityTaskSourceStatus.Pending)
                {
                    return false;
                }

                m_result = result;
                m_exception = exception;
                continuation = m_continuation;
                taskCompletionSource = m_taskCompletionSource;
                m_continuation = null;
                m_cancellationRegistration.Dispose();
                m_cancellationRegistration = default;
                Volatile.Write(ref m_status, (int)status);
                releaseSource = TryClaimTaskReleaseUnsafe();
            }

            ApplyStatusToTask(taskCompletionSource);
            if (releaseSource)
            {
                lock (this)
                {
                    ClearCompletionReferencesUnsafe();
                }

                ReleaseSource();
            }

            continuation?.Invoke();
            return true;
        }

        private void ApplyStatusToTask(TaskCompletionSource<T> taskCompletionSource)
        {
            int status = Volatile.Read(ref m_status);
            if (taskCompletionSource == null || status == (int)OnityTaskSourceStatus.Pending)
            {
                return;
            }

            if (status == (int)OnityTaskSourceStatus.Succeeded)
            {
                taskCompletionSource.TrySetResult(m_result);
            }
            else if (status == (int)OnityTaskSourceStatus.Canceled)
            {
                taskCompletionSource.TrySetCanceled(m_cancellationToken);
            }
            else
            {
                taskCompletionSource.TrySetException(m_exception);
            }
        }

        private void ValidateToken(int token)
        {
            if (token != m_version)
            {
                ThrowInvalidToken();
            }
        }

        private static void ThrowInvalidToken()
        {
            throw new InvalidOperationException(
                "The OnityTask source is no longer valid. Pooled OnityTask instances can be awaited only once.");
        }

        private void ReleaseAfterResult()
        {
            if (Volatile.Read(ref m_taskMaterialized) == 0
                && Interlocked.Exchange(ref m_released, 1) == 0)
            {
                ReleaseSource();
            }
        }

        private bool TryClaimTaskReleaseUnsafe()
        {
            if (m_taskMaterialized == 0 || m_released != 0)
            {
                return false;
            }

            m_released = 1;
            return true;
        }

        private void ClearCompletionReferencesUnsafe()
        {
            m_taskCompletionSource = null;
            m_cancellationRegistration = default;
            m_cancellationToken = default;
            m_exception = null;
            m_result = default;
        }
    }

    [ExecuteAlways]
    internal sealed class OnityTaskRunner : MonoBehaviour
    {
        private static OnityTaskRunner s_instance;
        private static int s_cancellationRequestCount;

        private readonly List<IOnityTaskTickSource> m_updateSources = new List<IOnityTaskTickSource>(64);
        private readonly List<IOnityTaskTickSource> m_fixedUpdateSources = new List<IOnityTaskTickSource>(16);
        private readonly List<IOnityTaskTickSource> m_lateUpdateSources = new List<IOnityTaskTickSource>(16);

        public static void Schedule(IOnityTaskTickSource source, OnityTaskLoopPhase phase)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            OnityTaskRunner runner = GetOrCreate();
            if (phase == OnityTaskLoopPhase.FixedUpdate)
            {
                runner.m_fixedUpdateSources.Add(source);
            }
            else if (phase == OnityTaskLoopPhase.LateUpdate)
            {
                runner.m_lateUpdateSources.Add(source);
            }
            else
            {
                runner.m_updateSources.Add(source);
            }
        }

        public static void NotifyCancellationRequested()
        {
            Interlocked.Increment(ref s_cancellationRequestCount);
        }

        private static OnityTaskRunner GetOrCreate()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            GameObject gameObject = new GameObject("OnityTaskRunner");
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            s_instance = gameObject.AddComponent<OnityTaskRunner>();
            return s_instance;
        }

        private void Update()
        {
            DrainCancellationRequests();
            TickSources(m_updateSources, Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            TickSources(m_fixedUpdateSources, Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        }

        private void LateUpdate()
        {
            TickSources(m_lateUpdateSources, Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
            }
        }

        private static void TickSources(
            List<IOnityTaskTickSource> sources,
            float deltaTime,
            float unscaledDeltaTime)
        {
            int count = sources.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                IOnityTaskTickSource source = sources[i];
                if (source.IsCancellationRequested)
                {
                    int version = source.Version;
                    RemoveAtSwapBack(sources, i);
                    source.TrySetCanceledFromRunner(version);
                    continue;
                }

                if (source.Tick(deltaTime, unscaledDeltaTime))
                {
                    RemoveAtSwapBack(sources, i);
                }
            }
        }

        private void DrainCancellationRequests()
        {
            if (Interlocked.Exchange(ref s_cancellationRequestCount, 0) == 0)
            {
                return;
            }

            CancelRequestedSources(m_updateSources);
            CancelRequestedSources(m_fixedUpdateSources);
            CancelRequestedSources(m_lateUpdateSources);
        }

        private static void CancelRequestedSources(List<IOnityTaskTickSource> sources)
        {
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                IOnityTaskTickSource source = sources[i];
                if (source.IsCancellationRequested == false)
                {
                    continue;
                }

                int version = source.Version;
                RemoveAtSwapBack(sources, i);
                source.TrySetCanceledFromRunner(version);
            }
        }

        private static void RemoveAtSwapBack(List<IOnityTaskTickSource> sources, int index)
        {
            int lastIndex = sources.Count - 1;
            sources[index] = sources[lastIndex];
            sources.RemoveAt(lastIndex);
        }

    }

    internal sealed class OnityFrameTaskSource : OnityTaskSourceBase, IOnityTaskTickSource
    {
        private const int k_maxPoolSize = 256;

        private static readonly Stack<OnityFrameTaskSource> s_pool = new Stack<OnityFrameTaskSource>(32);

        public static OnityFrameTaskSource Rent(
            OnityTaskLoopPhase phase,
            CancellationToken cancellationToken)
        {
            OnityFrameTaskSource source;
            lock (s_pool)
            {
                source = s_pool.Count > 0 ? s_pool.Pop() : new OnityFrameTaskSource();
            }

            source.Reset(cancellationToken);
            OnityTaskRunner.Schedule(source, phase);
            return source;
        }

        public bool Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (IsPending == false)
            {
                return true;
            }

            TrySetResult();
            return true;
        }

        protected override void ReleaseSource()
        {
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }
    }

    internal sealed class OnityDelayTaskSource : OnityTaskSourceBase, IOnityTaskTickSource
    {
        private const int k_maxPoolSize = 256;

        private static readonly Stack<OnityDelayTaskSource> s_pool = new Stack<OnityDelayTaskSource>(32);

        private float m_remainingSeconds;
        private bool m_useUnscaledTime;

        public static OnityDelayTaskSource Rent(
            float delaySeconds,
            bool useUnscaledTime,
            CancellationToken cancellationToken)
        {
            OnityDelayTaskSource source;
            lock (s_pool)
            {
                source = s_pool.Count > 0 ? s_pool.Pop() : new OnityDelayTaskSource();
            }

            source.Reset(cancellationToken);
            source.m_remainingSeconds = delaySeconds;
            source.m_useUnscaledTime = useUnscaledTime;
            OnityTaskRunner.Schedule(source, OnityTaskLoopPhase.Update);
            return source;
        }

        public bool Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (IsPending == false)
            {
                return true;
            }

            m_remainingSeconds -= m_useUnscaledTime ? unscaledDeltaTime : deltaTime;
            if (m_remainingSeconds > 0f)
            {
                return false;
            }

            TrySetResult();
            return true;
        }

        protected override void ReleaseSource()
        {
            m_remainingSeconds = 0f;
            m_useUnscaledTime = false;
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }
    }

    internal sealed class OnityPredicateTaskSource : OnityTaskSourceBase, IOnityTaskTickSource
    {
        private const int k_maxPoolSize = 256;

        private static readonly Stack<OnityPredicateTaskSource> s_pool = new Stack<OnityPredicateTaskSource>(16);

        private Func<bool> m_predicate;
        private bool m_waitWhile;

        public static OnityPredicateTaskSource Rent(
            Func<bool> predicate,
            bool waitWhile,
            CancellationToken cancellationToken)
        {
            OnityPredicateTaskSource source;
            lock (s_pool)
            {
                source = s_pool.Count > 0 ? s_pool.Pop() : new OnityPredicateTaskSource();
            }

            source.Reset(cancellationToken);
            source.m_predicate = predicate;
            source.m_waitWhile = waitWhile;
            OnityTaskRunner.Schedule(source, OnityTaskLoopPhase.Update);
            return source;
        }

        public bool Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (IsPending == false)
            {
                return true;
            }

            try
            {
                bool value = m_predicate();
                bool shouldComplete = m_waitWhile ? value == false : value;
                if (shouldComplete == false)
                {
                    return false;
                }

                TrySetResult();
                return true;
            }
            catch (Exception exception)
            {
                TrySetException(exception);
                return true;
            }
        }

        protected override void ReleaseSource()
        {
            m_predicate = null;
            m_waitWhile = false;
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }
    }

    internal sealed class OnityAsyncOperationTaskSource<TAsyncOperation> :
        OnityTaskSourceBase<TAsyncOperation>,
        IOnityTaskTickSource
        where TAsyncOperation : AsyncOperation
    {
        private const int k_maxPoolSize = 256;

        private static readonly Stack<OnityAsyncOperationTaskSource<TAsyncOperation>> s_pool =
            new Stack<OnityAsyncOperationTaskSource<TAsyncOperation>>(16);

        private TAsyncOperation m_operation;
        private Action<float> m_onProgress;

        public static OnityAsyncOperationTaskSource<TAsyncOperation> Rent(
            TAsyncOperation operation,
            Action<float> onProgress,
            CancellationToken cancellationToken)
        {
            OnityAsyncOperationTaskSource<TAsyncOperation> source;
            lock (s_pool)
            {
                source = s_pool.Count > 0
                    ? s_pool.Pop()
                    : new OnityAsyncOperationTaskSource<TAsyncOperation>();
            }

            source.Reset(cancellationToken);
            source.m_operation = operation;
            source.m_onProgress = onProgress;
            OnityTaskRunner.Schedule(source, OnityTaskLoopPhase.Update);
            return source;
        }

        public bool Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (IsPending == false)
            {
                return true;
            }

            try
            {
                TAsyncOperation operation = m_operation;
                m_onProgress?.Invoke(Mathf.Clamp01(operation.progress));

                if (operation.isDone == false)
                {
                    return false;
                }

                m_onProgress?.Invoke(1f);
                TrySetResult(operation);
                return true;
            }
            catch (Exception exception)
            {
                TrySetException(exception);
                return true;
            }
        }

        protected override void ReleaseSource()
        {
            m_operation = null;
            m_onProgress = null;
            lock (s_pool)
            {
                if (s_pool.Count < k_maxPoolSize)
                {
                    s_pool.Push(this);
                }
            }
        }
    }

    /// <summary>
    /// Async method builder for async methods returning <see cref="OnityTask"/>.
    /// </summary>
    public struct OnityTaskMethodBuilder
    {
        private AsyncTaskMethodBuilder m_builder;

        /// <summary>
        /// Creates a method builder.
        /// </summary>
        /// <returns>Created builder.</returns>
        public static OnityTaskMethodBuilder Create()
        {
            return new OnityTaskMethodBuilder
            {
                m_builder = AsyncTaskMethodBuilder.Create()
            };
        }

        /// <summary>
        /// Gets the task controlled by this builder.
        /// </summary>
        public OnityTask Task => OnityTask.FromTask(m_builder.Task);

        /// <summary>
        /// Starts the async state machine.
        /// </summary>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="stateMachine">State machine.</param>
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.Start(ref stateMachine);
        }

        /// <summary>
        /// Sets the async state machine.
        /// </summary>
        /// <param name="stateMachine">State machine.</param>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            m_builder.SetStateMachine(stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a safe awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a critical awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Completes the async method successfully.
        /// </summary>
        public void SetResult()
        {
            m_builder.SetResult();
        }

        /// <summary>
        /// Completes the async method with an exception.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        public void SetException(Exception exception)
        {
            m_builder.SetException(exception);
        }
    }

    /// <summary>
    /// Async method builder for async methods returning <see cref="OnityTask{T}"/>.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    public struct OnityTaskMethodBuilder<T>
    {
        private AsyncTaskMethodBuilder<T> m_builder;

        /// <summary>
        /// Creates a typed method builder.
        /// </summary>
        /// <returns>Created builder.</returns>
        public static OnityTaskMethodBuilder<T> Create()
        {
            return new OnityTaskMethodBuilder<T>
            {
                m_builder = AsyncTaskMethodBuilder<T>.Create()
            };
        }

        /// <summary>
        /// Gets the task controlled by this builder.
        /// </summary>
        public OnityTask<T> Task => OnityTask<T>.FromTask(m_builder.Task);

        /// <summary>
        /// Starts the async state machine.
        /// </summary>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="stateMachine">State machine.</param>
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.Start(ref stateMachine);
        }

        /// <summary>
        /// Sets the async state machine.
        /// </summary>
        /// <param name="stateMachine">State machine.</param>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            m_builder.SetStateMachine(stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a safe awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Schedules a continuation for a critical awaiter.
        /// </summary>
        /// <typeparam name="TAwaiter">Awaiter type.</typeparam>
        /// <typeparam name="TStateMachine">State machine type.</typeparam>
        /// <param name="awaiter">Awaiter.</param>
        /// <param name="stateMachine">State machine.</param>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(
            ref TAwaiter awaiter,
            ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            m_builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Completes the async method successfully.
        /// </summary>
        /// <param name="result">Async method result.</param>
        public void SetResult(T result)
        {
            m_builder.SetResult(result);
        }

        /// <summary>
        /// Completes the async method with an exception.
        /// </summary>
        /// <param name="exception">Failure exception.</param>
        public void SetException(Exception exception)
        {
            m_builder.SetException(exception);
        }
    }

    /// <summary>
    /// Exception raised when an Onity Unity web request fails.
    /// </summary>
    public sealed class OnityUnityWebRequestException : Exception
    {
        /// <summary>
        /// Initializes a request failure exception.
        /// </summary>
        /// <param name="request">Failed request.</param>
        public OnityUnityWebRequestException(UnityWebRequest request)
            : base(CreateMessage(request))
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Url = request.url ?? string.Empty;
            ResponseCode = request.responseCode;
            Result = request.result;
            RequestError = request.error ?? string.Empty;
        }

        /// <summary>
        /// Request URL.
        /// </summary>
        public string Url { get; }

        /// <summary>
        /// HTTP response code.
        /// </summary>
        public long ResponseCode { get; }

        /// <summary>
        /// Unity request result.
        /// </summary>
        public UnityWebRequest.Result Result { get; }

        /// <summary>
        /// Unity request error string.
        /// </summary>
        public string RequestError { get; }

        private static string CreateMessage(UnityWebRequest request)
        {
            if (request == null)
            {
                return "UnityWebRequest failed.";
            }

            return $"UnityWebRequest failed ({request.result}, {request.responseCode}) for {request.url}: {request.error}";
        }
    }

    /// <summary>
    /// Lightweight async helpers inspired by common Unity task workflows.
    /// </summary>
    public static class OnityAsync
    {
        /// <summary>
        /// Awaits one rendered frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task NextFrameAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.NextFrameAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.EveryUpdate().ToTask(cancellationToken),
                "OnityAsync.NextFrameAsync");
        }

        /// <summary>
        /// Awaits one fixed update frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task NextFixedFrameAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.NextFixedFrameAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.EveryFixedUpdate().ToTask(cancellationToken),
                "OnityAsync.NextFixedFrameAsync");
        }

        /// <summary>
        /// Awaits one late update frame.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task NextLateFrameAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.NextLateFrameAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.EveryLateUpdate().ToTask(cancellationToken),
                "OnityAsync.NextLateFrameAsync");
        }

        /// <summary>
        /// Awaits a delay in seconds.
        /// </summary>
        /// <param name="delaySeconds">Delay duration in seconds.</param>
        /// <param name="useUnscaledTime">Use unscaled time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task DelayAsync(
            float delaySeconds,
            bool useUnscaledTime = false,
            CancellationToken cancellationToken = default)
        {
            if (delaySeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(delaySeconds));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.DelayAsync");
            }

            if (delaySeconds <= 0f)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.DelayAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable.Timer(delaySeconds, useUnscaledTime).ToTask(cancellationToken),
                "OnityAsync.DelayAsync");
        }

        /// <summary>
        /// Awaits a delay using a time provider.
        /// </summary>
        /// <param name="delay">Delay duration.</param>
        /// <param name="timeProvider">Optional time provider.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task DelayAsync(
            TimeSpan delay,
            OnityTimeProvider timeProvider = null,
            CancellationToken cancellationToken = default)
        {
            if (delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.DelayAsync(TimeSpan)");
            }

            if (delay == TimeSpan.Zero)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.DelayAsync(TimeSpan)");
            }

            OnityTimeProvider resolvedTimeProvider = timeProvider ?? OnityTimeProvider.System;

            return OnityTaskTracker.Track(
                resolvedTimeProvider.DelayAsync(delay, cancellationToken),
                "OnityAsync.DelayAsync(TimeSpan)");
        }

        /// <summary>
        /// Awaits until predicate returns true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task WaitUntilAsync(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.WaitUntilAsync");
            }

            if (predicate())
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.WaitUntilAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable
                    .EveryUpdate(cancellationToken)
                    .Where(_ => predicate())
                    .ToTask(cancellationToken),
                "OnityAsync.WaitUntilAsync");
        }

        /// <summary>
        /// Awaits while predicate remains true.
        /// </summary>
        /// <param name="predicate">Predicate callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task WaitWhileAsync(
            Func<bool> predicate,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTaskTracker.Track(
                    Task.FromCanceled(cancellationToken),
                    "OnityAsync.WaitWhileAsync");
            }

            if (predicate() == false)
            {
                return OnityTaskTracker.Track(
                    Task.CompletedTask,
                    "OnityAsync.WaitWhileAsync");
            }

            return OnityTaskTracker.Track(
                OnityUnityObservable
                    .EveryUpdate(cancellationToken)
                    .Where(_ => predicate() == false)
                    .ToTask(cancellationToken),
                "OnityAsync.WaitWhileAsync");
        }

        /// <summary>
        /// Returns a completed task for Unit payload streams.
        /// </summary>
        /// <param name="observable">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static Task AwaitObservable(
            IOnityObservable<Unit> observable,
            CancellationToken cancellationToken = default)
        {
            if (observable == null)
            {
                throw new ArgumentNullException(nameof(observable));
            }

            return OnityTaskTracker.Track(
                observable.ToTask(cancellationToken),
                "OnityAsync.AwaitObservable");
        }

        /// <summary>
        /// Awaits completion of all tasks.
        /// </summary>
        /// <param name="tasks">Task list.</param>
        /// <returns>Completion task.</returns>
        public static Task WhenAll(params Task[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAll(tasks),
                "OnityAsync.WhenAll");
        }

        /// <summary>
        /// Awaits completion of all typed tasks.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="tasks">Task list.</param>
        /// <returns>Task array result.</returns>
        public static Task<T[]> WhenAll<T>(params Task<T>[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAll(tasks),
                "OnityAsync.WhenAll<T>");
        }

        /// <summary>
        /// Awaits the first completed task from list.
        /// </summary>
        /// <param name="tasks">Task list.</param>
        /// <returns>Winner task.</returns>
        public static Task<Task> WhenAny(params Task[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAny(tasks),
                "OnityAsync.WhenAny");
        }

        /// <summary>
        /// Awaits the first completed typed task from list.
        /// </summary>
        /// <typeparam name="T">Task result type.</typeparam>
        /// <param name="tasks">Task list.</param>
        /// <returns>Winner task.</returns>
        public static Task<Task<T>> WhenAny<T>(params Task<T>[] tasks)
        {
            if (tasks == null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            return OnityTaskTracker.Track(
                Task.WhenAny(tasks),
                "OnityAsync.WhenAny<T>");
        }
    }

    /// <summary>
    /// OnityTask bridge methods for Unity async operations and observables.
    /// </summary>
    public static class OnityTaskBridgeExtensions
    {
        /// <summary>
        /// Converts a Unity async operation into an OnityTask.
        /// </summary>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>OnityTask completed by the Unity operation.</returns>
        public static OnityTask<TAsyncOperation> AsOnityTask<TAsyncOperation>(
            this TAsyncOperation operation,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
            where TAsyncOperation : AsyncOperation
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return OnityTask<TAsyncOperation>.FromCanceled(cancellationToken);
            }

            if (operation.isDone)
            {
                onProgress?.Invoke(1f);
                return OnityTask<TAsyncOperation>.FromResult(operation);
            }

            return new OnityTask<TAsyncOperation>(
                OnityAsyncOperationTaskSource<TAsyncOperation>.Rent(
                    operation,
                    onProgress,
                    cancellationToken));
        }

        /// <summary>
        /// Converts a Unity async operation into a cancelable OnityTask.
        /// </summary>
        /// <typeparam name="TAsyncOperation">Async operation type.</typeparam>
        /// <param name="operation">Target operation.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        /// <returns>OnityTask completed by the Unity operation.</returns>
        public static OnityTask<TAsyncOperation> WithOnityCancellation<TAsyncOperation>(
            this TAsyncOperation operation,
            CancellationToken cancellationToken,
            Action<float> onProgress = null)
            where TAsyncOperation : AsyncOperation
        {
            return operation.AsOnityTask(onProgress, cancellationToken);
        }

        /// <summary>
        /// Returns an OnityTask completed by the first source value.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>OnityTask completed by first source value.</returns>
        public static OnityTask<T> FirstOnityTask<T>(
            this IOnityObservable<T> source,
            CancellationToken cancellationToken = default)
        {
            return OnityTask<T>.FromTask(source.FirstAsync(cancellationToken));
        }

        /// <summary>
        /// Returns an OnityTask that completes when the source emits one Unit value.
        /// </summary>
        /// <param name="source">Source stream.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Completion task.</returns>
        public static OnityTask ToOnityTask(
            this IOnityObservable<Unit> source,
            CancellationToken cancellationToken = default)
        {
            return OnityTask.FromTask(source.ToTask(cancellationToken));
        }
    }

    /// <summary>
    /// OnityTask bridge methods for awaitable message channels.
    /// </summary>
    public static class OnityAsyncMessagingExtensions
    {
        /// <summary>
        /// Publishes an async message and exposes delivery as an OnityTask.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="publisher">Async publisher.</param>
        /// <param name="message">Message value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task completed when all async subscribers finish.</returns>
        public static OnityTask PublishOnityTask<TMessage>(
            this IAsyncPublisher<TMessage> publisher,
            TMessage message,
            CancellationToken cancellationToken = default)
        {
            if (publisher == null)
            {
                throw new ArgumentNullException(nameof(publisher));
            }

            return OnityTask.FromTask(
                PublishAsyncInternal(publisher, message, cancellationToken));
        }

        /// <summary>
        /// Subscribes an OnityTask-returning handler to an async message channel.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="subscriber">Async subscriber.</param>
        /// <param name="handler">OnityTask-returning handler.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable SubscribeOnityTask<TMessage>(
            this IAsyncSubscriber<TMessage> subscriber,
            Func<TMessage, OnityTask> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return SubscribeOnityTask(
                subscriber,
                (message, _) => handler(message));
        }

        /// <summary>
        /// Subscribes a cancellation-aware OnityTask-returning handler to an async message channel.
        /// </summary>
        /// <typeparam name="TMessage">Message type.</typeparam>
        /// <param name="subscriber">Async subscriber.</param>
        /// <param name="handler">OnityTask-returning handler.</param>
        /// <returns>Disposable subscription token.</returns>
        public static IDisposable SubscribeOnityTask<TMessage>(
            this IAsyncSubscriber<TMessage> subscriber,
            Func<TMessage, CancellationToken, OnityTask> handler)
        {
            if (subscriber == null)
            {
                throw new ArgumentNullException(nameof(subscriber));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return subscriber.Subscribe(
                async (message, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await handler(message, cancellationToken);
                });
        }

        private static async Task PublishAsyncInternal<TMessage>(
            IAsyncPublisher<TMessage> publisher,
            TMessage message,
            CancellationToken cancellationToken)
        {
            ValueTask publishTask = publisher.PublishAsync(message, cancellationToken);

            if (publishTask.IsCompletedSuccessfully)
            {
                publishTask.GetAwaiter().GetResult();
                return;
            }

            await publishTask.AsTask();
        }
    }
}
