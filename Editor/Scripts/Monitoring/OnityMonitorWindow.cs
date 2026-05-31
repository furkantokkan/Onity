using System;
using System.Collections.Generic;
using Onity.DI;
using Onity.Messaging;
using Onity.Unity.Contexts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// UI Toolkit diagnostics window for Onity contexts, container state, and messaging channels.
    /// </summary>
    public sealed class OnityMonitorWindow : EditorWindow
    {
        private const double k_refreshIntervalSeconds = 0.5d;
        private const string k_windowTitle = "Onity Monitor";

        private readonly List<ContextEntry> m_contextEntries = new List<ContextEntry>(32);
        private readonly List<MessageChannelDiagnostics> m_channelDiagnostics = new List<MessageChannelDiagnostics>(32);

        private ListView m_contextListView;
        private Toggle m_autoRefreshToggle;
        private Label m_lastRefreshLabel;
        private Label m_contextDetailsLabel;
        private Label m_containerDetailsLabel;
        private Label m_messageDetailsLabel;
        private ScrollView m_messageChannelScrollView;

        private OnityContext m_selectedContext;
        private double m_nextRefreshTime;

        [MenuItem("Onity/Tools/Monitor", false, 100)]
        private static void OpenWindow()
        {
            OnityMonitorWindow window = GetWindow<OnityMonitorWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(900f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            m_nextRefreshTime = 0d;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void CreateGUI()
        {
            BuildLayout(rootVisualElement);
            RefreshMonitor();
        }

        private void OnEditorUpdate()
        {
            if (m_autoRefreshToggle == null || m_autoRefreshToggle.value == false)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < m_nextRefreshTime)
            {
                return;
            }

            m_nextRefreshTime = EditorApplication.timeSinceStartup + k_refreshIntervalSeconds;
            RefreshMonitor();
        }

        private void BuildLayout(VisualElement root)
        {
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            VisualElement toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginBottom = 6f;
            Button refreshButton = new Button(RefreshMonitor)
            {
                text = "Refresh"
            };

            m_autoRefreshToggle = new Toggle("Auto Refresh")
            {
                value = true
            };

            m_lastRefreshLabel = new Label("Last Refresh: -");
            m_lastRefreshLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            m_lastRefreshLabel.style.flexGrow = 1f;

            toolbar.Add(refreshButton);
            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);
            toolbar.Add(m_autoRefreshToggle);
            toolbar.Add(m_lastRefreshLabel);
            root.Add(toolbar);

            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            root.Add(body);

            VisualElement leftPanel = new VisualElement();
            leftPanel.style.width = 340f;
            leftPanel.style.flexShrink = 0f;
            leftPanel.style.flexDirection = FlexDirection.Column;
            leftPanel.style.marginRight = 10f;
            body.Add(leftPanel);

            Label contextTitle = new Label("Contexts");
            contextTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            leftPanel.Add(contextTitle);

            m_contextListView = new ListView();
            m_contextListView.itemsSource = m_contextEntries;
            m_contextListView.selectionType = SelectionType.Single;
            m_contextListView.reorderable = false;
            m_contextListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            m_contextListView.style.flexGrow = 1f;
            m_contextListView.makeItem = MakeContextListItem;
            m_contextListView.bindItem = BindContextListItem;
            m_contextListView.selectionChanged += OnContextSelectionChanged;
            leftPanel.Add(m_contextListView);

            VisualElement rightPanel = new VisualElement();
            rightPanel.style.flexGrow = 1f;
            rightPanel.style.flexDirection = FlexDirection.Column;
            body.Add(rightPanel);

            rightPanel.Add(CreateSectionTitle("Context"));
            m_contextDetailsLabel = CreateDetailsLabel();
            rightPanel.Add(m_contextDetailsLabel);

            rightPanel.Add(CreateSectionTitle("Container"));
            m_containerDetailsLabel = CreateDetailsLabel();
            rightPanel.Add(m_containerDetailsLabel);

            rightPanel.Add(CreateSectionTitle("Messaging"));
            m_messageDetailsLabel = CreateDetailsLabel();
            rightPanel.Add(m_messageDetailsLabel);

            m_messageChannelScrollView = new ScrollView(ScrollViewMode.Vertical);
            m_messageChannelScrollView.style.flexGrow = 1f;
            m_messageChannelScrollView.style.borderTopWidth = 1f;
            m_messageChannelScrollView.style.borderBottomWidth = 1f;
            m_messageChannelScrollView.style.borderLeftWidth = 1f;
            m_messageChannelScrollView.style.borderRightWidth = 1f;
            m_messageChannelScrollView.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
            m_messageChannelScrollView.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
            m_messageChannelScrollView.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
            m_messageChannelScrollView.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
            rightPanel.Add(m_messageChannelScrollView);
        }

        private static VisualElement MakeContextListItem()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.paddingLeft = 4f;
            label.style.paddingTop = 3f;
            label.style.paddingBottom = 3f;
            return label;
        }

        private void BindContextListItem(VisualElement item, int index)
        {
            if (item is Label label && index >= 0 && index < m_contextEntries.Count)
            {
                label.text = m_contextEntries[index].DisplayName;
            }
        }

        private static Label CreateSectionTitle(string title)
        {
            Label label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 2f;
            return label;
        }

        private static Label CreateDetailsLabel()
        {
            Label label = new Label();
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.UpperLeft;
            return label;
        }

        private void RefreshMonitor()
        {
            RefreshContextList();
            RefreshDetails();

            if (m_lastRefreshLabel != null)
            {
                string now = DateTime.Now.ToString("HH:mm:ss");
                m_lastRefreshLabel.text = $"Last Refresh: {now}";
            }
        }

        private void RefreshContextList()
        {
            int selectedInstanceId = m_selectedContext != null ? m_selectedContext.GetInstanceID() : 0;
            m_contextEntries.Clear();

            OnityContext[] contexts = UnityEngine.Object.FindObjectsByType<OnityContext>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < contexts.Length; i++)
            {
                OnityContext context = contexts[i];

                if (context == null)
                {
                    continue;
                }

                if (EditorUtility.IsPersistent(context))
                {
                    continue;
                }

                ContextEntry entry = new ContextEntry(context, BuildContextDisplayName(context));
                m_contextEntries.Add(entry);
            }

            m_contextEntries.Sort(
                (a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

            m_contextListView?.Rebuild();

            if (m_contextEntries.Count == 0)
            {
                m_selectedContext = null;
                return;
            }

            int selectedIndex = 0;

            for (int i = 0; i < m_contextEntries.Count; i++)
            {
                if (m_contextEntries[i].Context != null && m_contextEntries[i].Context.GetInstanceID() == selectedInstanceId)
                {
                    selectedIndex = i;
                    break;
                }
            }

            m_contextListView.selectedIndex = selectedIndex;
            m_selectedContext = m_contextEntries[selectedIndex].Context;
        }

        private static string BuildContextDisplayName(OnityContext context)
        {
            string sceneName = context.gameObject.scene.IsValid() ? context.gameObject.scene.name : "NoScene";
            string hierarchyPath = GetHierarchyPath(context.transform);
            return $"{context.GetType().Name} | {sceneName} | {hierarchyPath}";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private void OnContextSelectionChanged(IEnumerable<object> selectedItems)
        {
            m_selectedContext = null;

            foreach (object selectedItem in selectedItems)
            {
                if (selectedItem is ContextEntry entry)
                {
                    m_selectedContext = entry.Context;
                    break;
                }
            }

            RefreshDetails();
        }

        private void RefreshDetails()
        {
            if (m_contextDetailsLabel == null || m_containerDetailsLabel == null || m_messageDetailsLabel == null)
            {
                return;
            }

            if (m_selectedContext == null)
            {
                m_contextDetailsLabel.text = "Select a context from the list.";
                m_containerDetailsLabel.text = "-";
                m_messageDetailsLabel.text = "-";
                SetChannelLabels(Array.Empty<MessageChannelDiagnostics>());
                return;
            }

            m_contextDetailsLabel.text =
                $"Name: {m_selectedContext.name}\n" +
                $"Type: {m_selectedContext.GetType().Name}\n" +
                $"Scene: {m_selectedContext.gameObject.scene.name}\n" +
                $"Active: {m_selectedContext.gameObject.activeInHierarchy}\n" +
                $"Enabled: {m_selectedContext.enabled}";

            OnityContainer container = m_selectedContext.Container;

            if (container == null)
            {
                m_containerDetailsLabel.text = "Container: Not initialized yet.";
                m_messageDetailsLabel.text = "Message Broker: Not available.";
                SetChannelLabels(Array.Empty<MessageChannelDiagnostics>());
                return;
            }

            try
            {
                OnityContainerDiagnostics diagnostics = container.GetDiagnostics();
                m_containerDetailsLabel.text =
                    $"Explicit Bindings: {diagnostics.ExplicitBindingCount}\n" +
                    $"Implicit Bindings: {diagnostics.ImplicitBindingCount}\n" +
                    $"Cached Injection Plans: {diagnostics.CachedPlanCount}\n" +
                    $"Owned Providers: {diagnostics.OwnedProviderCount}\n" +
                    $"Has Parent Scope: {diagnostics.HasParent}";

                RefreshMessageDiagnostics(container);
            }
            catch (Exception exception)
            {
                m_containerDetailsLabel.text = $"Container diagnostics unavailable: {exception.Message}";
                m_messageDetailsLabel.text = "Message Broker diagnostics unavailable.";
                SetChannelLabels(Array.Empty<MessageChannelDiagnostics>());
            }
        }

        private void RefreshMessageDiagnostics(OnityContainer container)
        {
            if (container.TryResolve(out IMessageBroker brokerInterface) == false)
            {
                m_messageDetailsLabel.text = "Message Broker: Not bound.";
                SetChannelLabels(Array.Empty<MessageChannelDiagnostics>());
                return;
            }

            if (brokerInterface is MessageBroker broker == false)
            {
                m_messageDetailsLabel.text = $"Message Broker: {brokerInterface.GetType().Name} (custom implementation)";
                SetChannelLabels(Array.Empty<MessageChannelDiagnostics>());
                return;
            }

            m_channelDiagnostics.Clear();
            broker.GetDiagnostics(m_channelDiagnostics);
            m_channelDiagnostics.Sort(
                (a, b) => string.Compare(a.MessageType.FullName, b.MessageType.FullName, StringComparison.Ordinal));

            m_messageDetailsLabel.text =
                $"Message Broker Type: {nameof(MessageBroker)}\n" +
                $"Channel Count: {broker.ChannelCount}";

            SetChannelLabels(m_channelDiagnostics);
        }

        private void SetChannelLabels(IReadOnlyList<MessageChannelDiagnostics> channels)
        {
            m_messageChannelScrollView.Clear();

            if (channels.Count == 0)
            {
                Label emptyLabel = new Label("No channels allocated.");
                emptyLabel.style.paddingLeft = 6f;
                emptyLabel.style.paddingTop = 4f;
                m_messageChannelScrollView.Add(emptyLabel);
                return;
            }

            for (int i = 0; i < channels.Count; i++)
            {
                MessageChannelDiagnostics channel = channels[i];
                Label row = new Label(
                    $"{channel.MessageType.FullName}  |  Subscribers: {channel.SubscriberCount}");

                row.style.paddingLeft = 6f;
                row.style.paddingTop = 2f;
                row.style.paddingBottom = 2f;
                m_messageChannelScrollView.Add(row);
            }
        }

        private readonly struct ContextEntry
        {
            public readonly OnityContext Context;
            public readonly string DisplayName;

            public ContextEntry(OnityContext context, string displayName)
            {
                Context = context;
                DisplayName = displayName;
            }
        }
    }
}
