using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Samples
{
    /// <summary>
    /// Runs Tank Arena scene generation once when a marker file is present.
    /// This enables generation from an already-open Unity editor instance.
    /// </summary>
    [InitializeOnLoad]
    public static class OnityTankArenaAutoRegenerate
    {
        private const string k_markerAssetPath = "Assets/Onity-Packages/Onity/Samples/TankArena2D/.regenerate-scenes";
        private const string k_logPrefix = "[OnityTankArenaAutoRegenerate]";

        private static readonly string s_markerAbsolutePath = ResolveMarkerAbsolutePath();

        static OnityTankArenaAutoRegenerate()
        {
            EditorApplication.delayCall += TryRunPendingRegeneration;
        }

        private static void TryRunPendingRegeneration()
        {
            EditorApplication.delayCall -= TryRunPendingRegeneration;

            if (File.Exists(s_markerAbsolutePath) == false)
            {
                return;
            }

            try
            {
                Debug.Log($"{k_logPrefix} Marker found. Regenerating Tank Arena scene flow.");
                OnityTankArenaSceneGenerator.GenerateTankArenaScene();
                Debug.Log($"{k_logPrefix} Scene flow regeneration finished.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                TryDeleteMarker();
                AssetDatabase.Refresh();
            }
        }

        private static string ResolveMarkerAbsolutePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Path.Combine(projectRoot, k_markerAssetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void TryDeleteMarker()
        {
            if (File.Exists(s_markerAbsolutePath) == false)
            {
                return;
            }

            try
            {
                File.Delete(s_markerAbsolutePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"{k_logPrefix} Could not delete marker file: {exception.Message}");
            }
        }
    }
}

