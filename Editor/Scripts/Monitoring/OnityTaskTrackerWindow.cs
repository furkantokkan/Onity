using System;
using System.Collections.Generic;
using Onity.Unity.Async;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// Editor window for tracking Onity task activity.
    /// </summary>
    public sealed class OnityTaskTrackerWindow : EditorWindow
    {
        private const string k_windowTitle = "Onity Task Tracker";
        private const double k_refreshIntervalSeconds = 0.5d;

        private readonly List<OnityTrackedTaskInfo> m_rows = new List<OnityTrackedTaskInfo>(256);
        private Vector2 m_scrollPosition;
        private bool m_autoRefresh = true;
        private bool m_includeCompleted = true;
        private string m_searchText = string.Empty;
        private bool m_showStackTrace;
        private double m_nextRefreshTime;

        [MenuItem("Onity/Tools/Task Tracker", false, 120)]
        private static void OpenWindow()
        {
            OnityTaskTrackerWindow window = GetWindow<OnityTaskTrackerWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(920f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            m_showStackTrace = OnityTaskTracker.EnableStackTrace;
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
                OnityTaskTracker.IsEnabled,
                "Tracking Enabled",
                EditorStyles.toolbarButton,
                GUILayout.Width(124f));

            if (nextTrackingEnabled != OnityTaskTracker.IsEnabled)
            {
                OnityTaskTracker.IsEnabled = nextTrackingEnabled;
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
                OnityTaskTracker.EnableStackTrace = nextStackTraceEnabled;
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

            bool nextIncludeCompleted = GUILayout.Toggle(
                m_includeCompleted,
                "Include Completed",
                EditorStyles.toolbarButton,
                GUILayout.Width(142f));

            if (nextIncludeCompleted != m_includeCompleted)
            {
                m_includeCompleted = nextIncludeCompleted;
                RefreshRows();
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                RefreshRows();
            }

            if (GUILayout.Button("Clear Completed", EditorStyles.toolbarButton, GUILayout.Width(126f)))
            {
                OnityTaskTracker.ClearCompleted();
                RefreshRows();
            }

            if (GUILayout.Button("Clear All", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                OnityTaskTracker.ClearAll();
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
            GUI.Label(new Rect(x, rect.y + 2f, 120f, rect.height), "Status", EditorStyles.boldLabel);
            x += 122f;
            GUI.Label(new Rect(x, rect.y + 2f, 280f, rect.height), "Source", EditorStyles.boldLabel);
            x += 282f;
            GUI.Label(new Rect(x, rect.y + 2f, 90f, rect.height), "Elapsed(ms)", EditorStyles.boldLabel);
            x += 92f;
            GUI.Label(new Rect(x, rect.y + 2f, 170f, rect.height), "Started(UTC)", EditorStyles.boldLabel);
            x += 172f;
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), "Error", EditorStyles.boldLabel);
        }

        private void DrawRows()
        {
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            int visibleIndex = 0;

            for (int i = 0; i < m_rows.Count; i++)
            {
                OnityTrackedTaskInfo row = m_rows[i];

                if (IsVisible(row) == false)
                {
                    continue;
                }

                Rect rect = EditorGUILayout.GetControlRect(false, 20f);
                Color background = visibleIndex % 2 == 0
                    ? new Color(0.1f, 0.1f, 0.1f, 0.15f)
                    : new Color(0.08f, 0.08f, 0.08f, 0.25f);
                EditorGUI.DrawRect(rect, background);
                DrawDataRow(rect, row);
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

        private static void DrawDataRow(Rect rect, OnityTrackedTaskInfo row)
        {
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y + 2f, 50f, rect.height), row.TaskId.ToString(), EditorStyles.label);
            x += 52f;

            GUIStyle statusStyle = new GUIStyle(EditorStyles.label);

            if (row.Status == System.Threading.Tasks.TaskStatus.Faulted)
            {
                statusStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
            }
            else if (row.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
            {
                statusStyle.normal.textColor = new Color(0.5f, 0.95f, 0.55f);
            }

            GUI.Label(new Rect(x, rect.y + 2f, 120f, rect.height), row.Status.ToString(), statusStyle);
            x += 122f;

            GUI.Label(new Rect(x, rect.y + 2f, 280f, rect.height), row.Source, EditorStyles.label);
            x += 282f;
            GUI.Label(new Rect(x, rect.y + 2f, 90f, rect.height), row.ElapsedMilliseconds.ToString("0.0"), EditorStyles.label);
            x += 92f;
            GUI.Label(new Rect(x, rect.y + 2f, 170f, rect.height), row.StartedAtUtc.ToString("HH:mm:ss.fff"), EditorStyles.label);
            x += 172f;
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), row.ErrorMessage, EditorStyles.label);
        }

        private void RefreshRows()
        {
            OnityTaskTracker.GetSnapshot(m_rows, m_includeCompleted);
            m_rows.Sort((left, right) => right.TaskId.CompareTo(left.TaskId));
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

        private bool IsVisible(OnityTrackedTaskInfo row)
        {
            if (string.IsNullOrWhiteSpace(m_searchText))
            {
                return true;
            }

            return ContainsIgnoreCase(row.Source, m_searchText)
                || ContainsIgnoreCase(row.Status.ToString(), m_searchText)
                || ContainsIgnoreCase(row.ErrorMessage, m_searchText)
                || ContainsIgnoreCase(row.StackTrace, m_searchText)
                || row.TaskId.ToString().IndexOf(m_searchText, StringComparison.OrdinalIgnoreCase) >= 0;
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
