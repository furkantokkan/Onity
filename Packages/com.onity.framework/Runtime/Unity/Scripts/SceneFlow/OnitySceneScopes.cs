using System.Collections.Generic;
using Onity.Unity.Contexts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Finds the <see cref="SceneContext" /> of a loaded scene.
    /// </summary>
    public static class OnitySceneScopes
    {
        // Reused root list; main thread only. Cleared after every lookup so it keeps no scene objects alive.
        private static readonly List<GameObject> s_roots = new List<GameObject>(16);

        /// <summary>
        /// Finds the scene's <see cref="SceneContext" />, including one on an inactive object. It walks the scene's
        /// root objects, so call it once per scene change, never per frame. Main thread only.
        /// </summary>
        /// <param name="scene">A loaded scene.</param>
        /// <param name="context">The scene's context, or null when it returns false.</param>
        /// <returns>False for an invalid or unloaded scene, or a scene without a context.</returns>
        public static bool TryFind(Scene scene, out SceneContext context)
        {
            context = null;

            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return false;
            }

            try
            {
                scene.GetRootGameObjects(s_roots);

                for (int index = 0; index < s_roots.Count; index++)
                {
                    SceneContext found = s_roots[index].GetComponentInChildren<SceneContext>(true);

                    if (found != null)
                    {
                        context = found;
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                s_roots.Clear();
            }
        }
    }
}
