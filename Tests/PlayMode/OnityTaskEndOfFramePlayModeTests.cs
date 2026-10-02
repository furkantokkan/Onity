using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Rendering;

namespace Onity.Tests.PlayMode
{
    public sealed class OnityTaskEndOfFramePlayModeTests
    {
        [Test]
        public void ActualRenderingGuard_PrecedesPrecancellation_AndDoesNotCreateHost()
        {
            FieldInfo host = typeof(OnityTaskPlayerLoop).GetField("s_endOfFrameRunner", BindingFlags.Static | BindingFlags.NonPublic);
            object baseline = host.GetValue(null);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                if ((Application.isEditor && Application.isBatchMode)
                    || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    Assert.Throws<PlatformNotSupportedException>(() => OnityTask.WaitForEndOfFrame());
                    Assert.Throws<PlatformNotSupportedException>(() => OnityTask.WaitForEndOfFrame(cancellation.Token));
                }
                else
                {
                    var task = OnityTask.WaitForEndOfFrame(cancellation.Token);
                    Assert.That(task.IsCanceled, Is.True);
                    Assert.That(Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult())
                        .CancellationToken, Is.EqualTo(cancellation.Token));
                }
                Assert.That(host.GetValue(null), Is.SameAs(baseline));
            }
        }

        [Test]
        public void WorkerAndClosedSession_RejectBeforeRenderingAndPrecancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Task<Exception> worker = Task.Run(() => Catch(() => OnityTask.WaitForEndOfFrame(cancellation.Token)));
                Assert.That(worker.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(worker.Result, Is.TypeOf<InvalidOperationException>());
                try
                {
                    typeof(OnityTaskPlayerLoop).GetMethod("CloseSession", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, null);
                    Assert.Throws<InvalidOperationException>(() => OnityTask.WaitForEndOfFrame());
                    Assert.Throws<InvalidOperationException>(() => OnityTask.WaitForEndOfFrame(cancellation.Token));
                }
                finally
                {
                    typeof(OnityTaskPlayerLoop).GetMethod("BeginSession", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { true });
                    OnityTaskPlayerLoop.Initialize();
                }
            }
        }

        private static Exception Catch(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
