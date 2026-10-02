using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// Base class for SEP-style scene initialization entry point.
    /// </summary>
    public abstract class OnitySceneInitiator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("When enabled, initialization runs once for this component lifetime.")]
        private bool m_runOnlyOnce = true;

        private CancellationTokenSource m_destroyCancellationTokenSource;
        private bool m_hasInitialized;
        private bool m_isInitializing;

        private void Start()
        {
            if (m_runOnlyOnce && m_hasInitialized)
            {
                return;
            }

            if (m_isInitializing)
            {
                return;
            }

            _ = InitializeInternalAsync();
        }

        private void OnDestroy()
        {
            if (m_destroyCancellationTokenSource == null)
            {
                return;
            }

            m_destroyCancellationTokenSource.Cancel();
            m_destroyCancellationTokenSource.Dispose();
            m_destroyCancellationTokenSource = null;
        }

        /// <summary>
        /// Called once when scene initiator starts.
        /// </summary>
        /// <param name="cancellationToken">Token canceled on destroy.</param>
        /// <returns>Initialization task.</returns>
        protected abstract Task InitializeAsync(CancellationToken cancellationToken);

        private async Task InitializeInternalAsync()
        {
            m_isInitializing = true;

            if (m_destroyCancellationTokenSource == null)
            {
                m_destroyCancellationTokenSource = new CancellationTokenSource();
            }

            try
            {
                await InitializeAsync(m_destroyCancellationTokenSource.Token);
                m_hasInitialized = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                m_isInitializing = false;
            }
        }
    }
}
