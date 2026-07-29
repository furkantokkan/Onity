using UnityEngine;

namespace Onity.Unity.Contexts
{
    /// <summary>
    /// Scene-level context that usually inherits from <see cref="ProjectContext" />.
    /// </summary>
    [DefaultExecutionOrder(-9800)]
    public sealed class SceneContext : OnityContext
    {
        [Tooltip("Optional explicit project context reference.")]
        [SerializeField] private ProjectContext m_projectContext;

        /// <inheritdoc />
        protected override OnityContext ResolveDefaultParentContext()
        {
            if (m_projectContext != null)
            {
                return m_projectContext;
            }

            return ProjectContext.Instance;
        }
    }
}
