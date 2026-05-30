using System;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Samples.RollABall
{
    /// <summary>
    /// Minimal bootstrap loader for Roll-a-Ball sample scene flow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RollABallBootstrapSceneLoader : MonoBehaviour
    {
        [Header("Flow")]
        [Tooltip("Gameplay scene loaded from bootstrap.")]
        [SerializeField] private string m_gameSceneName = "Game";

        [Tooltip("Loads gameplay scene automatically on Start.")]
        [SerializeField] private bool m_autoLoadOnStart = true;

        private bool m_isLoading;

        private async void Start()
        {
            if (m_autoLoadOnStart == false || m_isLoading)
            {
                return;
            }

            m_isLoading = true;

            try
            {
                await OnitySceneLoader.LoadSingleAsync(m_gameSceneName);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                m_isLoading = false;
            }
        }
    }
}
