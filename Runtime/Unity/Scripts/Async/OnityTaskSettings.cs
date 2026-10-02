using Unity.IL2CPP.CompilerServices;

namespace Onity.Unity.Async
{
    /// <summary>
    /// Process-wide OnityTask settings behind the public <see cref="OnityTask"/> properties. They live
    /// here so that <see cref="OnityTask"/> and <see cref="OnityTask{T}"/> stay free of a static
    /// constructor. Hot paths read the fields directly without a fence: the settings are documented to
    /// be configured before async methods start, and the public setters publish with a volatile write.
    /// </summary>
    [Il2CppEagerStaticClassConstruction]
    internal static class OnityTaskSettings
    {
        internal const int k_defaultRunnerPoolCapacity = 128;
        internal const int k_defaultSourcePoolCapacity = 256;

        internal static bool s_flowExecutionContext;
        internal static int s_runnerPoolCapacity = k_defaultRunnerPoolCapacity;
        internal static int s_sourcePoolCapacity = k_defaultSourcePoolCapacity;
    }
}
