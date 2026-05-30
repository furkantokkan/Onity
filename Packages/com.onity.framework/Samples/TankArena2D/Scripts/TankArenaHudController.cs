using Onity.Unity.UI;
using UnityEngine;

namespace Onity.Samples.TankArena2D
{
    /// <summary>
    /// Bridges HUD view lifecycle with Onity presenter factory.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TankArenaHudView))]
    public sealed class TankArenaHudController : MonoBehaviour
    {
        [Header("Scene References")]
        [Tooltip("Tank Arena HUD view component.")]
        [SerializeField] private TankArenaHudView m_view;

        private IOnityUiPresenter m_presenter;
        private bool m_isOpened;

        private void Awake()
        {
            if (m_view == null)
            {
                m_view = GetComponent<TankArenaHudView>();
            }
        }

        private void OnEnable()
        {
            if (m_view == null)
            {
                return;
            }

            m_presenter = OnityUiPresenterFactory.Create<TankArenaHudPresenter>();
            m_presenter.SetView(m_view);
            m_presenter.OnViewOpening();
            m_presenter.OnViewOpened();
            m_isOpened = true;
        }

        private void OnDisable()
        {
            CloseAndDisposePresenter();
        }

        private void OnDestroy()
        {
            CloseAndDisposePresenter();
        }

        private void CloseAndDisposePresenter()
        {
            if (m_presenter == null)
            {
                return;
            }

            if (m_isOpened)
            {
                m_presenter.OnViewClosing();
                m_presenter.OnViewClosed();
                m_isOpened = false;
            }

            m_presenter.Dispose();
            m_presenter = null;
        }
    }
}
