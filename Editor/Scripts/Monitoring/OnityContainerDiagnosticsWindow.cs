using System;
using System.Collections.Generic;
using System.Text;
using Onity.DI;
using Onity.Unity.Contexts;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// Container diagnostics window inspired by VContainer diagnostics layout.
    /// </summary>
    public sealed class OnityContainerDiagnosticsWindow : EditorWindow
    {
        private const string k_windowTitle = "Onity Container Diagnostics";
        private const string k_collectMetricsEditorPrefKey = "Onity.Diagnostics.CollectMetrics";
        private const double k_autoRefreshIntervalSeconds = 1.0d;

        private readonly List<BindingRow> m_rows = new List<BindingRow>(256);
        private readonly List<OnityBindingDiagnostics> m_bindingBuffer = new List<OnityBindingDiagnostics>(64);
        private readonly Dictionary<string, FlattenAggregation> m_flattenMap = new Dictionary<string, FlattenAggregation>(256);

        private Vector2 m_scrollPosition;
        private bool m_flatten = true;
        private bool m_autoRefresh = true;
        private string m_searchText = string.Empty;
        private double m_nextRefreshTime;

        [MenuItem("Onity/Tools/Container Diagnostics", false, 101)]
        private static void OpenWindow()
        {
            OnityContainerDiagnosticsWindow window = GetWindow<OnityContainerDiagnosticsWindow>();
            window.titleContent = new GUIContent(k_windowTitle);
            window.minSize = new Vector2(1100f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            bool collectMetrics = EditorPrefs.GetBool(k_collectMetricsEditorPrefKey, false);
            OnityContainer.DiagnosticsCollectionEnabled = collectMetrics;
            EditorApplication.update += OnEditorUpdate;
            Reload();
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

            m_nextRefreshTime = EditorApplication.timeSinceStartup + k_autoRefreshIntervalSeconds;
            Reload();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawStatus();
            DrawHeaderRow();
            DrawRows();
        }

        private void DrawToolbar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool nextFlatten = GUILayout.Toggle(m_flatten, "Flatten", EditorStyles.toolbarButton, GUILayout.Width(70f));

            if (nextFlatten != m_flatten)
            {
                m_flatten = nextFlatten;
                Reload();
            }

            bool nextAutoRefresh = GUILayout.Toggle(m_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(54f));

            if (nextAutoRefresh != m_autoRefresh)
            {
                m_autoRefresh = nextAutoRefresh;
            }

            bool nextCollectMetrics = GUILayout.Toggle(
                OnityContainer.DiagnosticsCollectionEnabled,
                "Collect Metrics",
                EditorStyles.toolbarButton,
                GUILayout.Width(105f));

            if (nextCollectMetrics != OnityContainer.DiagnosticsCollectionEnabled)
            {
                OnityContainer.DiagnosticsCollectionEnabled = nextCollectMetrics;
                EditorPrefs.SetBool(k_collectMetricsEditorPrefKey, nextCollectMetrics);
                Reload();
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                Reload();
            }

            GUILayout.Space(8f);

            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.textField;
            string nextSearchText = GUILayout.TextField(m_searchText, searchStyle, GUILayout.MinWidth(180f), GUILayout.MaxWidth(320f));

            if (string.Equals(nextSearchText, m_searchText, StringComparison.Ordinal) == false)
            {
                m_searchText = nextSearchText;
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
            GUILayout.Label($"Rows: {GetFilteredRowCount()}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            if (OnityContainer.DiagnosticsCollectionEnabled)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Onity diagnostics collector is disabled. Enable 'Collect Metrics' to populate RefCount and Time columns.",
                MessageType.Info);
        }

        private void DrawHeaderRow()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            DrawRowBackground(rect, true);

            float x = rect.x;
            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.Type), rect.height), "Type", true);
            x += GetColumnWidth(rect.width, Column.Type);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.ContractTypes), rect.height), "ContractTypes", true);
            x += GetColumnWidth(rect.width, Column.ContractTypes);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.Lifetime), rect.height), "Lifetime", true);
            x += GetColumnWidth(rect.width, Column.Lifetime);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.Register), rect.height), "Register", true);
            x += GetColumnWidth(rect.width, Column.Register);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.RefCount), rect.height), "RefCount", true);
            x += GetColumnWidth(rect.width, Column.RefCount);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.Scope), rect.height), "Scope", true);
            x += GetColumnWidth(rect.width, Column.Scope);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rect.width, Column.Time), rect.height), "Time", true);
        }

        private void DrawRows()
        {
            m_scrollPosition = EditorGUILayout.BeginScrollView(m_scrollPosition);
            float rowWidth = EditorGUIUtility.currentViewWidth - 24f;
            int visibleIndex = 0;

            for (int i = 0; i < m_rows.Count; i++)
            {
                BindingRow row = m_rows[i];

                if (IsVisible(row) == false)
                {
                    continue;
                }

                Rect rect = EditorGUILayout.GetControlRect(false, 20f);
                DrawRowBackground(rect, visibleIndex % 2 == 0);
                DrawDataRow(rect, row, rowWidth);
                visibleIndex++;
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDataRow(Rect rect, BindingRow row, float rowWidth)
        {
            float x = rect.x;
            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.Type), rect.height), row.TypeName, false);
            x += GetColumnWidth(rowWidth, Column.Type);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.ContractTypes), rect.height), row.ContractTypes, false);
            x += GetColumnWidth(rowWidth, Column.ContractTypes);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.Lifetime), rect.height), row.Lifetime, false);
            x += GetColumnWidth(rowWidth, Column.Lifetime);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.Register), rect.height), row.Register, false);
            x += GetColumnWidth(rowWidth, Column.Register);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.RefCount), rect.height), row.RefCount, false);
            x += GetColumnWidth(rowWidth, Column.RefCount);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.Scope), rect.height), row.Scope, false);
            x += GetColumnWidth(rowWidth, Column.Scope);

            DrawCell(new Rect(x, rect.y, GetColumnWidth(rowWidth, Column.Time), rect.height), row.Time, false);
        }

        private void Reload()
        {
            m_rows.Clear();
            m_flattenMap.Clear();

            OnityContext[] contexts = UnityEngine.Object.FindObjectsByType<OnityContext>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < contexts.Length; i++)
            {
                OnityContext context = contexts[i];

                if (context == null || EditorUtility.IsPersistent(context))
                {
                    continue;
                }

                OnityContainer container = context.Container;

                if (container == null)
                {
                    continue;
                }

                string scopeName = BuildScopeName(context);
                m_bindingBuffer.Clear();

                try
                {
                    container.GetBindingDiagnostics(m_bindingBuffer);
                }
                catch
                {
                    continue;
                }

                for (int rowIndex = 0; rowIndex < m_bindingBuffer.Count; rowIndex++)
                {
                    OnityBindingDiagnostics diagnostics = m_bindingBuffer[rowIndex];
                    AddRow(scopeName, diagnostics);
                }
            }

            if (m_flatten)
            {
                FlushFlattenRows();
            }

            m_rows.Sort(CompareRows);
        }

        private void AddRow(string scopeName, OnityBindingDiagnostics diagnostics)
        {
            string typeName = diagnostics.ImplementationType != null
                ? diagnostics.ImplementationType.FullName
                : "<null>";
            string contractNames = BuildContractNames(diagnostics.ContractTypes);
            string register = diagnostics.IsImplicitRegistration ? "Implicit" : "Explicit";
            string refCount = diagnostics.ResolveCount.ToString();
            string time = FormatTime(diagnostics.AverageResolveMilliseconds, diagnostics.LastResolveMilliseconds);

            if (m_flatten)
            {
                string flattenKey = BuildFlattenKey(typeName, contractNames, diagnostics.Lifetime, register);

                if (m_flattenMap.TryGetValue(flattenKey, out FlattenAggregation aggregation) == false)
                {
                    aggregation = new FlattenAggregation(typeName, contractNames, diagnostics.Lifetime, register);
                    m_flattenMap.Add(flattenKey, aggregation);
                }

                aggregation.Add(scopeName, diagnostics.ResolveCount, diagnostics.AverageResolveMilliseconds, diagnostics.LastResolveMilliseconds);
                return;
            }

            m_rows.Add(new BindingRow(typeName, contractNames, diagnostics.Lifetime, register, refCount, scopeName, time));
        }

        private void FlushFlattenRows()
        {
            foreach (FlattenAggregation aggregation in m_flattenMap.Values)
            {
                m_rows.Add(
                    new BindingRow(
                        aggregation.TypeName,
                        aggregation.ContractTypes,
                        aggregation.Lifetime,
                        aggregation.Register,
                        aggregation.ResolveCount.ToString(),
                        aggregation.ScopeLabel,
                        aggregation.TimeLabel));
            }
        }

        private static int CompareRows(BindingRow left, BindingRow right)
        {
            int scopeCompare = string.Compare(left.Scope, right.Scope, StringComparison.OrdinalIgnoreCase);

            if (scopeCompare != 0)
            {
                return scopeCompare;
            }

            return string.Compare(left.TypeName, right.TypeName, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsVisible(BindingRow row)
        {
            if (string.IsNullOrWhiteSpace(m_searchText))
            {
                return true;
            }

            return ContainsIgnoreCase(row.TypeName, m_searchText)
                || ContainsIgnoreCase(row.ContractTypes, m_searchText)
                || ContainsIgnoreCase(row.Scope, m_searchText)
                || ContainsIgnoreCase(row.Lifetime, m_searchText)
                || ContainsIgnoreCase(row.Register, m_searchText);
        }

        private int GetFilteredRowCount()
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

        private static bool ContainsIgnoreCase(string source, string search)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search))
            {
                return false;
            }

            return source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildScopeName(OnityContext context)
        {
            string sceneName = context.gameObject.scene.IsValid() ? context.gameObject.scene.name : "NoScene";
            return $"{sceneName}/{BuildHierarchyPath(context.transform)}";
        }

        private static string BuildHierarchyPath(Transform transform)
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

        private static string BuildContractNames(Type[] contractTypes)
        {
            if (contractTypes == null || contractTypes.Length == 0)
            {
                return "-";
            }

            StringBuilder builder = new StringBuilder(contractTypes.Length * 24);

            for (int i = 0; i < contractTypes.Length; i++)
            {
                Type contractType = contractTypes[i];

                if (i > 0)
                {
                    builder.Append(", ");
                }

                if (contractType == null)
                {
                    builder.Append("<null>");
                    continue;
                }

                builder.Append(contractType.FullName);
            }

            return builder.ToString();
        }

        private static string BuildFlattenKey(string typeName, string contractTypes, string lifetime, string register)
        {
            return $"{typeName}|{contractTypes}|{lifetime}|{register}";
        }

        private static string FormatTime(double averageMilliseconds, double lastMilliseconds)
        {
            return $"{averageMilliseconds:0.000} / {lastMilliseconds:0.000} ms";
        }

        private static void DrawRowBackground(Rect rect, bool even)
        {
            Color color = even
                ? new Color(0.16f, 0.16f, 0.16f, 0.25f)
                : new Color(0.08f, 0.08f, 0.08f, 0.15f);

            EditorGUI.DrawRect(rect, color);
        }

        private static void DrawCell(Rect rect, string text, bool isHeader)
        {
            GUIStyle style = isHeader ? EditorStyles.boldLabel : EditorStyles.label;
            Rect paddedRect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 6f, rect.height - 2f);
            GUI.Label(paddedRect, text, style);
        }

        private static float GetColumnWidth(float totalWidth, Column column)
        {
            return totalWidth * GetColumnRatio(column);
        }

        private static float GetColumnRatio(Column column)
        {
            switch (column)
            {
                case Column.Type:
                    return 0.16f;
                case Column.ContractTypes:
                    return 0.24f;
                case Column.Lifetime:
                    return 0.08f;
                case Column.Register:
                    return 0.08f;
                case Column.RefCount:
                    return 0.1f;
                case Column.Scope:
                    return 0.2f;
                case Column.Time:
                    return 0.14f;
                default:
                    return 0f;
            }
        }

        private enum Column
        {
            Type,
            ContractTypes,
            Lifetime,
            Register,
            RefCount,
            Scope,
            Time
        }

        private readonly struct BindingRow
        {
            public readonly string TypeName;
            public readonly string ContractTypes;
            public readonly string Lifetime;
            public readonly string Register;
            public readonly string RefCount;
            public readonly string Scope;
            public readonly string Time;

            public BindingRow(
                string typeName,
                string contractTypes,
                string lifetime,
                string register,
                string refCount,
                string scope,
                string time)
            {
                TypeName = typeName;
                ContractTypes = contractTypes;
                Lifetime = lifetime;
                Register = register;
                RefCount = refCount;
                Scope = scope;
                Time = time;
            }
        }

        private sealed class FlattenAggregation
        {
            private long m_weightedCount;
            private double m_weightedAverageSum;
            private readonly HashSet<string> m_scopes;

            public readonly string TypeName;
            public readonly string ContractTypes;
            public readonly string Lifetime;
            public readonly string Register;

            public long ResolveCount { get; private set; }
            public double LastMilliseconds { get; private set; }

            public string ScopeLabel => m_scopes.Count == 1
                ? FirstScope()
                : $"{m_scopes.Count} scopes";

            public string TimeLabel
            {
                get
                {
                    double averageMilliseconds = m_weightedCount > 0
                        ? m_weightedAverageSum / m_weightedCount
                        : 0d;

                    return FormatTime(averageMilliseconds, LastMilliseconds);
                }
            }

            public FlattenAggregation(string typeName, string contractTypes, string lifetime, string register)
            {
                TypeName = typeName;
                ContractTypes = contractTypes;
                Lifetime = lifetime;
                Register = register;
                ResolveCount = 0;
                LastMilliseconds = 0d;
                m_weightedCount = 0;
                m_weightedAverageSum = 0d;
                m_scopes = new HashSet<string>(StringComparer.Ordinal);
            }

            public void Add(string scopeName, long resolveCount, double averageMilliseconds, double lastMilliseconds)
            {
                if (string.IsNullOrEmpty(scopeName) == false)
                {
                    m_scopes.Add(scopeName);
                }

                ResolveCount += resolveCount;

                if (resolveCount > 0)
                {
                    m_weightedAverageSum += averageMilliseconds * resolveCount;
                    m_weightedCount += resolveCount;
                }

                if (lastMilliseconds > LastMilliseconds)
                {
                    LastMilliseconds = lastMilliseconds;
                }
            }

            private string FirstScope()
            {
                foreach (string scope in m_scopes)
                {
                    return scope;
                }

                return "-";
            }
        }
    }
}
