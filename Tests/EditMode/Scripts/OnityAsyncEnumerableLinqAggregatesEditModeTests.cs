using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Unity.Async;

namespace Onity.Tests.EditMode
{
    public sealed class OnityAsyncEnumerableLinqAggregatesEditModeTests
    {
        private static LinqProbe<int> Items(params int[] items) => LinqTestSupport.Items(items);
        private static T Read<T>(OnityTask<T> task) => LinqTestSupport.Read(task);
        private static Exception Catch(Action action) => LinqTestSupport.Catch(action);
        private static OnityTask<bool> Await(bool value) => OnityTask<bool>.FromResult(value);
        private static OnityTask<T> Result<T>(T value) => OnityTask<T>.FromResult(value);

        private static void AllEqual<T>(T expected, OnityTask<T> plain, OnityTask<T> sync, OnityTask<T> awaiting,
            OnityTask<T> cancel)
        {
            Assert.That(Read(plain), Is.EqualTo(expected));
            Assert.That(Read(sync), Is.EqualTo(expected));
            Assert.That(Read(awaiting), Is.EqualTo(expected));
            Assert.That(Read(cancel), Is.EqualTo(expected));
        }

        // ---- admission -------------------------------------------------------------------

        [Test]
        public void Arguments_ValidateSynchronously_AndTerminalsAcquireOnlyWhenCalled()
        {
            IOnityAsyncEnumerable<int> missing = null;
            var source = Items(1, 2);
            Func<int, bool> sync = null;
            Func<int, OnityTask<bool>> awaiting = null;
            Func<int, CancellationToken, OnityTask<bool>> cancel = null;
            Assert.Throws<ArgumentNullException>(() => missing.CountAsync());
            Assert.Throws<ArgumentNullException>(() => source.CountAsync(sync));
            Assert.Throws<ArgumentNullException>(() => source.CountAwaitAsync(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.CountAwaitWithCancellationAsync(cancel));
            Assert.Throws<ArgumentNullException>(() => missing.LongCountAsync());
            Assert.Throws<ArgumentNullException>(() => source.LongCountAsync(sync));
            Assert.Throws<ArgumentNullException>(() => missing.AnyAsync());
            Assert.Throws<ArgumentNullException>(() => source.AnyAsync(sync));
            Assert.Throws<ArgumentNullException>(() => source.AnyAwaitAsync(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.AnyAwaitWithCancellationAsync(cancel));
            Assert.Throws<ArgumentNullException>(() => source.AllAsync(sync));
            Assert.Throws<ArgumentNullException>(() => source.AllAwaitAsync(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.AllAwaitWithCancellationAsync(cancel));
            Assert.Throws<ArgumentNullException>(() => missing.ContainsAsync(1));
            Assert.Throws<ArgumentNullException>(() => missing.AggregateAsync((a, b) => a + b));
            Assert.Throws<ArgumentNullException>(() => source.AggregateAsync((Func<int, int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.AggregateAsync(0, (Func<int, int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.AggregateAsync(0, (a, b) => a + b, (Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.AggregateAwaitAsync((Func<int, int, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => source.AggregateAwaitWithCancellationAsync(
                0, (Func<int, int, CancellationToken, OnityTask<int>>)null));
            Assert.Throws<ArgumentNullException>(() => missing.FirstAsync(value => true));
            Assert.Throws<ArgumentNullException>(() => source.FirstAsync(sync));
            Assert.Throws<ArgumentNullException>(() => source.FirstAwaitAsync(awaiting));
            Assert.Throws<ArgumentNullException>(() => source.FirstOrDefaultAwaitWithCancellationAsync(cancel));
            Assert.Throws<ArgumentNullException>(() => source.LastAsync(sync));
            Assert.Throws<ArgumentNullException>(() => source.LastOrDefaultAwaitAsync(awaiting));
            Assert.Throws<ArgumentNullException>(() => missing.SingleAsync());
            Assert.Throws<ArgumentNullException>(() => source.SingleOrDefaultAsync(sync));
            Assert.Throws<ArgumentNullException>(() => missing.ElementAtAsync(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => source.ElementAtAsync(-1));
            Assert.Throws<ArgumentNullException>(() => source.SequenceEqualAsync(null));
            Assert.Throws<ArgumentNullException>(() => ((IOnityAsyncEnumerable<int>)null).SequenceEqualAsync(source));
            Assert.Throws<ArgumentNullException>(() => missing.MinAsync());
            Assert.Throws<ArgumentNullException>(() => source.MaxAsync((Func<int, string>)null));
            Assert.Throws<ArgumentNullException>(() => missing.SumAsync());
            Assert.Throws<ArgumentNullException>(() => source.SumAsync((Func<int, int>)null));
            Assert.Throws<ArgumentNullException>(() => source.AverageAwaitAsync((Func<int, OnityTask<double>>)null));
            Assert.Throws<ArgumentNullException>(() => source.MinAwaitWithCancellationAsync(
                (Func<int, CancellationToken, OnityTask<decimal?>>)null));
            Assert.That(source.Acquires, Is.Zero);
            Assert.That(Read(source.ElementAtOrDefaultAsync(-1)), Is.Zero);
            Assert.That(source.Acquires, Is.Zero);
        }

        // ---- Count / LongCount / Any / All / Contains ------------------------------------

        [Test]
        public void Count_AllShapes()
        {
            var source = Items(1, 2, 3, 4, 5);
            Assert.That(Read(source.CountAsync()), Is.EqualTo(5));
            Assert.That(Read(source.CountAsync(value => value > 2)), Is.EqualTo(3));
            Assert.That(Read(source.CountAwaitAsync(value => Await(value > 3))), Is.EqualTo(2));
            Assert.That(Read(source.CountAwaitWithCancellationAsync((value, token) => Await(value < 2))), Is.EqualTo(1));
            Assert.That(Read(source.LongCountAsync()), Is.EqualTo(5L));
            Assert.That(Read(source.LongCountAsync(value => value > 2)), Is.EqualTo(3L));
            Assert.That(Read(source.LongCountAwaitAsync(value => Await(value > 3))), Is.EqualTo(2L));
            Assert.That(Read(source.LongCountAwaitWithCancellationAsync((value, token) => Await(false))), Is.Zero);
            Assert.That(Read(Items().CountAsync()), Is.Zero);
            Assert.That(Read(Items().LongCountAsync(value => true)), Is.Zero);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void AnyAndAll_ShortCircuit_AndDisposeOnEarlyExit()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.AnyAsync()), Is.True);
            Assert.That(source.Moves, Is.EqualTo(1));
            Assert.That(source.Disposals, Is.EqualTo(1));
            Assert.That(Read(source.AnyAsync(value => value == 3)), Is.True);
            Assert.That(source.Moves, Is.EqualTo(4));
            Assert.That(Read(source.AnyAwaitAsync(value => Await(value == 2))), Is.True);
            Assert.That(Read(source.AnyAwaitWithCancellationAsync((value, token) => Await(value == 9))), Is.False);
            Assert.That(Read(Items().AnyAsync()), Is.False);
            Assert.That(Read(source.AllAsync(value => value < 3)), Is.False);
            Assert.That(Read(source.AllAsync(value => value < 9)), Is.True);
            Assert.That(Read(source.AllAwaitAsync(value => Await(value < 9))), Is.True);
            Assert.That(Read(source.AllAwaitWithCancellationAsync((value, token) => Await(value < 2))), Is.False);
            Assert.That(Read(Items().AllAsync(value => false)), Is.True);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void AllStopsAtFirstRejection()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.AllAsync(value => value < 2)), Is.False);
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void Contains_UsesDefaultOrSuppliedComparer_AndStopsEarly()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.ContainsAsync(2)), Is.True);
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(Read(source.ContainsAsync(9)), Is.False);
            Assert.That(Read(source.ContainsAsync(9, EqualityComparer<int>.Default)), Is.False);
            Assert.That(Read(source.ContainsAsync(7, new ModuloComparer(5))), Is.True);
            Assert.That(Read(source.ContainsAsync(2, (IEqualityComparer<int>)null)), Is.True);
            var strings = new LinqProbe<string>("a", "B");
            Assert.That(Read(strings.ContainsAsync("b")), Is.False);
            Assert.That(Read(strings.ContainsAsync("b", StringComparer.OrdinalIgnoreCase)), Is.True);
            Assert.That(Read(new LinqProbe<string>(null, "x").ContainsAsync(null)), Is.True);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        // ---- Aggregate -------------------------------------------------------------------

        [Test]
        public void Aggregate_AllShapes()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.AggregateAsync((a, b) => a * 10 + b)), Is.EqualTo(1234));
            Assert.That(Read(source.AggregateAsync(100, (a, b) => a + b)), Is.EqualTo(110));
            Assert.That(Read(source.AggregateAsync("s", (a, b) => a + b, a => a + "!")), Is.EqualTo("s1234!"));
            Assert.That(Read(source.AggregateAwaitAsync((a, b) => Result(a * 10 + b))), Is.EqualTo(1234));
            Assert.That(Read(source.AggregateAwaitAsync(100, (a, b) => Result(a + b))), Is.EqualTo(110));
            Assert.That(Read(source.AggregateAwaitAsync("s", (a, b) => Result(a + b), a => Result(a + "!"))),
                Is.EqualTo("s1234!"));
            Assert.That(Read(source.AggregateAwaitWithCancellationAsync((a, b, token) => Result(a * 10 + b))),
                Is.EqualTo(1234));
            Assert.That(Read(source.AggregateAwaitWithCancellationAsync(100, (a, b, token) => Result(a + b))),
                Is.EqualTo(110));
            Assert.That(Read(source.AggregateAwaitWithCancellationAsync("s", (a, b, token) => Result(a + b),
                (a, token) => Result(a + "?"))), Is.EqualTo("s1234?"));
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void Aggregate_EmptyStream_ReturnsSeedOrFaultsWithoutSeed()
        {
            var empty = Items();
            Assert.That(Read(empty.AggregateAsync(7, (a, b) => a + b)), Is.EqualTo(7));
            Assert.That(Read(empty.AggregateAsync(7, (a, b) => a + b, a => a * 2)), Is.EqualTo(14));
            var task = empty.AggregateAsync((a, b) => a + b);
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.InstanceOf<InvalidOperationException>());
            Assert.That(empty.Disposals, Is.EqualTo(3));
        }

        // ---- First / Last / Single -------------------------------------------------------

        [Test]
        public void First_AllShapes_StopAtTheMatch()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.FirstAsync(value => value > 1)), Is.EqualTo(2));
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(Read(source.FirstAwaitAsync(value => Await(value > 2))), Is.EqualTo(3));
            Assert.That(Read(source.FirstAwaitWithCancellationAsync((value, token) => Await(value > 3))), Is.EqualTo(4));
            Assert.That(Read(source.FirstOrDefaultAsync()), Is.EqualTo(1));
            Assert.That(Read(source.FirstOrDefaultAsync(value => value > 9)), Is.Zero);
            Assert.That(Read(source.FirstOrDefaultAwaitAsync(value => Await(value > 1))), Is.EqualTo(2));
            Assert.That(Read(source.FirstOrDefaultAwaitWithCancellationAsync((value, token) => Await(false))), Is.Zero);
            Assert.That(Read(Items().FirstOrDefaultAsync()), Is.Zero);
            Assert.That(Read(new LinqProbe<string>().FirstOrDefaultAsync()), Is.Null);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void First_NoMatch_FaultsWithInvalidOperation()
        {
            var source = Items(1, 2);
            foreach (var task in new[]
            {
                source.FirstAsync(value => value > 9),
                source.FirstAwaitAsync(value => Await(false)),
                source.FirstAwaitWithCancellationAsync((value, token) => Await(false)),
                Items().LastAsync(),
                source.LastAsync(value => value > 9),
                source.LastAwaitAsync(value => Await(false)),
                source.LastAwaitWithCancellationAsync((value, token) => Await(false)),
                Items().SingleAsync(),
                source.SingleAsync(value => value > 9),
                source.SingleAwaitAsync(value => Await(false)),
                source.SingleAwaitWithCancellationAsync((value, token) => Await(false))
            })
            {
                Assert.That(task.IsFaulted, Is.True);
                Assert.That(Catch(() => Read(task)), Is.InstanceOf<InvalidOperationException>());
            }
        }

        [Test]
        public void Last_AllShapes_ScanEverything()
        {
            var source = Items(1, 2, 3, 4);
            Assert.That(Read(source.LastAsync()), Is.EqualTo(4));
            Assert.That(source.Moves, Is.EqualTo(5));
            Assert.That(Read(source.LastAsync(value => value < 3)), Is.EqualTo(2));
            Assert.That(Read(source.LastAwaitAsync(value => Await(value < 4))), Is.EqualTo(3));
            Assert.That(Read(source.LastAwaitWithCancellationAsync((value, token) => Await(value < 2))), Is.EqualTo(1));
            Assert.That(Read(source.LastOrDefaultAsync()), Is.EqualTo(4));
            Assert.That(Read(source.LastOrDefaultAsync(value => value > 9)), Is.Zero);
            Assert.That(Read(source.LastOrDefaultAwaitAsync(value => Await(value < 3))), Is.EqualTo(2));
            Assert.That(Read(source.LastOrDefaultAwaitWithCancellationAsync((value, token) => Await(value > 3))),
                Is.EqualTo(4));
            Assert.That(Read(Items().LastOrDefaultAsync()), Is.Zero);
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        [Test]
        public void Single_AllShapes_AndSecondMatchFaultsEarly()
        {
            var source = Items(1, 2, 3);
            Assert.That(Read(Items(5).SingleAsync()), Is.EqualTo(5));
            Assert.That(Read(source.SingleAsync(value => value == 2)), Is.EqualTo(2));
            Assert.That(Read(source.SingleAwaitAsync(value => Await(value == 3))), Is.EqualTo(3));
            Assert.That(Read(source.SingleAwaitWithCancellationAsync((value, token) => Await(value == 1))), Is.EqualTo(1));
            Assert.That(Read(Items().SingleOrDefaultAsync()), Is.Zero);
            Assert.That(Read(source.SingleOrDefaultAsync(value => value > 9)), Is.Zero);
            Assert.That(Read(source.SingleOrDefaultAwaitAsync(value => Await(value == 2))), Is.EqualTo(2));
            Assert.That(Read(source.SingleOrDefaultAwaitWithCancellationAsync((value, token) => Await(value == 3))),
                Is.EqualTo(3));
            int disposalsBefore = source.Disposals;
            int movesBefore = source.Moves;
            var many = source.SingleAsync();
            Assert.That(many.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(many)), Is.InstanceOf<InvalidOperationException>());
            Assert.That(source.Moves - movesBefore, Is.EqualTo(2));
            Assert.That(source.Disposals - disposalsBefore, Is.EqualTo(1));
            foreach (var task in new[]
            {
                source.SingleAsync(value => value > 1),
                source.SingleAwaitAsync(value => Await(true)),
                source.SingleAwaitWithCancellationAsync((value, token) => Await(true)),
                source.SingleOrDefaultAsync(),
                source.SingleOrDefaultAsync(value => true),
                source.SingleOrDefaultAwaitAsync(value => Await(true)),
                source.SingleOrDefaultAwaitWithCancellationAsync((value, token) => Await(true))
            })
            {
                Assert.That(Catch(() => Read(task)), Is.InstanceOf<InvalidOperationException>());
            }
        }

        // ---- ElementAt -------------------------------------------------------------------

        [Test]
        public void ElementAt_ReadsByIndex_AndStopsAtTheItem()
        {
            var source = Items(10, 20, 30);
            Assert.That(Read(source.ElementAtAsync(1)), Is.EqualTo(20));
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(Read(source.ElementAtAsync(0)), Is.EqualTo(10));
            Assert.That(Read(source.ElementAtOrDefaultAsync(2)), Is.EqualTo(30));
            Assert.That(Read(source.ElementAtOrDefaultAsync(3)), Is.Zero);
            var outOfRange = source.ElementAtAsync(3);
            Assert.That(outOfRange.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(outOfRange)), Is.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(source.Disposals, Is.EqualTo(source.Acquires));
        }

        // ---- SequenceEqual ---------------------------------------------------------------

        [Test]
        public void SequenceEqual_ComparesItemsAndLengths()
        {
            Assert.That(Read(Items(1, 2).SequenceEqualAsync(Items(1, 2))), Is.True);
            Assert.That(Read(Items().SequenceEqualAsync(Items())), Is.True);
            Assert.That(Read(Items(1, 2, 3).SequenceEqualAsync(Items(1, 2))), Is.False);
            Assert.That(Read(Items(1, 2).SequenceEqualAsync(Items(1, 2, 3))), Is.False);
            Assert.That(Read(Items(1, 2).SequenceEqualAsync(Items(1, 3))), Is.False);
            Assert.That(Read(new LinqProbe<string>("a", "B").SequenceEqualAsync(
                new LinqProbe<string>("A", "b"))), Is.False);
            Assert.That(Read(new LinqProbe<string>("a", "B").SequenceEqualAsync(
                new LinqProbe<string>("A", "b"), StringComparer.OrdinalIgnoreCase)), Is.True);
            Assert.That(Read(Items(1).SequenceEqualAsync(Items(1), (IEqualityComparer<int>)null)), Is.True);
        }

        [Test]
        public void SequenceEqual_DisposesBothEnumerationsOnEveryExit()
        {
            var first = Items(1, 2, 3);
            var second = Items(1, 9, 3);
            Assert.That(Read(first.SequenceEqualAsync(second)), Is.False);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(first.Moves, Is.EqualTo(2));
            Assert.That(second.Moves, Is.EqualTo(2));

            var failing = Items(1);
            failing.DisposeFailure = new InvalidOperationException("second cleanup");
            var healthy = Items(1);
            var task = healthy.SequenceEqualAsync(failing);
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(task)), Is.SameAs(failing.DisposeFailure));
            Assert.That(healthy.Disposals, Is.EqualTo(1));
            Assert.That(failing.Disposals, Is.EqualTo(1));
        }

        // ---- Min / Max (comparer based) --------------------------------------------------

        [Test]
        public void MinMax_Generic_AllShapes()
        {
            var strings = new LinqProbe<string>("pear", "apple", "fig");
            Assert.That(Read(strings.MinAsync()), Is.EqualTo("apple"));
            Assert.That(Read(strings.MaxAsync()), Is.EqualTo("pear"));
            Assert.That(Read(strings.MinAsync(value => value.Length)), Is.EqualTo(3));
            Assert.That(Read(strings.MaxAsync(value => value.Length)), Is.EqualTo(5));
            Assert.That(Read(strings.MinAwaitAsync(value => Result(value.Length))), Is.EqualTo(3));
            Assert.That(Read(strings.MaxAwaitAsync(value => Result(value.Length))), Is.EqualTo(5));
            Assert.That(Read(strings.MinAwaitWithCancellationAsync((value, token) => Result(value.Length))), Is.EqualTo(3));
            Assert.That(Read(strings.MaxAwaitWithCancellationAsync((value, token) => Result(value.Length))), Is.EqualTo(5));
            // Empty streams. The comparer-based generic overloads return default (UniTask parity): null for
            // the reference-type results below. A selector that returns int binds to the numeric Int32
            // overload instead, which throws on an empty stream, as System.Linq and UniTask do.
            Assert.That(Read(new LinqProbe<string>().MinAsync()), Is.Null);
            Assert.That(Read(new LinqProbe<string>().MaxAsync(value => value.ToUpperInvariant())), Is.Null);
            Assert.That(Read(new LinqProbe<string>().MinAwaitAsync(value => Result(value.ToUpperInvariant()))), Is.Null);
            Assert.That(Catch(() => Read(new LinqProbe<string>().MaxAsync(value => value.Length))),
                Is.InstanceOf<InvalidOperationException>());
            Assert.That(strings.Disposals, Is.EqualTo(strings.Acquires));
        }

        // ---- numeric Sum / Average / Min / Max -------------------------------------------

        [Test]
        public void Numeric_Int32()
        {
            var source = Items(3, 1, 2);
            AllEqual(6, source.SumAsync(), source.SumAsync(x => x), source.SumAwaitAsync(x => Result(x)),
                source.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(2.0, source.AverageAsync(), source.AverageAsync(x => x), source.AverageAwaitAsync(x => Result(x)),
                source.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(1, source.MinAsync(), source.MinAsync(x => x), source.MinAwaitAsync(x => Result(x)),
                source.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(3, source.MaxAsync(), source.MaxAsync(x => x), source.MaxAwaitAsync(x => Result(x)),
                source.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(Items().SumAsync()), Is.Zero);
            Assert.That(Catch(() => Read(Items().AverageAsync())), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(Items().MinAsync(x => x))), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(Items().MaxAwaitAsync(x => Result(x)))), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Read(Items(int.MaxValue, int.MaxValue).AverageAsync()), Is.EqualTo((double)int.MaxValue));
            var overflow = Items(int.MaxValue, 1).SumAsync();
            Assert.That(overflow.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(overflow)), Is.InstanceOf<OverflowException>());
        }

        [Test]
        public void Numeric_Int64()
        {
            var source = new LinqProbe<long>(3L, 1L, 2L);
            AllEqual(6L, source.SumAsync(), source.SumAsync(x => x), source.SumAwaitAsync(x => Result(x)),
                source.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(2.0, source.AverageAsync(), source.AverageAsync(x => x), source.AverageAwaitAsync(x => Result(x)),
                source.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(1L, source.MinAsync(), source.MinAsync(x => x), source.MinAwaitAsync(x => Result(x)),
                source.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(3L, source.MaxAsync(), source.MaxAsync(x => x), source.MaxAwaitAsync(x => Result(x)),
                source.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(new LinqProbe<long>().SumAsync()), Is.Zero);
            Assert.That(Catch(() => Read(new LinqProbe<long>().AverageAsync())), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(new LinqProbe<long>().MinAsync())), Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Numeric_Single()
        {
            var source = new LinqProbe<float>(3f, 1f, 2f);
            AllEqual(6f, source.SumAsync(), source.SumAsync(x => x), source.SumAwaitAsync(x => Result(x)),
                source.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(2f, source.AverageAsync(), source.AverageAsync(x => x), source.AverageAwaitAsync(x => Result(x)),
                source.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(1f, source.MinAsync(), source.MinAsync(x => x), source.MinAwaitAsync(x => Result(x)),
                source.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(3f, source.MaxAsync(), source.MaxAsync(x => x), source.MaxAwaitAsync(x => Result(x)),
                source.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(new LinqProbe<float>().SumAsync()), Is.Zero);
            Assert.That(Catch(() => Read(new LinqProbe<float>().AverageAsync())), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(new LinqProbe<float>().MaxAsync())), Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Numeric_Double()
        {
            var source = new LinqProbe<double>(3d, 1d, 2d);
            AllEqual(6d, source.SumAsync(), source.SumAsync(x => x), source.SumAwaitAsync(x => Result(x)),
                source.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(2d, source.AverageAsync(), source.AverageAsync(x => x), source.AverageAwaitAsync(x => Result(x)),
                source.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(1d, source.MinAsync(), source.MinAsync(x => x), source.MinAwaitAsync(x => Result(x)),
                source.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(3d, source.MaxAsync(), source.MaxAsync(x => x), source.MaxAwaitAsync(x => Result(x)),
                source.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(new LinqProbe<double>().SumAsync()), Is.Zero);
            Assert.That(Catch(() => Read(new LinqProbe<double>().AverageAsync())), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(new LinqProbe<double>().MinAsync())), Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Numeric_Decimal()
        {
            var source = new LinqProbe<decimal>(3m, 1m, 2m);
            AllEqual(6m, source.SumAsync(), source.SumAsync(x => x), source.SumAwaitAsync(x => Result(x)),
                source.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(2m, source.AverageAsync(), source.AverageAsync(x => x), source.AverageAwaitAsync(x => Result(x)),
                source.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(1m, source.MinAsync(), source.MinAsync(x => x), source.MinAwaitAsync(x => Result(x)),
                source.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual(3m, source.MaxAsync(), source.MaxAsync(x => x), source.MaxAwaitAsync(x => Result(x)),
                source.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(new LinqProbe<decimal>().SumAsync()), Is.Zero);
            Assert.That(Catch(() => Read(new LinqProbe<decimal>().AverageAsync())), Is.InstanceOf<InvalidOperationException>());
            Assert.That(Catch(() => Read(new LinqProbe<decimal>().MaxAsync())), Is.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Numeric_NullableInt32AndInt64()
        {
            var ints = new LinqProbe<int?>(3, null, 1);
            AllEqual((int?)4, ints.SumAsync(), ints.SumAsync(x => x), ints.SumAwaitAsync(x => Result(x)),
                ints.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((double?)2.0, ints.AverageAsync(), ints.AverageAsync(x => x), ints.AverageAwaitAsync(x => Result(x)),
                ints.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((int?)1, ints.MinAsync(), ints.MinAsync(x => x), ints.MinAwaitAsync(x => Result(x)),
                ints.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((int?)3, ints.MaxAsync(), ints.MaxAsync(x => x), ints.MaxAwaitAsync(x => Result(x)),
                ints.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            var none = new LinqProbe<int?>(null, null);
            Assert.That(Read(none.SumAsync()), Is.EqualTo(0));
            Assert.That(Read(none.AverageAsync()), Is.Null);
            Assert.That(Read(none.MinAsync()), Is.Null);
            Assert.That(Read(none.MaxAsync()), Is.Null);
            Assert.That(Read(new LinqProbe<int?>().AverageAsync()), Is.Null);

            var longs = new LinqProbe<long?>(3L, null, 1L);
            AllEqual((long?)4, longs.SumAsync(), longs.SumAsync(x => x), longs.SumAwaitAsync(x => Result(x)),
                longs.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((double?)2.0, longs.AverageAsync(), longs.AverageAsync(x => x), longs.AverageAwaitAsync(x => Result(x)),
                longs.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((long?)1, longs.MinAsync(), longs.MinAsync(x => x), longs.MinAwaitAsync(x => Result(x)),
                longs.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((long?)3, longs.MaxAsync(), longs.MaxAsync(x => x), longs.MaxAwaitAsync(x => Result(x)),
                longs.MaxAwaitWithCancellationAsync((x, t) => Result(x)));
            Assert.That(Read(new LinqProbe<long?>(new long?[] { null }).MaxAsync()), Is.Null);
        }

        [Test]
        public void Numeric_NullableSingleDoubleAndDecimal()
        {
            var floats = new LinqProbe<float?>(3f, null, 1f);
            AllEqual((float?)4f, floats.SumAsync(), floats.SumAsync(x => x), floats.SumAwaitAsync(x => Result(x)),
                floats.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((float?)2f, floats.AverageAsync(), floats.AverageAsync(x => x), floats.AverageAwaitAsync(x => Result(x)),
                floats.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((float?)1f, floats.MinAsync(), floats.MinAsync(x => x), floats.MinAwaitAsync(x => Result(x)),
                floats.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((float?)3f, floats.MaxAsync(), floats.MaxAsync(x => x), floats.MaxAwaitAsync(x => Result(x)),
                floats.MaxAwaitWithCancellationAsync((x, t) => Result(x)));

            var doubles = new LinqProbe<double?>(3d, null, 1d);
            AllEqual((double?)4d, doubles.SumAsync(), doubles.SumAsync(x => x), doubles.SumAwaitAsync(x => Result(x)),
                doubles.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((double?)2d, doubles.AverageAsync(), doubles.AverageAsync(x => x), doubles.AverageAwaitAsync(x => Result(x)),
                doubles.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((double?)1d, doubles.MinAsync(), doubles.MinAsync(x => x), doubles.MinAwaitAsync(x => Result(x)),
                doubles.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((double?)3d, doubles.MaxAsync(), doubles.MaxAsync(x => x), doubles.MaxAwaitAsync(x => Result(x)),
                doubles.MaxAwaitWithCancellationAsync((x, t) => Result(x)));

            var decimals = new LinqProbe<decimal?>(3m, null, 1m);
            AllEqual((decimal?)4m, decimals.SumAsync(), decimals.SumAsync(x => x), decimals.SumAwaitAsync(x => Result(x)),
                decimals.SumAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((decimal?)2m, decimals.AverageAsync(), decimals.AverageAsync(x => x),
                decimals.AverageAwaitAsync(x => Result(x)), decimals.AverageAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((decimal?)1m, decimals.MinAsync(), decimals.MinAsync(x => x), decimals.MinAwaitAsync(x => Result(x)),
                decimals.MinAwaitWithCancellationAsync((x, t) => Result(x)));
            AllEqual((decimal?)3m, decimals.MaxAsync(), decimals.MaxAsync(x => x), decimals.MaxAwaitAsync(x => Result(x)),
                decimals.MaxAwaitWithCancellationAsync((x, t) => Result(x)));

            Assert.That(Read(new LinqProbe<float?>(new float?[] { null }).AverageAsync()), Is.Null);
            Assert.That(Read(new LinqProbe<double?>().MinAsync()), Is.Null);
            Assert.That(Read(new LinqProbe<decimal?>(new decimal?[] { null }).SumAsync()), Is.EqualTo(0m));
        }

        [Test]
        public void Numeric_SelectorsReceiveItemsAndTheEnumerationToken()
        {
            var source = Items(1, 2, 3);
            Assert.That(Read(source.SumAsync(value => value * 2)), Is.EqualTo(12));
            Assert.That(Read(source.AverageAsync(value => value * 1.5)), Is.EqualTo(3.0));
            Assert.That(Read(source.MinAsync(value => (long)value - 5)), Is.EqualTo(-4L));
            Assert.That(Read(source.MaxAsync(value => (decimal?)(value * 10))), Is.EqualTo(30m));
            using (var cts = new CancellationTokenSource())
            {
                CancellationToken seen = default;
                Assert.That(Read(source.SumAwaitWithCancellationAsync((value, token) =>
                {
                    seen = token;
                    return Result(value);
                }, cts.Token)), Is.EqualTo(6));
                Assert.That(seen, Is.EqualTo(cts.Token));
                Assert.That(source.Token, Is.EqualTo(cts.Token));
            }
        }

        // ---- cancellation, faults, cleanup ------------------------------------------------

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void CancellationMidEnumeration_IsCanceled_AndUpstreamIsDisposedOnce(int shape)
        {
            using (var cts = new CancellationTokenSource())
            {
                var source = Items(1, 2, 3, 4, 5);
                source.OnMove = move =>
                {
                    if (move == 2)
                    {
                        cts.Cancel();
                    }
                };
                OnityTask task;
                switch (shape)
                {
                    case 0: task = ToTask(source.CountAsync(cts.Token)); break;
                    case 1: task = ToTask(source.LongCountAsync(value => true, cts.Token)); break;
                    case 2: task = ToTask(source.AnyAsync(value => false, cts.Token)); break;
                    case 3: task = ToTask(source.AllAwaitAsync(value => Await(true), cts.Token)); break;
                    case 4: task = ToTask(source.LastAsync(cts.Token)); break;
                    case 5: task = ToTask(source.SumAsync(cts.Token)); break;
                    case 6: task = ToTask(source.AggregateAsync((a, b) => a + b, cts.Token)); break;
                    case 7: task = ToTask(source.ContainsAsync(99, cts.Token)); break;
                    default: task = ToTask(source.MinAwaitWithCancellationAsync(
                        (value, token) => Result(value), cts.Token)); break;
                }
                Assert.That(task.IsCompleted, Is.True);
                Assert.That(task.IsCanceled, Is.True);
                Assert.That(source.Disposals, Is.EqualTo(1));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void UpstreamFault_Propagates_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("source");
            var source = Items(1, 2, 3, 4);
            source.MoveFailureAt = 2;
            source.MoveFailure = fault;
            OnityTask task;
            switch (shape)
            {
                case 0: task = ToTask(source.CountAsync()); break;
                case 1: task = ToTask(source.AnyAsync(value => false)); break;
                case 2: task = ToTask(source.LastAsync()); break;
                case 3: task = ToTask(source.SingleOrDefaultAsync(value => value > 9)); break;
                case 4: task = ToTask(source.AverageAsync()); break;
                default: task = ToTask(source.AggregateAsync(0, (a, b) => a + b)); break;
            }
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => task.GetAwaiter().GetResult()), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void DelegateException_Faults_AndUpstreamIsDisposedOnce(int shape)
        {
            var fault = new InvalidOperationException("delegate");
            var source = Items(1, 2, 3);
            OnityTask task;
            switch (shape)
            {
                case 0: task = ToTask(source.CountAsync((Func<int, bool>)(value => throw fault))); break;
                case 1: task = ToTask(source.FirstAwaitAsync((Func<int, OnityTask<bool>>)(value => throw fault))); break;
                case 2: task = ToTask(source.AggregateAsync((Func<int, int, int>)((a, b) => throw fault))); break;
                case 3: task = ToTask(source.SumAsync((Func<int, int>)(value => throw fault))); break;
                case 4: task = ToTask(source.MaxAsync((Func<int, string>)(value => throw fault))); break;
                case 5: task = ToTask(source.AllAsync((Func<int, bool>)(value => throw fault))); break;
                default: task = ToTask(source.AverageAwaitAsync((Func<int, OnityTask<double>>)(value => throw fault))); break;
            }
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(Catch(() => task.GetAwaiter().GetResult()), Is.SameAs(fault));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void CleanupFailure_TakesPrecedenceOverEarlierFailureAndSuccess()
        {
            var cleanup = new InvalidOperationException("cleanup");
            var delegateFault = new InvalidOperationException("delegate");
            var succeeds = Items(1, 2);
            succeeds.DisposeFailure = cleanup;
            var success = succeeds.CountAsync();
            Assert.That(success.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(success)), Is.SameAs(cleanup));

            var fails = Items(1, 2);
            fails.DisposeFailure = cleanup;
            var failure = fails.CountAsync((Func<int, bool>)(value => throw delegateFault));
            Assert.That(failure.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(failure)), Is.SameAs(cleanup));

            var early = Items(1, 2);
            early.DisposeFailure = cleanup;
            var any = early.AnyAsync();
            Assert.That(Catch(() => Read(any)), Is.SameAs(cleanup));
            Assert.That(early.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void ReturnedCanceledOrFaultedDelegateTask_KeepsItsStatus()
        {
            var fault = new InvalidOperationException("task");
            var faulted = Items(1, 2).AnyAwaitAsync(value => OnityTask<bool>.FromException(fault));
            Assert.That(faulted.IsFaulted, Is.True);
            Assert.That(Catch(() => Read(faulted)), Is.SameAs(fault));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var canceled = Items(1, 2).SumAwaitAsync(value => OnityTask<int>.FromCanceled(cts.Token));
                Assert.That(canceled.IsCanceled, Is.True);
            }
        }

        [Test]
        public void PendingAwaitedPredicate_KeepsMovesSequential_AndCompletesWhenResolved()
        {
            var source = Items(1, 2, 3);
            var pending = new OnityTaskCompletionSource<bool>();
            int calls = 0;
            var task = source.AnyAwaitAsync(value =>
            {
                calls++;
                return calls == 1 ? pending.Task : Await(value == 2);
            });
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(source.Moves, Is.EqualTo(1));
            pending.TrySetResult(false);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.True);
            Assert.That(source.Moves, Is.EqualTo(2));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void PendingUpstreamMove_ResumesTheTerminalOperator()
        {
            var source = Items(1, 2, 3);
            source.PendingAt = 1;
            source.Pending = new OnityTaskCompletionSource<bool>();
            var task = source.ToListAsync();
            Assert.That(task.IsCompleted, Is.False);
            source.Pending.TrySetResult(true);
            LinqTestSupport.Wait(() => task.IsCompleted);
            Assert.That(Read(task), Is.EqualTo(new List<int> { 1, 2, 3 }));
            Assert.That(source.Disposals, Is.EqualTo(1));
        }

        private static OnityTask ToTask<T>(OnityTask<T> task)
        {
            return task.IsCompleted && !task.IsFaulted && !task.IsCanceled
                ? OnityTask.Completed : ConvertFailed(task);
        }

        private static OnityTask ConvertFailed<T>(OnityTask<T> task)
        {
            if (task.IsCanceled)
            {
                return OnityTask.FromCanceled(new CancellationToken(true));
            }
            if (task.IsFaulted)
            {
                Exception error = Catch(() => task.GetAwaiter().GetResult());
                return OnityTask.FromException(error);
            }
            return OnityTask.Completed;
        }

        private sealed class ModuloComparer : IEqualityComparer<int>
        {
            private readonly int m_modulo;

            internal ModuloComparer(int modulo)
            {
                m_modulo = modulo;
            }

            public bool Equals(int x, int y) => x % m_modulo == y % m_modulo;
            public int GetHashCode(int value) => value % m_modulo;
        }
    }
}
