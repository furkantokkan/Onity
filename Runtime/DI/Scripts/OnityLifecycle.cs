namespace Onity.DI
{
    /// <summary>
    /// Implement on a singleton (or bound instance) to have the container call
    /// <see cref="Initialize" /> automatically once, at the end of
    /// <see cref="OnityContainer.Build" />, in binding-registration order. No manual
    /// entry-point registration is needed: binding the type is enough. This is the
    /// "automatic like Zenject" lifecycle - unlike containers that require an
    /// explicit register-entry-point call, here a bound type that implements this
    /// interface is wired up by the container itself.
    /// </summary>
    public interface IOnityInitializable
    {
        /// <summary>
        /// Runs once, after the owning container finishes <see cref="OnityContainer.Build" />.
        /// All dependencies are resolvable at this point.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// Implement on a singleton (or bound instance) to be ticked every frame. The
    /// owning Unity context pumps <see cref="OnityContainer.Tick" /> from
    /// <c>Update</c>; binding the type is enough to be collected - no manual
    /// registration. Transient bindings are not ticked (there is no single stable
    /// instance to tick).
    /// </summary>
    public interface IOnityTickable
    {
        /// <summary>Runs once per frame from the context's <c>Update</c>.</summary>
        void Tick();
    }

    /// <summary>
    /// Implement on a singleton (or bound instance) to be ticked every physics step.
    /// The owning Unity context pumps <see cref="OnityContainer.FixedTick" /> from
    /// <c>FixedUpdate</c>.
    /// </summary>
    public interface IOnityFixedTickable
    {
        /// <summary>Runs once per physics step from the context's <c>FixedUpdate</c>.</summary>
        void FixedTick();
    }

    /// <summary>
    /// Implement on a singleton (or bound instance) to be ticked late each frame,
    /// after all <see cref="IOnityTickable" /> work. The owning Unity context pumps
    /// <see cref="OnityContainer.LateTick" /> from <c>LateUpdate</c>.
    /// </summary>
    public interface IOnityLateTickable
    {
        /// <summary>Runs once per frame from the context's <c>LateUpdate</c>.</summary>
        void LateTick();
    }
}
