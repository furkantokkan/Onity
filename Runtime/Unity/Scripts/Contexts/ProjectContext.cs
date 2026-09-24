using UnityEngine;

namespace Onity.Unity.Contexts
{
    /// <summary>
    /// Global root context that persists across scene loads.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ProjectContext : OnityContext
    {
        private static ProjectContext s_instance;

        /// <summary>
        /// Active global project context instance.
        /// </summary>
        public static ProjectContext Instance => s_instance;

        /// <inheritdoc />
        protected override void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                DestroyContextObject();
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            base.Awake();
        }

        /// <inheritdoc />
        protected override void OnDestroy()
        {
            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
            }

            base.OnDestroy();
        }

        private void DestroyContextObject()
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
                return;
            }

            DestroyImmediate(gameObject);
        }
    }
}
