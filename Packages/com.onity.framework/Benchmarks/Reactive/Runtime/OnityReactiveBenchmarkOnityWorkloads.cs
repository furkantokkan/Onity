using System;
using Onity.Reactive;

namespace Onity.Benchmarks
{
    /// <summary>
    /// Onity.Reactive implementations of the comparison scenarios. Only <c>Onity.Reactive</c> is imported
    /// here, so <c>Subject</c>, <c>ReactiveProperty</c>, <c>Where</c>, <c>Select</c> and <c>Subscribe</c>
    /// always bind to Onity's public API. <c>Subscribe(Action&lt;int&gt;)</c> is Onity's extension, which
    /// wraps the action in an <see cref="Observer{T}"/> delegate per subscription.
    /// </summary>
    internal static class OnityReactiveBenchmarkOnityWorkloads
    {
        internal const string LibraryName = "Onity";

        private static readonly Predicate<int> s_isEven = IsEven;
        private static readonly Func<int, int> s_twice = Twice;
        private static readonly Func<int, int, int> s_add = Add;

        /// <summary>Name and version of the assembly that implements the API under test.</summary>
        internal static string RuntimeAssembly => typeof(Subject<int>).Assembly.GetName().Name + " "
            + typeof(Subject<int>).Assembly.GetName().Version;

        internal static OnityReactiveBenchmarkWorkload Create(string scenarioId)
        {
            switch (scenarioId)
            {
                case OnityReactiveBenchmarkScenarios.SubjectOnNext1:
                    return new SubjectOnNextWorkload(1);
                case OnityReactiveBenchmarkScenarios.SubjectOnNext8:
                    return new SubjectOnNextWorkload(OnityReactiveBenchmarkGolden.ManySubscribers);
                case OnityReactiveBenchmarkScenarios.SubjectSubscribeDispose:
                    return new SubjectSubscribeDisposeWorkload();
                case OnityReactiveBenchmarkScenarios.PropertySetChanged:
                    return new PropertySetChangedWorkload();
                case OnityReactiveBenchmarkScenarios.PropertySetSame:
                    return new PropertySetSameWorkload();
                case OnityReactiveBenchmarkScenarios.PropertySubscribeDispose:
                    return new PropertySubscribeDisposeWorkload();
                case OnityReactiveBenchmarkScenarios.WhereSelectOnNext:
                    return new WhereSelectOnNextWorkload();
                case OnityReactiveBenchmarkScenarios.ChainSubscribeDispose:
                    return new ChainSubscribeDisposeWorkload();
                case OnityReactiveBenchmarkScenarios.CombineLatestOnNext:
                    return new CombineLatestOnNextWorkload();
                default:
                    throw new ArgumentException("Unknown reactive benchmark scenario: " + scenarioId, nameof(scenarioId));
            }
        }

        private static bool IsEven(int value)
        {
            return (value & 1) == 0;
        }

        private static int Twice(int value)
        {
            return value * 2;
        }

        private static int Add(int first, int second)
        {
            return first + second;
        }

        private sealed class SubjectOnNextWorkload : OnityReactiveBenchmarkWorkload
        {
            private readonly IDisposable[] m_subscriptions;
            private Subject<int> m_subject;

            internal SubjectOnNextWorkload(int subscriberCount)
            {
                m_subscriptions = new IDisposable[subscriberCount];
            }

            internal override void Setup()
            {
                m_subject = new Subject<int>();
                for (int i = 0; i < m_subscriptions.Length; i++)
                {
                    m_subscriptions[i] = m_subject.Subscribe(Sink.OnNext);
                }
            }

            internal override void Run(int operations)
            {
                Subject<int> subject = m_subject;
                for (int i = 0; i < operations; i++)
                {
                    subject.OnNext(i);
                }
            }

            internal override void Probe(int value)
            {
                m_subject.OnNext(value);
            }

            internal override void Teardown()
            {
                for (int i = 0; i < m_subscriptions.Length; i++)
                {
                    m_subscriptions[i].Dispose();
                    m_subscriptions[i] = null;
                }

                m_subject.Dispose();
                m_subject = null;
            }
        }

        private sealed class SubjectSubscribeDisposeWorkload : OnityReactiveBenchmarkWorkload
        {
            private Subject<int> m_subject;
            private IDisposable m_resident;

            internal override void Setup()
            {
                m_subject = new Subject<int>();
                m_resident = m_subject.Subscribe(Sink.OnNext);
            }

            internal override void Run(int operations)
            {
                Subject<int> subject = m_subject;
                Action<int> onNext = Sink.OnNext;
                for (int i = 0; i < operations; i++)
                {
                    subject.Subscribe(onNext).Dispose();
                }
            }

            internal override void Probe(int value)
            {
                m_subject.OnNext(value);
            }

            internal override void Teardown()
            {
                m_resident.Dispose();
                m_resident = null;
                m_subject.Dispose();
                m_subject = null;
            }
        }

        private sealed class PropertySetChangedWorkload : OnityReactiveBenchmarkWorkload
        {
            private ReactiveProperty<int> m_property;
            private IDisposable m_subscription;

            internal override void Setup()
            {
                m_property = new ReactiveProperty<int>(OnityReactiveBenchmarkGolden.ChangedInitialValue);
                m_subscription = m_property.Subscribe(Sink.OnNext);
            }

            internal override void Run(int operations)
            {
                ReactiveProperty<int> property = m_property;
                for (int i = 0; i < operations; i++)
                {
                    property.Value = i + 1;
                }
            }

            internal override void Probe(int value)
            {
                m_property.Value = value;
            }

            internal override void Teardown()
            {
                m_subscription.Dispose();
                m_subscription = null;
                m_property.Dispose();
                m_property = null;
            }
        }

        private sealed class PropertySetSameWorkload : OnityReactiveBenchmarkWorkload
        {
            private ReactiveProperty<int> m_property;
            private IDisposable m_subscription;

            internal override void Setup()
            {
                m_property = new ReactiveProperty<int>(OnityReactiveBenchmarkGolden.SameValue);
                m_subscription = m_property.Subscribe(Sink.OnNext);
            }

            internal override void Run(int operations)
            {
                ReactiveProperty<int> property = m_property;
                for (int i = 0; i < operations; i++)
                {
                    property.Value = OnityReactiveBenchmarkGolden.SameValue;
                }
            }

            internal override void Probe(int value)
            {
                m_property.Value = value;
            }

            internal override void Teardown()
            {
                m_subscription.Dispose();
                m_subscription = null;
                m_property.Dispose();
                m_property = null;
            }
        }

        private sealed class PropertySubscribeDisposeWorkload : OnityReactiveBenchmarkWorkload
        {
            private ReactiveProperty<int> m_property;

            internal override void Setup()
            {
                m_property = new ReactiveProperty<int>(OnityReactiveBenchmarkGolden.SubscribeValue);
            }

            internal override void Run(int operations)
            {
                ReactiveProperty<int> property = m_property;
                Action<int> onNext = Sink.OnNext;
                for (int i = 0; i < operations; i++)
                {
                    property.Subscribe(onNext).Dispose();
                }
            }

            internal override void Probe(int value)
            {
                m_property.Value = value;
            }

            internal override void Teardown()
            {
                m_property.Dispose();
                m_property = null;
            }
        }

        private sealed class WhereSelectOnNextWorkload : OnityReactiveBenchmarkWorkload
        {
            private Subject<int> m_subject;
            private IDisposable m_subscription;

            internal override void Setup()
            {
                m_subject = new Subject<int>();
                m_subscription = m_subject.Where(s_isEven).Select(s_twice).Subscribe(Sink.OnNext);
            }

            internal override void Run(int operations)
            {
                Subject<int> subject = m_subject;
                for (int i = 0; i < operations; i++)
                {
                    subject.OnNext(i);
                }
            }

            internal override void Probe(int value)
            {
                m_subject.OnNext(value);
            }

            internal override void Teardown()
            {
                m_subscription.Dispose();
                m_subscription = null;
                m_subject.Dispose();
                m_subject = null;
            }
        }

        private sealed class ChainSubscribeDisposeWorkload : OnityReactiveBenchmarkWorkload
        {
            private Subject<int> m_subject;

            internal override void Setup()
            {
                m_subject = new Subject<int>();
            }

            internal override void Run(int operations)
            {
                Subject<int> subject = m_subject;
                Predicate<int> isEven = s_isEven;
                Func<int, int> twice = s_twice;
                Action<int> onNext = Sink.OnNext;
                for (int i = 0; i < operations; i++)
                {
                    subject.Where(isEven).Select(twice).Subscribe(onNext).Dispose();
                }
            }

            internal override void Probe(int value)
            {
                m_subject.OnNext(value);
            }

            internal override void Teardown()
            {
                m_subject.Dispose();
                m_subject = null;
            }
        }

        private sealed class CombineLatestOnNextWorkload : OnityReactiveBenchmarkWorkload
        {
            private Subject<int> m_first;
            private Subject<int> m_second;
            private IDisposable m_subscription;

            internal override void Setup()
            {
                m_first = new Subject<int>();
                m_second = new Subject<int>();
                m_subscription = m_first.CombineLatest(m_second, s_add).Subscribe(Sink.OnNext);
                m_first.OnNext(OnityReactiveBenchmarkGolden.PrimeFirst);
                m_second.OnNext(OnityReactiveBenchmarkGolden.PrimeSecond);
            }

            internal override void Run(int operations)
            {
                Subject<int> first = m_first;
                Subject<int> second = m_second;
                for (int i = 0; i < operations; i++)
                {
                    if ((i & 1) == 0)
                    {
                        first.OnNext(i);
                    }
                    else
                    {
                        second.OnNext(i);
                    }
                }
            }

            internal override void Probe(int value)
            {
                m_first.OnNext(value);
            }

            internal override void Teardown()
            {
                m_subscription.Dispose();
                m_subscription = null;
                m_first.Dispose();
                m_first = null;
                m_second.Dispose();
                m_second = null;
            }
        }
    }
}
