using System;
using System.Collections.Generic;
using System.Reflection;
using Onity.DI;
using Onity.Unity.Contexts;
using UnityEditor;
using UnityEngine;

namespace Onity.Editor.Monitoring
{
    /// <summary>
    /// Read-only inspector panel that shows [Inject] requests and binding source status.
    /// </summary>
    [InitializeOnLoad]
    public static class OnityInjectInspectorPanel
    {
        private const string k_foldoutEditorPrefKey = "Onity.InjectInspectorPanel.Foldout";
        private const float k_memberColumnWidth = 210f;
        private const float k_dependencyColumnWidth = 250f;
        private const float k_statusColumnWidth = 84f;

        private static readonly Dictionary<Type, InjectDependencyRequest[]> s_requestCache =
            new Dictionary<Type, InjectDependencyRequest[]>(128);

        static OnityInjectInspectorPanel()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI -= DrawPanel;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawPanel;
        }

        private static void DrawPanel(UnityEditor.Editor editor)
        {
            if (editor == null || editor.targets == null || editor.targets.Length != 1)
            {
                return;
            }

            MonoBehaviour behaviour = editor.target as MonoBehaviour;

            if (behaviour == null || EditorUtility.IsPersistent(behaviour))
            {
                return;
            }

            InjectDependencyRequest[] requests = GetOrCreateRequests(behaviour.GetType());

            if (requests.Length == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            bool isExpanded = EditorPrefs.GetBool(k_foldoutEditorPrefKey, true);
            bool nextExpanded = EditorGUILayout.Foldout(isExpanded, "Inject (Read Only)", true);

            if (nextExpanded != isExpanded)
            {
                EditorPrefs.SetBool(k_foldoutEditorPrefKey, nextExpanded);
                isExpanded = nextExpanded;
            }

            if (isExpanded)
            {
                DrawContent(behaviour, requests);
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawContent(MonoBehaviour behaviour, InjectDependencyRequest[] requests)
        {
            OnityContext context = ResolveContext(behaviour);
            DrawContextHeader(context);
            DrawTableHeader();

            for (int i = 0; i < requests.Length; i++)
            {
                InjectDependencyRequest request = requests[i];
                ResolveStatus resolveStatus = BuildResolveStatus(context, request.DependencyType);
                DrawTableRow(request, resolveStatus);
            }
        }

        private static void DrawContextHeader(OnityContext context)
        {
            if (context == null)
            {
                EditorGUILayout.HelpBox(
                    "No OnityContext found for this object. Injection source cannot be resolved.",
                    MessageType.Warning);
                return;
            }

            string contextLabel = BuildContextLabel(context);
            EditorGUILayout.LabelField("Context", contextLabel, EditorStyles.miniLabel);

            if (context.Container == null)
            {
                EditorGUILayout.HelpBox(
                    "Container is not initialized yet. Enter Play Mode to see runtime resolve status.",
                    MessageType.Info);
            }
        }

        private static void DrawTableHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Member", EditorStyles.miniBoldLabel, GUILayout.Width(k_memberColumnWidth));
            GUILayout.Label("Dependency", EditorStyles.miniBoldLabel, GUILayout.Width(k_dependencyColumnWidth));
            GUILayout.Label("Status", EditorStyles.miniBoldLabel, GUILayout.Width(k_statusColumnWidth));
            GUILayout.Label("Resolved By", EditorStyles.miniBoldLabel);
            GUILayout.EndHorizontal();
        }

        private static void DrawTableRow(InjectDependencyRequest request, ResolveStatus resolveStatus)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(request.MemberName, EditorStyles.miniLabel, GUILayout.Width(k_memberColumnWidth));
            GUILayout.Label(GetFriendlyTypeName(request.DependencyType), EditorStyles.miniLabel, GUILayout.Width(k_dependencyColumnWidth));

            Color previousColor = GUI.color;
            GUI.color = resolveStatus.IsResolved ? new Color(0.45f, 0.9f, 0.45f, 1f) : new Color(1f, 0.55f, 0.55f, 1f);
            GUILayout.Label(resolveStatus.IsResolved ? "Resolved" : "Missing", EditorStyles.miniLabel, GUILayout.Width(k_statusColumnWidth));
            GUI.color = previousColor;

            GUILayout.Label(resolveStatus.SourceName, EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        private static ResolveStatus BuildResolveStatus(OnityContext context, Type dependencyType)
        {
            if (context == null || context.Container == null)
            {
                return new ResolveStatus(false, "Container not available.");
            }

            OnityContainer container = context.Container;

            if (container.CanResolve(dependencyType) == false)
            {
                return new ResolveStatus(false, "No matching binding found.");
            }

            if (container.TryGetBindingSource(dependencyType, out OnityBindingSourceInfo sourceInfo))
            {
                string sourceName = sourceInfo.SourceName;

                if (sourceInfo.ScopeDepth > 0)
                {
                    sourceName = $"{sourceName} | Parent Scope +{sourceInfo.ScopeDepth}";
                }

                return new ResolveStatus(true, sourceName);
            }

            return new ResolveStatus(true, "Implicit (Concrete auto-resolve)");
        }

        private static InjectDependencyRequest[] GetOrCreateRequests(Type targetType)
        {
            if (targetType == null)
            {
                return Array.Empty<InjectDependencyRequest>();
            }

            if (s_requestCache.TryGetValue(targetType, out InjectDependencyRequest[] cached))
            {
                return cached;
            }

            List<InjectDependencyRequest> requests = new List<InjectDependencyRequest>(8);
            HashSet<string> dedupe = new HashSet<string>(StringComparer.Ordinal);

            for (Type current = targetType; current != null && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                CollectFields(current, requests, dedupe);
                CollectProperties(current, requests, dedupe);
                CollectMethods(current, requests, dedupe);

                if (current == typeof(Behaviour) || current == typeof(Component) || current == typeof(object))
                {
                    break;
                }
            }

            InjectDependencyRequest[] created = requests.ToArray();
            s_requestCache[targetType] = created;
            return created;
        }

        private static void CollectFields(Type type, List<InjectDependencyRequest> requests, HashSet<string> dedupe)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo fieldInfo = fields[i];

                if (fieldInfo.IsDefined(typeof(InjectAttribute), false) == false)
                {
                    continue;
                }

                string memberName = $"Field: {fieldInfo.Name}";
                string key = $"{memberName}|{fieldInfo.FieldType.FullName}";

                if (dedupe.Add(key))
                {
                    requests.Add(new InjectDependencyRequest(memberName, fieldInfo.FieldType));
                }
            }
        }

        private static void CollectProperties(Type type, List<InjectDependencyRequest> requests, HashSet<string> dedupe)
        {
            PropertyInfo[] properties =
                type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo propertyInfo = properties[i];

                if (propertyInfo.IsDefined(typeof(InjectAttribute), false) == false)
                {
                    continue;
                }

                MethodInfo setMethod = propertyInfo.GetSetMethod(true);

                if (setMethod == null)
                {
                    continue;
                }

                string memberName = $"Property: {propertyInfo.Name}";
                string key = $"{memberName}|{propertyInfo.PropertyType.FullName}";

                if (dedupe.Add(key))
                {
                    requests.Add(new InjectDependencyRequest(memberName, propertyInfo.PropertyType));
                }
            }
        }

        private static void CollectMethods(Type type, List<InjectDependencyRequest> requests, HashSet<string> dedupe)
        {
            MethodInfo[] methods =
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo methodInfo = methods[i];

                if (methodInfo.IsDefined(typeof(InjectAttribute), false) == false)
                {
                    continue;
                }

                ParameterInfo[] parameters = methodInfo.GetParameters();

                for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                {
                    ParameterInfo parameterInfo = parameters[parameterIndex];
                    string memberName = $"Method: {methodInfo.Name}({parameterInfo.Name})";
                    string key = $"{memberName}|{parameterInfo.ParameterType.FullName}";

                    if (dedupe.Add(key))
                    {
                        requests.Add(new InjectDependencyRequest(memberName, parameterInfo.ParameterType));
                    }
                }
            }
        }

        private static OnityContext ResolveContext(MonoBehaviour behaviour)
        {
            if (behaviour == null)
            {
                return null;
            }

            Transform current = behaviour.transform;

            while (current != null)
            {
                if (current.TryGetComponent(out OnityContext hierarchyContext))
                {
                    return hierarchyContext;
                }

                current = current.parent;
            }

            if (behaviour.gameObject.scene.IsValid())
            {
                OnityContext[] contexts = UnityEngine.Object.FindObjectsByType<OnityContext>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

                OnityContext fallbackSceneContext = null;

                for (int i = 0; i < contexts.Length; i++)
                {
                    OnityContext context = contexts[i];

                    if (context == null || EditorUtility.IsPersistent(context))
                    {
                        continue;
                    }

                    if (context.gameObject.scene != behaviour.gameObject.scene)
                    {
                        continue;
                    }

                    if (context is SceneContext)
                    {
                        return context;
                    }

                    if (fallbackSceneContext == null)
                    {
                        fallbackSceneContext = context;
                    }
                }

                if (fallbackSceneContext != null)
                {
                    return fallbackSceneContext;
                }
            }

            return ProjectContext.Instance;
        }

        private static string BuildContextLabel(OnityContext context)
        {
            if (context == null)
            {
                return "<none>";
            }

            string sceneName = context.gameObject.scene.IsValid() ? context.gameObject.scene.name : "NoScene";
            string hierarchyPath = BuildHierarchyPath(context.transform);
            return $"{sceneName}/{hierarchyPath} ({context.GetType().Name})";
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "<null>";
            }

            if (type.IsGenericType == false)
            {
                return type.FullName ?? type.Name;
            }

            Type[] genericArguments = type.GetGenericArguments();
            string typeName = type.Name;
            int tickIndex = typeName.IndexOf('`');

            if (tickIndex >= 0)
            {
                typeName = typeName.Substring(0, tickIndex);
            }

            string[] argumentNames = new string[genericArguments.Length];

            for (int i = 0; i < genericArguments.Length; i++)
            {
                argumentNames[i] = GetFriendlyTypeName(genericArguments[i]);
            }

            return $"{type.Namespace}.{typeName}<{string.Join(", ", argumentNames)}>";
        }

        private readonly struct InjectDependencyRequest
        {
            public readonly string MemberName;
            public readonly Type DependencyType;

            public InjectDependencyRequest(string memberName, Type dependencyType)
            {
                MemberName = memberName;
                DependencyType = dependencyType;
            }
        }

        private readonly struct ResolveStatus
        {
            public readonly bool IsResolved;
            public readonly string SourceName;

            public ResolveStatus(bool isResolved, string sourceName)
            {
                IsResolved = isResolved;
                SourceName = sourceName ?? string.Empty;
            }
        }
    }
}
