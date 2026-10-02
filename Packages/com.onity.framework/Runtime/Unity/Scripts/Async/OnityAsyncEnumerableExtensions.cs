using System;
using System.Threading;

namespace Onity.Unity.Async
{
    /// <summary>Operators, adapters and terminal consumers for native asynchronous streams.</summary>
    /// <remarks>Descriptions are reusable; enumerations are independent and initially unpooled.
    /// Pending operations and terminal storage may allocate. No implicit main-thread hop is added.</remarks>
    public static partial class OnityAsyncEnumerableExtensions
    {
        /// <summary>Buffers a lazy observable subscription for native asynchronous consumption.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Observable with public lifecycle subscription support.</param>
        /// <param name="capacity">Positive per-enumeration buffered item capacity.</param>
        /// <returns>An inert reusable description with independent subscriptions and buffers.</returns>
        /// <remarks>Overflow rejects the new value, detaches and drains accepted values before
        /// faulting. Source errors and failed completion, including OCE, remain faults. Cancellation
        /// and disposal discard private buffered items without changing the source. Pre-canceled
        /// first moves skip Subscribe. Unsubscription failure has cleanup precedence. Storage,
        /// subscriptions and pending sources allocate; no implicit thread hop is added. Callers
        /// must respect source subscription/disposal affinity; this does not make Subject thread-safe.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Capacity is not positive.</exception>
        public static IOnityAsyncEnumerable<T> AsOnityAsyncEnumerable<T>(
            this Onity.Reactive.IOnityObservable<T> source, int capacity)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            return new OnityPushAsyncEnumerable<T>(source, capacity);
        }

        /// <summary>Starts independent sequential native enumeration for each observable subscription.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable native stream description.</param>
        /// <returns>An observable delivering terminal notification after native cleanup.</returns>
        /// <remarks>Lifecycle observers receive successful completion or one error. Value-only
        /// subscribers ignore success and route terminal faults/independent cancellation OCE to
        /// OnityObservableExceptionHandler. Explicit disposal suppresses terminal notifications;
        /// callback and post-disposal cleanup failures are reported globally without recursive
        /// observer notification. The subscription owns one observer.Dispose call. Cancellation
        /// converted through an observable returns as faulted OCE, since OnityResult has no canceled
        /// status. Pending observations/owned tokens allocate; no implicit thread/context hop is added.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static Onity.Reactive.IOnityObservable<T> AsObservable<T>(this IOnityAsyncEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityPullObservable<T>(source);
        }

        /// <summary>Maps each item with one sequential native asynchronous selector.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Selected item type.</typeparam>
        /// <param name="source">Reusable upstream description.</param>
        /// <param name="selector">Selector receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy description with independent unpooled enumerators.</returns>
        /// <remarks>The enumeration token is forwarded upstream; a child token is created only
        /// for the first selector. Empty streams can end despite pre-cancellation. Thrown OCE is
        /// faulted; returned task status/token is authoritative. Disposal signals the selector
        /// token and observes pending work while disposing upstream. Uncooperative work can retain
        /// cleanup. Cleanup failure takes precedence; no implicit thread/context hop is added.
        /// Enumerators, tokens and pending observers allocate.</remarks>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> SelectAwait<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<TResult>> selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            return new OnitySelectAwaitEnumerable<T, TResult>(source, selector);
        }

        /// <summary>Filters each item with one sequential native asynchronous predicate.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable upstream description.</param>
        /// <param name="predicate">Predicate receiving an owned cooperative lifetime token.</param>
        /// <returns>A lazy filtered description.</returns>
        /// <remarks>Synchronous false predicates are iterated without recursive chaining.
        /// The original token goes upstream; the lazy child token goes to predicates. Thrown OCE
        /// is faulted and returned status/token is retained. Disposal signals pending work and
        /// awaits observation plus upstream cleanup. Cleanup failure takes precedence. Pending
        /// work may retain cleanup; storage allocates and no implicit thread/context hop is added.</remarks>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> WhereAwait<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, CancellationToken, OnityTask<bool>> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            return new OnityWhereAwaitEnumerable<T>(source, predicate);
        }

        /// <summary>Starts sequential item actions immediately and awaits cleanup on every exit.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable upstream description.</param>
        /// <param name="action">Action receiving an owned cooperative lifetime token.</param>
        /// <param name="cancellationToken">Original upstream enumeration token.</param>
        /// <returns>Completion with actual fault/cancellation status and cleanup precedence.</returns>
        /// <remarks>Upstream normal end without items succeeds despite pre-cancellation;
        /// an upstream canceled/faulted outcome is retained. One move/action is observed
        /// at a time. Synchronous exceptions, including OCE, remain faults; successful actions
        /// ignoring cancellation remain successful. Cleanup cancels only the owned action token,
        /// never the caller source. Uncooperative pending work can retain cleanup. Storage
        /// allocates; no implicit Unity/context hop or async-builder normalization is added.</remarks>
        /// <exception cref="ArgumentNullException">Source or action is null.</exception>
        public static OnityTask ForEachAsync<T>(this IOnityAsyncEnumerable<T> source,
            Func<T, CancellationToken, OnityTask> action, CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            OnityTask<bool> task = OnityAsyncEnumerableConsumers.Read(
                new OnityForEachAsyncEnumerator<T>(source, action, cancellationToken));
            return OnityAwaitStreamCompletion.Read(task);
        }

        /// <summary>Imports a BCL asynchronous stream without an implicit thread hop.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable BCL stream description.</param>
        /// <returns>An inert native description with independent, unpooled enumerators.</returns>
        /// <remarks>Acquisition is lazy. Each enumeration owns a child cancellation source;
        /// the caller's source is never canceled or disposed. Disposal requests cancellation,
        /// consumes the outstanding ValueTask once, then invokes upstream disposal. Actual
        /// canceled status/tokens are preserved; faulted OCE and synchronous exceptions remain
        /// faults. A malformed source rejecting registration without later completing can retain
        /// pending cleanup indefinitely. Only one move may be outstanding; each upstream
        /// ValueTask is consumed once, and repeated disposal shares cleanup.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> AsOnityAsyncEnumerable<T>(
            this System.Collections.Generic.IAsyncEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityBclAsyncEnumerable<T>(source);
        }

        /// <summary>Exports a native stream using the BCL asynchronous enumeration pattern.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable native stream description.</param>
        /// <returns>An inert BCL description with independent enumerators.</returns>
        /// <remarks>Successful synchronous moves use inline ValueTasks. Pending/faulted moves
        /// use explicit Task bridges and retain ordinary BCL caller await-context behavior.
        /// Internal observation adds no thread hop or creator-context capture. Native terminal
        /// status is preserved, including faulted OCE. Cleanup is invoked once and shared across
        /// disposal calls; pending operations and bridge storage may allocate.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static System.Collections.Generic.IAsyncEnumerable<T> AsAsyncEnumerable<T>(
            this IOnityAsyncEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityNativeAsyncEnumerable<T>(source);
        }

        /// <summary>Combines a wrapper token with each enumeration's token using OR semantics.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Reusable upstream description.</param>
        /// <param name="cancellationToken">Additional cooperative token.</param>
        /// <returns>A lazy description owning any required linked token source.</returns>
        /// <remarks>Sole/equal tokens are reused. Distinct cancelable tokens are linked until
        /// upstream cleanup finishes. Actual upstream cancellation tokens are preserved.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static IOnityAsyncEnumerable<T> WithCancellation<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return new OnityCancellationAsyncEnumerable<T>(source, cancellationToken);
        }

        /// <summary>Maps items sequentially with a synchronous selector.</summary>
        /// <typeparam name="T">Input item type.</typeparam>
        /// <typeparam name="TResult">Output item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="selector">Selector invoked outside lifecycle locks.</param>
        /// <returns>A lazy mapped description.</returns>
        /// <remarks>Selector exceptions, including OCE, are faults. Upstream cleanup runs once.</remarks>
        /// <exception cref="ArgumentNullException">Source or selector is null.</exception>
        public static IOnityAsyncEnumerable<TResult> Select<T, TResult>(
            this IOnityAsyncEnumerable<T> source, Func<T, TResult> selector)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            return new OnitySelectAsyncEnumerable<T, TResult>(source, selector);
        }

        /// <summary>Filters items sequentially without recursive synchronous moves.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="predicate">Predicate invoked outside lifecycle locks.</param>
        /// <returns>A lazy filtered description.</returns>
        /// <remarks>Predicate exceptions, including OCE, are faults. Upstream cleanup runs once.</remarks>
        /// <exception cref="ArgumentNullException">Source or predicate is null.</exception>
        public static IOnityAsyncEnumerable<T> Where<T>(
            this IOnityAsyncEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            return new OnityWhereAsyncEnumerable<T>(source, predicate);
        }

        /// <summary>Takes at most count items, awaiting upstream cleanup before the last item.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="count">Nonnegative item limit.</param>
        /// <returns>A lazy limited description; zero never acquires upstream resources.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Count is negative.</exception>
        public static IOnityAsyncEnumerable<T> Take<T>(this IOnityAsyncEnumerable<T> source, int count)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return count == 0 ? OnityAsyncEnumerable.Empty<T>() : new OnityTakeAsyncEnumerable<T>(source, count);
        }

        /// <summary>Reads the first item and awaits cleanup on every exit.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>The first item, or an empty-stream fault. Cleanup failure takes precedence.</returns>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T> FirstAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return OnityAsyncEnumerableConsumers.Read(new OnityFirstAsyncEnumerator<T>(source, cancellationToken));
        }

        /// <summary>Collects every item into a new array and awaits cleanup on every exit.</summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="source">Upstream description.</param>
        /// <param name="cancellationToken">Cooperative enumeration token.</param>
        /// <returns>Ordered items, with actual fault/cancellation status preserved.</returns>
        /// <remarks>Storage allocates. Cleanup failure takes precedence over an earlier operation failure.</remarks>
        /// <exception cref="ArgumentNullException">Source is null.</exception>
        public static OnityTask<T[]> ToArrayAsync<T>(
            this IOnityAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return OnityAsyncEnumerableConsumers.Read(new OnityArrayAsyncEnumerator<T>(source, cancellationToken));
        }
    }
}
