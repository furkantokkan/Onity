using Onity.Unity.Contexts;
using Onity.Unity.Installers;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Onity.Editor.Contexts
{
    /// <summary>
    /// Custom inspector for Onity contexts with installer-chain tooltips.
    /// </summary>
    [CustomEditor(typeof(OnityContext), true)]
    public sealed class OnityContextEditor : UnityEditor.Editor
    {
        private const string k_installersPropertyName = "m_installers";
        private const string k_parentContextPropertyName = "m_parentContext";
        private const string k_autoInjectPropertyName = "m_autoInjectHierarchy";
        private const string k_asyncBuildPropertyName = "m_runAsyncBuildCallbacks";

        private SerializedProperty m_scriptProperty;
        private SerializedProperty m_installersProperty;
        private SerializedProperty m_parentContextProperty;
        private SerializedProperty m_autoInjectProperty;
        private SerializedProperty m_asyncBuildProperty;
        private ReorderableList m_installersList;

        private void OnEnable()
        {
            m_scriptProperty = serializedObject.FindProperty("m_Script");
            m_installersProperty = serializedObject.FindProperty(k_installersPropertyName);
            m_parentContextProperty = serializedObject.FindProperty(k_parentContextPropertyName);
            m_autoInjectProperty = serializedObject.FindProperty(k_autoInjectPropertyName);
            m_asyncBuildProperty = serializedObject.FindProperty(k_asyncBuildPropertyName);
            BuildInstallersList();
        }

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptField();
            EditorGUILayout.Space(3f);
            DrawContextSetupSection();
            DrawDerivedProperties();

            serializedObject.ApplyModifiedProperties();
        }

        private void BuildInstallersList()
        {
            if (m_installersProperty == null)
            {
                return;
            }

            m_installersList = new ReorderableList(
                serializedObject,
                m_installersProperty,
                true,
                true,
                true,
                true);

            m_installersList.drawHeaderCallback = DrawInstallerHeader;
            m_installersList.drawElementCallback = DrawInstallerElement;
            m_installersList.elementHeightCallback = GetInstallerElementHeight;
        }

        private void DrawScriptField()
        {
            if (m_scriptProperty == null)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(m_scriptProperty);
            }
        }

        private void DrawContextSetupSection()
        {
            EditorGUILayout.LabelField("Context Setup", EditorStyles.boldLabel);

            if (m_installersList != null)
            {
                m_installersList.DoLayoutList();
            }
            else if (m_installersProperty != null)
            {
                EditorGUILayout.PropertyField(
                    m_installersProperty,
                    new GUIContent(
                        "Installer Chain",
                        "Installers execute in listed order during context Awake."));
            }

            if (m_parentContextProperty != null)
            {
                EditorGUILayout.PropertyField(
                    m_parentContextProperty,
                    new GUIContent(
                        "Parent Context",
                        "Optional explicit parent context. Leave empty to use automatic lookup."));
            }

            if (m_autoInjectProperty != null)
            {
                EditorGUILayout.PropertyField(
                    m_autoInjectProperty,
                    new GUIContent(
                        "Auto Inject Hierarchy",
                        "Injects all MonoBehaviours under this context root after installer execution."));
            }

            if (m_asyncBuildProperty != null)
            {
                EditorGUILayout.PropertyField(
                    m_asyncBuildProperty,
                    new GUIContent(
                        "Run Async Build Callbacks",
                        "Executes async post-build lifecycle callbacks after initial setup."));
            }
        }

        private void DrawDerivedProperties()
        {
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                k_installersPropertyName,
                k_parentContextPropertyName,
                k_autoInjectPropertyName,
                k_asyncBuildPropertyName);
        }

        private void DrawInstallerHeader(Rect rect)
        {
            EditorGUI.LabelField(
                rect,
                new GUIContent(
                    "Installer Chain (Awake Order)",
                    "Each step runs top-to-bottom during context Awake before hierarchy auto-injection."));
        }

        private void DrawInstallerElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (m_installersProperty == null || index < 0 || index >= m_installersProperty.arraySize)
            {
                return;
            }

            SerializedProperty installerProperty = m_installersProperty.GetArrayElementAtIndex(index);
            MonoInstaller installer = installerProperty.objectReferenceValue as MonoInstaller;
            GUIContent label = new GUIContent($"Step {index + 1}", BuildInstallerTooltip(installer, index));

            rect.y += 1f;
            rect.height = EditorGUI.GetPropertyHeight(installerProperty, true);
            EditorGUI.PropertyField(rect, installerProperty, label, true);
        }

        private float GetInstallerElementHeight(int index)
        {
            if (m_installersProperty == null || index < 0 || index >= m_installersProperty.arraySize)
            {
                return EditorGUIUtility.singleLineHeight + 4f;
            }

            SerializedProperty installerProperty = m_installersProperty.GetArrayElementAtIndex(index);
            return EditorGUI.GetPropertyHeight(installerProperty, true) + 4f;
        }

        private static string BuildInstallerTooltip(MonoInstaller installer, int index)
        {
            int stepNumber = index + 1;

            if (installer == null)
            {
                return $"Step {stepNumber}: Empty installer slot. Empty steps are skipped at runtime.";
            }

            string sceneName = installer.gameObject.scene.IsValid()
                ? installer.gameObject.scene.name
                : "NoScene";

            return
                $"Step {stepNumber}: {installer.GetType().Name}\n" +
                $"GameObject: {GetHierarchyPath(installer.transform)}\n" +
                $"Scene: {sceneName}\n" +
                "Execution: Runs during context Awake in listed order.";
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
    }
}
