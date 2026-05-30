using UnityEngine;

namespace Onity.Unity.Contexts
{
    /// <summary>
    /// Runtime bootstrapper that loads <see cref="ProjectContext" /> from Resources before scenes.
    /// </summary>
    public static class ProjectContextBootstrap
    {
        /// <summary>
        /// Resources path used for the ProjectContext prefab.
        /// </summary>
        public const string ResourcePath = "Onity/ProjectContext";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureProjectContextLoaded()
        {
            if (ProjectContext.Instance != null)
            {
                return;
            }

            ProjectContext[] loadedContexts = Object.FindObjectsOfType<ProjectContext>(true);

            if (loadedContexts != null && loadedContexts.Length > 0)
            {
                return;
            }

            ProjectContext prefab = Resources.Load<ProjectContext>(ResourcePath);

            if (prefab == null)
            {
                return;
            }

            ProjectContext instance = Object.Instantiate(prefab);
            instance.gameObject.name = prefab.gameObject.name;
        }
    }
}
