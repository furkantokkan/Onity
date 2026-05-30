using UnityEngine;

namespace Onity.Unity.Contexts
{
    /// <summary>
    /// GameObject-scoped context for isolated object graphs.
    /// </summary>
    [DefaultExecutionOrder(-9700)]
    public sealed class GameObjectContext : OnityContext
    {
        [Tooltip("Searches parent hierarchy for nearest OnityContext when no explicit parent is set.")]
        [SerializeField] private bool m_findParentInHierarchy = true;

        [Tooltip("Falls back to ProjectContext when no parent context exists in hierarchy.")]
        [SerializeField] private bool m_fallbackToProjectContext = true;

        /// <inheritdoc />
        protected override OnityContext ResolveDefaultParentContext()
        {
            if (m_findParentInHierarchy)
            {
                Transform current = transform.parent;

                while (current != null)
                {
                    if (current.TryGetComponent(out OnityContext parentContext))
                    {
                        return parentContext;
                    }

                    current = current.parent;
                }
            }

            if (m_fallbackToProjectContext)
            {
                return ProjectContext.Instance;
            }

            return null;
        }
    }
}
