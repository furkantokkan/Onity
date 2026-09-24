using System;

namespace Onity.Unity.UI
{
    /// <summary>
    /// Presenter lifecycle abstraction for Onity UI modules.
    /// </summary>
    public interface IOnityUiPresenter : IDisposable
    {
        /// <summary>
        /// Assigns the current view instance.
        /// </summary>
        /// <param name="view">View instance.</param>
        void SetView(object view);

        /// <summary>
        /// Called before view open animation/state starts.
        /// </summary>
        void OnViewOpening();

        /// <summary>
        /// Called after view is fully opened.
        /// </summary>
        void OnViewOpened();

        /// <summary>
        /// Called before view close animation/state starts.
        /// </summary>
        void OnViewClosing();

        /// <summary>
        /// Called after view is fully closed.
        /// </summary>
        void OnViewClosed();
    }

    /// <summary>
    /// Typed presenter base class for quick UI MVP setup.
    /// </summary>
    /// <typeparam name="TView">View contract type.</typeparam>
    public abstract class OnityUiPresenter<TView> : IOnityUiPresenter
        where TView : class
    {
        /// <summary>
        /// Typed view reference.
        /// </summary>
        protected TView View { get; private set; }

        /// <inheritdoc />
        public void SetView(object view)
        {
            if (view is TView typedView == false)
            {
                throw new InvalidCastException(
                    $"Cannot assign view type '{view?.GetType().FullName}' to presenter '{GetType().FullName}'.");
            }

            View = typedView;
            OnViewAssigned();
        }

        /// <inheritdoc />
        public virtual void OnViewOpening()
        {
        }

        /// <inheritdoc />
        public virtual void OnViewOpened()
        {
        }

        /// <inheritdoc />
        public virtual void OnViewClosing()
        {
        }

        /// <inheritdoc />
        public virtual void OnViewClosed()
        {
        }

        /// <inheritdoc />
        public virtual void Dispose()
        {
        }

        /// <summary>
        /// Called after view assignment succeeds.
        /// </summary>
        protected virtual void OnViewAssigned()
        {
        }
    }
}
