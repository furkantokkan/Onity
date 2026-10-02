using System.Threading;
using System.Threading.Tasks;

namespace Onity.DI
{
    /// <summary>
    /// Implement on a singleton, local scoped binding, or bound instance to have the container run
    /// <see cref="InitializeAsync" /> once during <see cref="OnityContainer.BuildAsync" />. Binding the
    /// type is enough; it is collected at <see cref="OnityContainer.Build" /> exactly like
    /// <see cref="IOnityInitializable" />. Transient bindings are not collected.
    /// </summary>
    /// <remarks>
    /// <see cref="OnityContainer.BuildAsync" /> runs the synchronous <c>Initialize</c> entry points
    /// (inside <c>Build</c>), then the async build callbacks, then each async initializer one at a
    /// time in binding-registration order, awaiting each before starting the next. Each initializer
    /// resumes on the context the build started on (Unity's main thread in Play Mode). A Unity
    /// context reports <c>IsReady</c> only after every async initializer completed.
    /// </remarks>
    public interface IOnityAsyncInitializable
    {
        /// <summary>
        /// Runs once, after the synchronous initializers and the async build callbacks.
        /// </summary>
        /// <param name="cancellationToken">Canceled when the owning scope is disposed or the
        /// <c>BuildAsync</c> caller cancels.</param>
        /// <returns>A task that completes when initialization finishes. A fault or cancellation ends
        /// <c>BuildAsync</c>; the next <c>BuildAsync</c> call runs this initializer again, while
        /// initializers that already completed do not run again.</returns>
        ValueTask InitializeAsync(CancellationToken cancellationToken);
    }
}
