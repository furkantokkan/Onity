#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;

namespace Onity.Unity.Input
{
    /// <summary>
    /// Zero-allocation action key for map/action lookups.
    /// </summary>
    public readonly struct OnityInputActionKey : IEquatable<OnityInputActionKey>
    {
        private static readonly StringComparer s_stringComparer = StringComparer.Ordinal;

        private readonly int m_hashCode;

        /// <summary>
        /// Action map name.
        /// </summary>
        public string ActionMapName { get; }

        /// <summary>
        /// Action name.
        /// </summary>
        public string ActionName { get; }

        /// <summary>
        /// Initializes a new action key.
        /// </summary>
        /// <param name="actionMapName">Action map name.</param>
        /// <param name="actionName">Action name.</param>
        public OnityInputActionKey(string actionMapName, string actionName)
        {
            ActionMapName = actionMapName ?? string.Empty;
            ActionName = actionName ?? string.Empty;
            m_hashCode = HashCode.Combine(
                s_stringComparer.GetHashCode(ActionMapName),
                s_stringComparer.GetHashCode(ActionName));
        }

        /// <inheritdoc />
        public bool Equals(OnityInputActionKey other)
        {
            return s_stringComparer.Equals(ActionMapName, other.ActionMapName)
                   && s_stringComparer.Equals(ActionName, other.ActionName);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is OnityInputActionKey other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_hashCode;
        }
    }
}
#endif
