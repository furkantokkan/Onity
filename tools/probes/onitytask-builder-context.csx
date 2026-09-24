using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// Run with the Mono and Roslyn csi bundled in Unity 2022.3.62f3.
// This probes the proposed continuation context wrapper, not Onity product code.
public static class OnityBuilderContextProbe
{
    private sealed class Gate
    {
        private Action m_continuation;

        public Awaiter GetAwaiter()
        {
            return new Awaiter(this);
        }

        public void Signal()
        {
            m_continuation();
        }

        public struct Awaiter : ICriticalNotifyCompletion
        {
            private readonly Gate m_gate;

            public Awaiter(Gate gate)
            {
                m_gate = gate;
            }

            public bool IsCompleted => false;

            public void GetResult()
            {
            }

            public void OnCompleted(Action continuation)
            {
                m_gate.m_continuation = continuation;
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                m_gate.m_continuation = continuation;
            }
        }
    }

    private sealed class NamedSynchronizationContext : SynchronizationContext
    {
        public NamedSynchronizationContext(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private static readonly List<string> s_trace = new List<string>();
    private static readonly AsyncLocal<string> s_local = new AsyncLocal<string>(change =>
        s_trace.Add("notify:" + change.PreviousValue + ">" + change.CurrentValue
            + ":" + change.ThreadContextChanged + ":sc=" + ContextName()));

    private static string ContextName()
    {
        NamedSynchronizationContext context =
            SynchronizationContext.Current as NamedSynchronizationContext;
        return context == null ? "null" : context.Name;
    }

    private static async Task<int> BaselineAsync(Gate gate)
    {
        s_local.Value = "method";
        await gate;
        s_trace.Add("move:" + s_local.Value + ":sc=" + ContextName());
        return 7;
    }

    public static string Observe(bool candidate)
    {
        SynchronizationContext previousContext = SynchronizationContext.Current;
        string previousValue = s_local.Value;

        try
        {
            SynchronizationContext.SetSynchronizationContext(
                new NamedSynchronizationContext("registration"));
            s_local.Value = "caller";

            Gate gate = new Gate();
            Task<int> baselineTask = null;
            if (candidate)
            {
                s_local.Value = "method";
                SynchronizationContext registrationContext = SynchronizationContext.Current;
                ExecutionContext capturedContext;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    capturedContext = ExecutionContext.Capture();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(registrationContext);
                }

                gate.GetAwaiter().UnsafeOnCompleted(() =>
                    ExecutionContext.Run(capturedContext, _ =>
                        s_trace.Add("move:" + s_local.Value + ":sc=" + ContextName()), null));
                s_local.Value = "caller";
            }
            else
            {
                baselineTask = BaselineAsync(gate);
            }

            SynchronizationContext.SetSynchronizationContext(
                new NamedSynchronizationContext("completion"));
            s_local.Value = "completion";
            s_trace.Clear();
            gate.Signal();

            if (baselineTask != null && baselineTask.GetAwaiter().GetResult() != 7)
            {
                throw new InvalidOperationException("Baseline returned an unexpected result.");
            }

            return string.Join("|", s_trace);
        }
        finally
        {
            s_local.Value = previousValue;
            SynchronizationContext.SetSynchronizationContext(previousContext);
            s_trace.Clear();
        }
    }
}

string baseline = OnityBuilderContextProbe.Observe(false);
string candidate = OnityBuilderContextProbe.Observe(true);
Console.WriteLine("BASELINE=" + baseline);
Console.WriteLine("CANDIDATE=" + candidate);

if (string.Equals(baseline, candidate, StringComparison.Ordinal))
{
    Console.WriteLine("MATCH: this probe found no continuation-context difference.");
}
else
{
    Console.WriteLine("NO-GO: AsyncLocal notification or MoveNext context differs.");
    Environment.Exit(1);
}
