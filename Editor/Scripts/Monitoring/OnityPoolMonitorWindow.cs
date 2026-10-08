using System;
using System.Collections.Generic;
using Onity.Pooling;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// Editor window that monitors registered Onity pools.
    /// </summary>
    public sealed class OnityPoolMonitorWindow : EditorWindow
    {
        private const string k_windowTitle = "Onity Pool Monitor";
        private const double k_refreshIntervalSeconds = 0.5d;

        private readonly List<OnityPoolDiagnosticsSnapshot> m_rows = new List<OnityPoolDiagnosticsSnapshot>(128);
        private Vector2 m_scrollPosition;
        private bool m_autoRefresh = true;
        private string m_searchText = string.Empty;
        private double m_nextRefreshTime;

        [MenuItem("Onity/Tools/Pool Monitor", false, 121)]
        private static void OpenWindow()
        {
            OnityPoolMonitorWindow window = GetWindow<OnityPoolMonitorWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(980f, 320f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
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

            bool nextAutoRefresh = GUILayout.Toggle(m_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50f));

            if (nextAutoRefresh != m_autoRefresh)
            {
                m_autoRefresh = nextAutoRefresh;
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                RefreshRows();
            }

            GUILayout.Space(8f);

            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.textField;
            string nextSearch = GUILayout.TextField(
                m_searchText,
                searchStyle,
                GUILayout.MinWidth(180f),
                GUILayout.MaxWidth(300f));

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
            GUILayout.Label($"Pools: {GetVisibleCount()}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private static void DrawHeader()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 0.35f));

            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y + 2f, 250f, rect.height), "Pool", EditorStyles.boldLabel);
            x += 252f;
            GUI.Label(new Rect(x, rect.y + 2f, 180f, rect.height), "Item", EditorStyles.boldLabel);
            x += 182f;
            GUI.Label(new Rect(x, rect.y + 2f, 45f, rect.height), "All", EditorStyles.boldLabel);
            x += 47f;
            GUI.Label(new Rect(x, rect.y + 2f, 55f, rect.height), "Active", EditorStyles.boldLabel);
            x += 57f;
            GUI.Label(new Rect(x, rect.y + 2f, 60f, rect.height), "Inactive", EditorStyles.boldLabel);
            x += 62f;
            GUI.Label(new Rect(x, rect.y + 2f, 70f, rect.height), "Gets", EditorStyles.boldLabel);
            x += 72f;
            GUI.Label(new Rect(x, rect.y + 2f, 70f, rect.height), "Releases", EditorStyles.boldLabel);
            x += 72f;
            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), "State", EditorStyles.boldLabel);
        }

        private void DrawRows()
        {
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            int visibleIndex = 0;

            for (int i = 0; i < m_rows.Count; i++)
            {
                OnityPoolDiagnosticsSnapshot row = m_rows[i];

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
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawDataRow(Rect rect, OnityPoolDiagnosticsSnapshot row)
        {
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y + 2f, 250f, rect.height), row.PoolName, EditorStyles.label);
            x += 252f;
            GUI.Label(new Rect(x, rect.y + 2f, 180f, rect.height), row.ItemType, EditorStyles.label);
            x += 182f;
            GUI.Label(new Rect(x, rect.y + 2f, 45f, rect.height), row.CountAll.ToString(), EditorStyles.label);
            x += 47f;
            GUI.Label(new Rect(x, rect.y + 2f, 55f, rect.height), row.CountActive.ToString(), EditorStyles.label);
            x += 57f;
            GUI.Label(new Rect(x, rect.y + 2f, 60f, rect.height), row.CountInactive.ToString(), EditorStyles.label);
            x += 62f;
            GUI.Label(new Rect(x, rect.y + 2f, 70f, rect.height), row.GetCount.ToString(), EditorStyles.label);
            x += 72f;
            GUI.Label(new Rect(x, rect.y + 2f, 70f, rect.height), row.ReleaseCount.ToString(), EditorStyles.label);
            x += 72f;

            GUIStyle stateStyle = new GUIStyle(EditorStyles.label);

            if (row.IsDisposed)
            {
                stateStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
            }

            GUI.Label(new Rect(x, rect.y + 2f, rect.width - x, rect.height), row.IsDisposed ? "Disposed" : "Alive", stateStyle);
        }

        private void RefreshRows()
        {
            OnityPoolDiagnosticsRegistry.GetSnapshots(m_rows);
            m_rows.Sort((left, right) => string.Compare(left.PoolName, right.PoolName, StringComparison.OrdinalIgnoreCase));
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

        private bool IsVisible(OnityPoolDiagnosticsSnapshot row)
        {
            if (string.IsNullOrWhiteSpace(m_searchText))
            {
                return true;
            }

            return ContainsIgnoreCase(row.PoolName, m_searchText)
                || ContainsIgnoreCase(row.ItemType, m_searchText)
                || ContainsIgnoreCase(row.PoolType, m_searchText);
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
