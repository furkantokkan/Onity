using System.Collections.Generic;
using Onity.DI;
using UnityEngine;

namespace OnityShowcase
{
    /// <summary>
    /// Scene composition root for the Coin Rush showcase. Plays the role the full package's
    /// <c>SceneContext</c> plays, but using only the embedded engine-free <see cref="OnityContainer"/>:
    /// it creates the container, runs the assigned <see cref="OnityMonoInstaller"/>, builds the graph,
    /// member-injects every <see cref="ShowcaseBehaviour"/> under this root, drives the global round
    /// countdown each frame, and disposes the container (and its owned singletons) on destroy.
    /// Logic lives in plain services; this behaviour is intentionally thin lifecycle glue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnityShowcaseContext : MonoBehaviour
    {
        [Header("Installer")]
        [Tooltip("Installer that registers the showcase bindings. Defaults to one on this GameObject.")]
        [SerializeField] private OnityMonoInstaller m_installer;

        private OnityContainer m_container;
        private ICountdownService m_countdown;
        private bool m_isReady;

        private void Awake()
        {
            if (m_installer == null)
            {
                m_installer = GetComponent<OnityMonoInstaller>();
            }

            if (m_installer == null)
            {
                Debug.LogError("OnityShowcaseContext requires an OnityMonoInstaller reference.", this);
                return;
            }

            m_container = new OnityContainer();
            m_installer.InstallBindings(m_container);
            m_container.Build();

            InjectChildren();

            m_countdown = m_container.Resolve<ICountdownService>();
            m_isReady = true;

            NotifyChildrenInjected();
        }

        private void Update()
        {
            if (m_isReady == false)
            {
                return;
            }

            // Reactive timers in the full package come from OnityUnityObservable.EveryUpdate();
            // here the context forwards Unity's per-frame delta into the engine-free service.
            m_countdown.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            m_isReady = false;
            m_container?.Dispose();
            m_container = null;
        }

        private void InjectChildren()
        {
            List<ShowcaseBehaviour> behaviours = new List<ShowcaseBehaviour>(16);
            GetComponentsInChildren(true, behaviours);

            for (int i = 0; i < behaviours.Count; i++)
            {
                m_container.Inject(behaviours[i]);
            }
        }

        private void NotifyChildrenInjected()
        {
            List<ShowcaseBehaviour> behaviours = new List<ShowcaseBehaviour>(16);
            GetComponentsInChildren(true, behaviours);

            for (int i = 0; i < behaviours.Count; i++)
            {
                behaviours[i].OnInjected();
            }
        }
    }
}
