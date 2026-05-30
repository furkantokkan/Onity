#if ONITY_ENTITIES
using Unity.Entities;

namespace Onity.DOTS
{
    /// <summary>
    /// Early initialization group for Onity DOTS systems.
    /// </summary>
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial class OnityInitGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Late initialization group for Onity DOTS systems.
    /// </summary>
    [UpdateBefore(typeof(EndInitializationEntityCommandBufferSystem))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class OnityLateInitGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Primary simulation group for Onity DOTS systems.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class OnitySimulationGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Early simulation group for systems that must run before regular simulation.
    /// </summary>
    [UpdateBefore(typeof(BeginSimulationEntityCommandBufferSystem))]
    [UpdateInGroup(typeof(OnitySimulationGroup), OrderFirst = true)]
    public partial class OnityBeginSimulationGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Fixed-step simulation group for deterministic tick workflows.
    /// </summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial class OnityFixedStepGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Late simulation group for post-simulation operations.
    /// </summary>
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public partial class OnityLateSimulationGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Early presentation group for view sync and visualization updates.
    /// </summary>
    [UpdateBefore(typeof(BeginPresentationEntityCommandBufferSystem))]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class OnityBeginPresentationGroup : ComponentSystemGroup
    {
    }
}
#endif
