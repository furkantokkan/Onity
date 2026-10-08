using System;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// The step an <see cref="IOnitySceneService" /> operation is in.
    /// </summary>
    public enum OnitySceneOperationPhase
    {
        /// <summary>
        /// No operation runs.
        /// </summary>
        Idle = 0,

        /// <summary>
        /// A cover is showing over the current scene.
        /// </summary>
        Covering = 1,

        /// <summary>
        /// A scene loads; <see cref="OnitySceneOperationState.Progress" /> reports how far.
        /// </summary>
        Loading = 2,

        /// <summary>
        /// The target scene loaded and its <c>SceneContext</c> builds.
        /// </summary>
        Initializing = 3,

        /// <summary>
        /// The cover is lifting.
        /// </summary>
        Revealing = 4,

        /// <summary>
        /// A scene unloads.
        /// </summary>
        Unloading = 5
    }

    /// <summary>
    /// The current operation of an <see cref="IOnitySceneService" />, published through
    /// <see cref="IOnitySceneService.State" /> only when it changes.
    /// </summary>
    public readonly struct OnitySceneOperationState : IEquatable<OnitySceneOperationState>
    {
        /// <summary>
        /// Initializes a state.
        /// </summary>
        /// <param name="operationId">The running operation's identity; 0 when idle.</param>
        /// <param name="phase">The operation's step.</param>
        /// <param name="scene">The scene the operation targets, as requested; null when idle.</param>
        /// <param name="progress">Normalized progress of the current step, from 0 to 1.</param>
        public OnitySceneOperationState(
            int operationId,
            OnitySceneOperationPhase phase,
            string scene,
            float progress)
        {
            OperationId = operationId;
            Phase = phase;
            Scene = scene;
            Progress = progress;
        }

        /// <summary>
        /// Gets the idle state.
        /// </summary>
        public static OnitySceneOperationState Idle => default;

        /// <summary>
        /// Gets the running operation's identity, the one its <see cref="OnitySceneResult" /> carries; 0 when idle.
        /// </summary>
        public int OperationId { get; }

        /// <summary>
        /// Gets the operation's step.
        /// </summary>
        public OnitySceneOperationPhase Phase { get; }

        /// <summary>
        /// Gets the scene the operation targets, as requested; null when idle.
        /// </summary>
        public string Scene { get; }

        /// <summary>
        /// Gets the normalized progress of the current step, from 0 to 1.
        /// </summary>
        public float Progress { get; }

        /// <inheritdoc />
        public bool Equals(OnitySceneOperationState other)
        {
            return OperationId == other.OperationId
                && Phase == other.Phase
                && Progress.Equals(other.Progress)
                && string.Equals(Scene, other.Scene, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OnitySceneOperationState other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = OperationId;
                hash = (hash * 397) ^ (int)Phase;
                hash = (hash * 397) ^ Progress.GetHashCode();
                return (hash * 397) ^ (Scene != null ? StringComparer.Ordinal.GetHashCode(Scene) : 0);
            }
        }

        /// <summary>
        /// Compares two states by value.
        /// </summary>
        /// <param name="left">The first state.</param>
        /// <param name="right">The second state.</param>
        /// <returns>True when both are equal.</returns>
        public static bool operator ==(OnitySceneOperationState left, OnitySceneOperationState right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Compares two states by value.
        /// </summary>
        /// <param name="left">The first state.</param>
        /// <param name="right">The second state.</param>
        /// <returns>True when they differ.</returns>
        public static bool operator !=(OnitySceneOperationState left, OnitySceneOperationState right)
        {
            return left.Equals(right) == false;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Phase == OnitySceneOperationPhase.Idle
                ? "Idle"
                : $"{Phase} '{Scene}' {Progress:P0} (operation {OperationId})";
        }
    }
}
