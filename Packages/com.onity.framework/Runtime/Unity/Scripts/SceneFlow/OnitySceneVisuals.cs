using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Hides a whole loaded scene in one call: its cameras, audio listeners and lights stop and its UI documents stop
    /// drawing and taking presses. <see cref="OnityScenePreloader" /> uses it on the scene a started scene replaces,
    /// right before that scene unloads, so the two never show together.
    /// </summary>
    public static class OnitySceneVisuals
    {
        // The audio module is optional and the package does not depend on it, so the listener type is looked up once.
        private static readonly Type s_audioListenerType =
            Type.GetType("UnityEngine.AudioListener, UnityEngine.AudioModule", false);

        // Reused lists; main thread only. Cleared after every call so they keep no scene objects alive.
        private static readonly List<GameObject> s_roots = new List<GameObject>(16);
        private static readonly List<Camera> s_cameras = new List<Camera>(4);
        private static readonly List<Light> s_lights = new List<Light>(8);
        private static readonly List<UIDocument> s_documents = new List<UIDocument>(8);

        /// <summary>
        /// Hides <paramref name="scene" />: disables its cameras, audio listeners and lights, and sets the root of
        /// each of its UI documents to <c>display: none</c> (the documents stay enabled, so their trees survive). An
        /// invalid or unloaded scene is ignored. It walks the scene's objects, so call it once per scene change,
        /// never per frame. Main thread only.
        /// </summary>
        /// <param name="scene">The scene to hide.</param>
        public static void Hide(Scene scene)
        {
            if (scene.IsValid() == false || scene.isLoaded == false)
            {
                return;
            }

            try
            {
                scene.GetRootGameObjects(s_roots);

                for (int index = 0; index < s_roots.Count; index++)
                {
                    HideRoot(s_roots[index]);
                }
            }
            finally
            {
                s_roots.Clear();
                s_cameras.Clear();
                s_lights.Clear();
                s_documents.Clear();
            }
        }

        private static void HideRoot(GameObject root)
        {
            root.GetComponentsInChildren(true, s_cameras);

            for (int index = 0; index < s_cameras.Count; index++)
            {
                s_cameras[index].enabled = false;
            }

            root.GetComponentsInChildren(true, s_lights);

            for (int index = 0; index < s_lights.Count; index++)
            {
                s_lights[index].enabled = false;
            }

            root.GetComponentsInChildren(true, s_documents);

            for (int index = 0; index < s_documents.Count; index++)
            {
                VisualElement documentRoot = s_documents[index].rootVisualElement;

                if (documentRoot != null)
                {
                    documentRoot.style.display = DisplayStyle.None;
                }
            }

            if (s_audioListenerType == null)
            {
                return;
            }

            Component[] listeners = root.GetComponentsInChildren(s_audioListenerType, true);

            for (int index = 0; index < listeners.Length; index++)
            {
                if (listeners[index] is Behaviour listener)
                {
                    listener.enabled = false;
                }
            }
        }
    }
}
