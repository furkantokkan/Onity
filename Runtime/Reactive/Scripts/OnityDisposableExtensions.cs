using System;

namespace Onity.Reactive
{
    /// <summary>
    /// Convenience extensions for disposable registration.
    /// </summary>
    public static class OnityDisposableExtensions
    {
        /// <summary>
        /// Adds a disposable to the provided composite collection.
        /// </summary>
        /// <param name="disposable">Disposable instance.</param>
        /// <param name="compositeDisposable">Composite collection.</param>
        /// <returns>Same disposable instance for fluent chaining.</returns>
        public static IDisposable AddTo(this IDisposable disposable, CompositeDisposable compositeDisposable)
        {
            if (disposable == null)
            {
                throw new ArgumentNullException(nameof(disposable));
            }

            if (compositeDisposable == null)
            {
                throw new ArgumentNullException(nameof(compositeDisposable));
            }

            compositeDisposable.Add(disposable);
            return disposable;
        }
    }
}
