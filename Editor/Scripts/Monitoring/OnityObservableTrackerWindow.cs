using System;
using System.Collections.Generic;
using Onity.Reactive;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// Editor window for tracking Onity reactive subscriptions.
    /// </summary>
    public sealed class OnityObservableTrackerWindow : EditorWindow
    {
        private const string k_windowTitle = "Onity Observable Tracker";
        private const double k_refreshIntervalSeconds = 0.5d;

        private readonly List<OnityTrackedObservableInfo> m_rows = new List<OnityTrackedObservableInfo>(256);
        private Vector2 m_scrollPosition;
        private bool m_autoRefresh = true;
        private bool m_includeDisposed = true;
        private string m_searchText = string.Empty;
        private bool m_showStackTrace;
        private double m_nextRefreshTime;

        [MenuItem("Onity/Tools/Observable Tracker", false, 121)]
        private static void OpenWindow()
        {
            OnityObservableTrackerWindow window = GetWindow<OnityObservableTrackerWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(980f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            m_showStackTrace = OnityObservableTracker.EnableStackTrace;
            RefreshRows();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (m_autoRefresh == false)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup < m_nextRefreshTime)
            {
                return;
            }

            m_nextRefreshTime = EditorApplication.timeSinceStartup + k_refreshIntervalSeconds;
            RefreshRows();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawHeader();
            DrawRows();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool nextTrackingEnabled = GUILayout.Toggle(
                OnityObservableTracker.EnableTracking,
                "Tracking Enabled",
                EditorStyles.toolbarButton,
                GUILayout.Width(124f));

            if (nextTrackingEnabled != OnityObservableTracker.EnableTracking)
            {
                OnityObservableTracker.EnableTracking = nextTrackingEnabled;
                RefreshRows();
            }

            bool nextStackTraceEnabled = GUILayout.Toggle(
                m_showStackTrace,
                "StackTrace",
                EditorStyles.toolbarButton,
                GUILayout.Width(92f));

            if (nextStackTraceEnabled != m_showStackTrace)
            {
                m_showStackTrace = nextStackTraceEnabled;
                OnityObservableTracker.EnableStackTrace = nextStackTraceEnabled;
                RefreshRows();
            }

            bool nextAutoRefresh = GUILayout.Toggle(
                m_autoRefresh,
                "Auto",
                EditorStyles.toolbarButton,
                GUILayout.Width(50f));

            if (nextAutoRefresh != m_autoRefresh)
            {
                m_autoRefresh = nextAutoRefresh;
            }

            bool nextIncludeDisposed = GUILayout.Toggle(
                m_includeDisposed,
                "Include Disposed",
                EditorStyles.toolbarButton,
                GUILayout.Width(132f));

            if (nextIncludeDisposed != m_includeDisposed)
            {
                m_includeDisposed = nextIncludeDisposed;
                RefreshRows();
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                RefreshRows();
            }

            if (GUILayout.Button("Clear Disposed", EditorStyles.toolbarButton, GUILayout.Width(116f)))
            {
                OnityObservableTracker.ClearDisposed();
                RefreshRows();
            }

            if (GUILayout.Button("Clear All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                OnityObservableTracker.ClearAll();
                RefreshRows();
            }

            GUILayout.Space(8f);

            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.textField;
            string nextSearch = GUILayout.TextField(
                m_searchText,
                searchStyle,
                GUILayout.MinWidth(180f),
                GUILayout.MaxWidth(240f));

            if (string.Equals(nextSearch, m_searchText, StringComparison.Ordinal) == false)
            {
                m_searchText = nextSearch;
            }

            bool hasSearchText = string.IsNullOrEmpty(m_searchText) == false;
            if (hasSearchText == false)
            {
                GUILayout.Space(18f);
            }
            else if (GUILayout.Button(
                GUIContent.none,
                GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.miniButton,
                GUILayout.Width(18f)))
            {
                m_searchText = string.Empty;
                GUI.FocusControl(null);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Rows: {GetVisibleCount()}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private static void DrawHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 0.35f));

            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y + 2f, 50f, rect.height), "Id", EditorStyles.boldLabel);
            x += 52f;
            GUI.Label(new Rect(x, rect.y + 2f, 80f, rect.height), "Active", EditorStyles.boldLabel);
            x += 82f;
            GUI.Label(new Rect(x, rect.y + 2f, 300f, rect.height), "Observable", EditorStyles.boldLabel);
            x += 302f;
            GUI.Label(new Rect(x, rect.y + 2f, 280f, rect.height), "Observer", EditorStyles.boldLabel);
            x += 282f;
            GUI.Label(new Rect(x, rect.y + 2f, 90f, rect.height), "Elapsed(ms)", EditorStyles.boldLabel);
            x += 92f;
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), "Started(UTC)", EditorStyles.boldLabel);
        }

        private void DrawRows()
        {
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            int visibleIndex = 0;

            for (int i = 0; i < m_rows.Count; i++)
            {
                OnityTrackedObservableInfo row = m_rows[i];

                if (IsVisible(row) == false)
                {
                    continue;
                }

                Rect rowRect = EditorGUILayout.GetControlRect(false, 20f);
                Color backgroundColor = visibleIndex % 2 == 0
                    ? new Color(0.1f, 0.1f, 0.1f, 0.15f)
                    : new Color(0.08f, 0.08f, 0.08f, 0.25f);
                EditorGUI.DrawRect(rowRect, backgroundColor);
                DrawDataRow(rowRect, row);
                visibleIndex++;

                if (m_showStackTrace && string.IsNullOrEmpty(row.StackTrace) == false)
                {
                    Rect stackRect = EditorGUILayout.GetControlRect(false, 44f);
                    EditorGUI.DrawRect(stackRect, new Color(0.05f, 0.05f, 0.05f, 0.25f));
                    GUIStyle stackStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        wordWrap = true,
                        alignment = TextAnchor.UpperLeft
                    };

                    GUI.Label(
                        new Rect(stackRect.x + 6f, stackRect.y + 2f, stackRect.width - 12f, stackRect.height - 4f),
                        row.StackTrace,
                        stackStyle);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawDataRow(Rect rect, OnityTrackedObservableInfo row)
        {
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y + 2f, 50f, rect.height), row.TrackingId.ToString(), EditorStyles.label);
            x += 52f;

            GUIStyle activeStyle = new GUIStyle(EditorStyles.label);
            activeStyle.normal.textColor = row.IsActive ? new Color(0.5f, 0.95f, 0.55f) : new Color(0.85f, 0.85f, 0.85f);
            GUI.Label(new Rect(x, rect.y + 2f, 80f, rect.height), row.IsActive ? "Active" : "Disposed", activeStyle);
            x += 82f;

            GUI.Label(new Rect(x, rect.y + 2f, 300f, rect.height), row.ObservableType, EditorStyles.label);
            x += 302f;
            GUI.Label(new Rect(x, rect.y + 2f, 280f, rect.height), row.ObserverType, EditorStyles.label);
            x += 282f;
            GUI.Label(new Rect(x, rect.y + 2f, 90f, rect.height), row.ElapsedMilliseconds.ToString("0.0"), EditorStyles.label);
            x += 92f;
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), row.AddedAtUtc.ToString("HH:mm:ss.fff"), EditorStyles.label);
        }

        private void RefreshRows()
        {
            OnityObservableTracker.GetSnapshot(m_rows, m_includeDisposed);
            m_rows.Sort((left, right) => right.TrackingId.CompareTo(left.TrackingId));
        }

        private int GetVisibleCount()
        {
            if (string.IsNullOrWhiteSpace(m_searchText))
            {
                return m_rows.Count;
            }

            int count = 0;

            for (int i = 0; i < m_rows.Count; i++)
            {
                if (IsVisible(m_rows[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsVisible(OnityTrackedObservableInfo row)
        {
            if (string.IsNullOrWhiteSpace(m_searchText))
            {
                return true;
            }

            return ContainsIgnoreCase(row.ObservableType, m_searchText)
                || ContainsIgnoreCase(row.ObserverType, m_searchText)
                || row.TrackingId.ToString().IndexOf(m_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsIgnoreCase(string source, string search)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
            {
                return false;
            }

            return source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
