using UnityEngine;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Scene bootstrap used by the IL2CPP benchmark build runner. A generated
    /// benchmark scene references this component so the player run does not
    /// depend on RuntimeInitializeOnLoad ordering or user project scenes.
    /// </summary>
    public sealed class OnityDiBenchmarkPlayerBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            OnityDiBenchmarkPlayerRunner.RunBenchmarkAndQuit();
        }
    }
}
