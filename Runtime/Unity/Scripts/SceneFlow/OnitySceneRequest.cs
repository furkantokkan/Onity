using System;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Selects the screen cover of one <see cref="IOnitySceneService" /> request.
    /// </summary>
    public enum OnitySceneCoverMode
    {
        /// <summary>
        /// The service's default cover; no cover when the service has none.
        /// </summary>
        Default = 0,

        /// <summary>
        /// No cover.
        /// </summary>
        None = 1,

        /// <summary>
        /// The request's own cover, <see cref="OnitySceneRequest.Cover" />.
        /// </summary>
        Custom = 2
    }

    /// <summary>
    /// One scene load for <see cref="IOnitySceneService.LoadAsync" />: the target scene, how it loads, its enter
    /// data and its cover. Build it with <see cref="Single" /> or <see cref="Additive" />, then refine it with the
    /// <c>With</c> methods, which return a changed copy.
    /// </summary>
    /// <example>
    /// <code>
    /// OnitySceneRequest.Single("Gameplay", new LevelEnterData(3));
    /// OnitySceneRequest.Single("Gameplay").WithCover(m_iris);
    /// OnitySceneRequest.Single("Gameplay", startData).WithPreparedScene();
    /// OnitySceneRequest.Additive("Hud");
    /// OnitySceneRequest.Additive("Level2", setActive: true);
    /// </code>
    /// </example>
    public readonly struct OnitySceneRequest
    {
        private readonly bool m_setActive;

        private OnitySceneRequest(
            string scene,
            LoadSceneMode mode,
            IOnitySceneEnterData enterData,
            OnitySceneCoverMode coverMode,
            IOnitySceneCover cover,
            bool preferPreparedScene,
            bool setActive)
        {
            Scene = scene;
            Mode = mode;
            EnterData = enterData;
            CoverMode = coverMode;
            Cover = cover;
            PreferPreparedScene = preferPreparedScene;
            m_setActive = setActive;
        }

        /// <summary>
        /// Gets the target scene: a scene name, or a path when several scenes in the Build Settings share the name.
        /// </summary>
        public string Scene { get; }

        /// <summary>
        /// Gets how the scene loads: <see cref="LoadSceneMode.Single" /> replaces every loaded scene,
        /// <see cref="LoadSceneMode.Additive" /> keeps them.
        /// </summary>
        public LoadSceneMode Mode { get; }

        /// <summary>
        /// Gets the data the target scene reads with
        /// <see cref="OnitySceneTransitionStore.TryGetEnterData{TEnterData}" />, or the start data a prepared scene
        /// receives; may be null.
        /// </summary>
        public IOnitySceneEnterData EnterData { get; }

        /// <summary>
        /// Gets the cover selection. <see cref="Single" /> starts with <see cref="OnitySceneCoverMode.Default" /> and
        /// <see cref="Additive" /> with <see cref="OnitySceneCoverMode.None" />.
        /// </summary>
        public OnitySceneCoverMode CoverMode { get; }

        /// <summary>
        /// Gets the cover of a <see cref="OnitySceneCoverMode.Custom" /> request; null otherwise.
        /// </summary>
        public IOnitySceneCover Cover { get; }

        /// <summary>
        /// Gets whether a Single request starts the scene an <see cref="OnityScenePreloader" /> keeps prepared,
        /// and loads it normally when none matches.
        /// </summary>
        public bool PreferPreparedScene { get; }

        /// <summary>
        /// Gets whether the target becomes the active scene: always for a Single request, and for an Additive
        /// request only when it asks.
        /// </summary>
        public bool SetActive => Mode == LoadSceneMode.Single || m_setActive;

        /// <summary>
        /// Creates a request that replaces every loaded scene with <paramref name="scene" />, behind the service's
        /// default cover and, when the service's profile routes through it, the Loading scene.
        /// </summary>
        /// <param name="scene">The scene name or path.</param>
        /// <param name="enterData">Optional data for the target scene.</param>
        /// <returns>The request.</returns>
        public static OnitySceneRequest Single(string scene, IOnitySceneEnterData enterData = null)
        {
            return new OnitySceneRequest(
                scene,
                LoadSceneMode.Single,
                enterData,
                OnitySceneCoverMode.Default,
                null,
                false,
                true);
        }

        /// <summary>
        /// Creates a request that loads <paramref name="scene" /> next to the loaded scenes, with no cover.
        /// </summary>
        /// <param name="scene">The scene name or path.</param>
        /// <param name="enterData">Optional data for the target scene.</param>
        /// <param name="setActive">True to make the loaded scene the active scene.</param>
        /// <returns>The request.</returns>
        public static OnitySceneRequest Additive(
            string scene,
            IOnitySceneEnterData enterData = null,
            bool setActive = false)
        {
            return new OnitySceneRequest(
                scene,
                LoadSceneMode.Additive,
                enterData,
                OnitySceneCoverMode.None,
                null,
                false,
                setActive);
        }

        /// <summary>
        /// Returns a copy that runs behind <paramref name="cover" /> instead of the default cover.
        /// </summary>
        /// <param name="cover">The cover for this request.</param>
        /// <returns>The changed request.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="cover" /> is null; use <see cref="WithoutCover" />.</exception>
        public OnitySceneRequest WithCover(IOnitySceneCover cover)
        {
            if (cover == null)
            {
                throw new ArgumentNullException(nameof(cover));
            }

            return new OnitySceneRequest(
                Scene,
                Mode,
                EnterData,
                OnitySceneCoverMode.Custom,
                cover,
                PreferPreparedScene,
                m_setActive);
        }

        /// <summary>
        /// Returns a copy that runs with no cover.
        /// </summary>
        /// <returns>The changed request.</returns>
        public OnitySceneRequest WithoutCover()
        {
            return new OnitySceneRequest(
                Scene,
                Mode,
                EnterData,
                OnitySceneCoverMode.None,
                null,
                PreferPreparedScene,
                m_setActive);
        }

        /// <summary>
        /// Returns a copy that runs behind the service's default cover, for example an Additive request that should
        /// cover the screen.
        /// </summary>
        /// <returns>The changed request.</returns>
        public OnitySceneRequest WithDefaultCover()
        {
            return new OnitySceneRequest(
                Scene,
                Mode,
                EnterData,
                OnitySceneCoverMode.Default,
                null,
                PreferPreparedScene,
                m_setActive);
        }

        /// <summary>
        /// Returns a copy that starts the scene an <see cref="OnityScenePreloader" /> keeps prepared under
        /// <see cref="Scene" /> when it accepts <see cref="EnterData" /> as its start data, and loads the scene
        /// normally otherwise. Only Single requests can start a prepared scene.
        /// </summary>
        /// <returns>The changed request.</returns>
        public OnitySceneRequest WithPreparedScene()
        {
            return new OnitySceneRequest(
                Scene,
                Mode,
                EnterData,
                CoverMode,
                Cover,
                true,
                m_setActive);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Mode} '{Scene}'";
        }
    }
}
