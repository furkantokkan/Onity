using System;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// How an <see cref="IOnitySceneService" /> request ended.
    /// </summary>
    public enum OnitySceneResultStatus
    {
        /// <summary>
        /// The target scene loaded (or unloaded), its <c>SceneContext</c> finished building, its cover lifted and
        /// the scenes it replaced unloaded.
        /// </summary>
        Succeeded = 0,

        /// <summary>
        /// Another operation was running; nothing started.
        /// </summary>
        Busy = 1,

        /// <summary>
        /// The request names no loadable scene, a name matches several scenes, or the request combines options that
        /// cannot work together; nothing started.
        /// </summary>
        InvalidTarget = 2,

        /// <summary>
        /// The scene to unload is not loaded; nothing started.
        /// </summary>
        NotLoaded = 3,

        /// <summary>
        /// The service was disposed before or during the operation.
        /// </summary>
        Disposed = 4,

        /// <summary>
        /// The scene could not be loaded or unloaded; <see cref="OnitySceneResult.Exception" /> holds the cause.
        /// </summary>
        LoadFailed = 5,

        /// <summary>
        /// The scene loaded, but its <c>SceneContext</c> build failed, the context was destroyed, or the scene
        /// unloaded before it was ready. The cover still lifted.
        /// </summary>
        ReadinessFailed = 6,

        /// <summary>
        /// The scene loaded, but its <c>SceneContext</c> was not ready within the reveal policy's maximum wait. The
        /// cover lifted; the scene may still finish building.
        /// </summary>
        ReadinessTimedOut = 7
    }

    /// <summary>
    /// The outcome of one <see cref="IOnitySceneService" /> request.
    /// </summary>
    public readonly struct OnitySceneResult
    {
        /// <summary>
        /// Initializes a result, for example in a test double of <see cref="IOnitySceneService" />.
        /// </summary>
        /// <param name="status">How the request ended.</param>
        /// <param name="operationId">The operation's identity, or 0 when nothing started.</param>
        /// <param name="scene">The final scene, or <c>default</c> when there is none.</param>
        /// <param name="message">Why the request did not succeed; null on success.</param>
        /// <param name="exception">The exception behind a failure, when one exists.</param>
        public OnitySceneResult(
            OnitySceneResultStatus status,
            int operationId,
            Scene scene,
            string message = null,
            Exception exception = null)
        {
            Status = status;
            OperationId = operationId;
            Scene = scene;
            Message = message;
            Exception = exception;
        }

        /// <summary>
        /// Gets how the request ended.
        /// </summary>
        public OnitySceneResultStatus Status { get; }

        /// <summary>
        /// Gets whether the request succeeded.
        /// </summary>
        public bool IsSuccess => Status == OnitySceneResultStatus.Succeeded;

        /// <summary>
        /// Gets the identity of the operation the request started, as published in
        /// <see cref="IOnitySceneService.State" />; 0 when the request started nothing.
        /// </summary>
        public int OperationId { get; }

        /// <summary>
        /// Gets the exact scene the operation ended on: the loaded target, also after a readiness failure, or the
        /// unloaded scene. <c>default</c> when no scene was reached.
        /// </summary>
        public Scene Scene { get; }

        /// <summary>
        /// Gets why the request did not succeed; null on success.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the exception behind a <see cref="OnitySceneResultStatus.LoadFailed" /> or
        /// <see cref="OnitySceneResultStatus.ReadinessFailed" /> result, when one exists.
        /// </summary>
        public Exception Exception { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return Message == null
                ? $"{Status} (operation {OperationId})"
                : $"{Status} (operation {OperationId}): {Message}";
        }
    }
}
