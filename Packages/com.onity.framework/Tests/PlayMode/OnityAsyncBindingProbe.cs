using System.Collections.Generic;
using UnityEngine;

namespace Onity.Tests.PlayMode
{
    /// <summary>Runtime test component that records the values bound to it.</summary>
    public sealed class OnityAsyncBindingProbe : MonoBehaviour
    {
        /// <summary>Values applied by a binding, in order.</summary>
        public readonly List<int> Values = new List<int>();
    }
}
