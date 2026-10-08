using System.Collections;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Onity.Tests.PlayMode
{
    /// <summary>
    /// Delivers real UI Toolkit events through a runtime panel hosted by a <c>UIDocument</c>. The tests
    /// report themselves as inconclusive when the run cannot create a runtime panel.
    /// </summary>
    public sealed class OnityUIToolkitAsyncPlayModeTests
    {
        public sealed class PingEvent : EventBase<PingEvent>
        {
        }

        private GameObject m_host;
        private PanelSettings m_settings;
        private VisualElement m_root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_settings = ScriptableObject.CreateInstance<PanelSettings>();
            m_host = new GameObject("uitk-async-host");
            UIDocument document = m_host.AddComponent<UIDocument>();
            document.panelSettings = m_settings;
            yield return null;
            yield return null;

            m_root = document.rootVisualElement;
            if (m_root == null || m_root.panel == null)
            {
                Assert.Inconclusive("This run cannot create a UI Toolkit runtime panel.");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (m_host != null)
            {
                Object.Destroy(m_host);
            }

            if (m_settings != null)
            {
                Object.Destroy(m_settings);
            }

            m_host = null;
            m_settings = null;
            m_root = null;
        }

        [UnityTest]
        public IEnumerator OnEventAsync_CompletesWithTheEvent_AndStopsListening()
        {
            VisualElement element = new VisualElement();
            m_root.Add(element);
            VisualElement target = null;
            bool resumed = false;
            OnityTaskAwaiter<PingEvent> awaiter = element.OnEventAsync<PingEvent>().GetAwaiter();
            awaiter.OnCompleted(() =>
            {
                target = awaiter.GetResult().target as VisualElement;
                resumed = true;
            });

            Send(element);
            for (int frame = 0; frame < 3 && !resumed; frame++)
            {
                yield return null;
            }

            Assert.That(resumed, Is.True);
            Assert.That(target, Is.SameAs(element), "the event is valid while the continuation runs inline");

            // A second event finds no listener: a fresh wait is still pending after it.
            OnityTask<PingEvent> second = element.OnEventAsync<PingEvent>();
            Assert.That(second.IsCompleted, Is.False);
            Send(element);
            for (int frame = 0; frame < 3 && !second.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(second.IsCompletedSuccessfully, Is.True);
        }

        [UnityTest]
        public IEnumerator OnEventAsAsyncEnumerable_YieldsEachEventWhileAMoveIsPending()
        {
            VisualElement element = new VisualElement();
            m_root.Add(element);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<PingEvent> enumerator = element.OnEventAsAsyncEnumerable<PingEvent>(source.Token)
                    .GetAsyncEnumerator();

                for (int i = 0; i < 3; i++)
                {
                    OnityTask<bool> move = enumerator.MoveNextAsync();
                    Send(element);
                    for (int frame = 0; frame < 3 && !move.IsCompleted; frame++)
                    {
                        yield return null;
                    }

                    Assert.That(move.IsCompletedSuccessfully, Is.True);
                    Assert.That(move.GetAwaiter().GetResult(), Is.True);
                }

                OnityTask<bool> last = enumerator.MoveNextAsync();
                source.Cancel();

                Assert.That(last.IsCanceled, Is.True);
                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [UnityTest]
        public IEnumerator OnValueChangedAsync_CompletesWithTheNewValue()
        {
            TextField field = new TextField();
            m_root.Add(field);
            OnityTask<string> wait = field.OnValueChangedAsync();

            field.value = "hello";
            for (int frame = 0; frame < 3 && !wait.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            Assert.That(wait.GetAwaiter().GetResult(), Is.EqualTo("hello"));
        }

        [UnityTest]
        public IEnumerator OnValueChangedAsAsyncEnumerable_YieldsEachNewValue()
        {
            IntegerField field = new IntegerField();
            m_root.Add(field);
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                IOnityAsyncEnumerator<int> enumerator = field.OnValueChangedAsAsyncEnumerable(source.Token)
                    .GetAsyncEnumerator();

                for (int value = 1; value <= 3; value++)
                {
                    OnityTask<bool> move = enumerator.MoveNextAsync();
                    field.value = value * 5;
                    for (int frame = 0; frame < 3 && !move.IsCompleted; frame++)
                    {
                        yield return null;
                    }

                    Assert.That(move.GetAwaiter().GetResult(), Is.True);
                    Assert.That(enumerator.Current, Is.EqualTo(value * 5));
                }

                enumerator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [UnityTest]
        public IEnumerator OnClickAsync_CompletesWhenTheButtonIsClicked()
        {
            Button button = new Button();
            m_root.Add(button);
            OnityTask click = button.OnClickAsync();
            IOnityAsyncEnumerator<Unit> clicks = button.OnClickAsAsyncEnumerable().GetAsyncEnumerator();
            OnityTask<bool> move = clicks.MoveNextAsync();

            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }

            for (int frame = 0; frame < 3 && !(click.IsCompleted && move.IsCompleted); frame++)
            {
                yield return null;
            }

            Assert.That(click.IsCompletedSuccessfully, Is.True);
            Assert.That(move.GetAwaiter().GetResult(), Is.True);
            clicks.DisposeAsync().GetAwaiter().GetResult();
        }

        [UnityTest]
        public IEnumerator BindTo_FollowsTheSourceWhileAttached_AndStopsWhenDetached()
        {
            OnityAsyncReactiveProperty<string> source = new OnityAsyncReactiveProperty<string>("a");
            Label label = new Label();
            m_root.Add(label);
            int changeEvents = 0;
            label.RegisterValueChangedCallback(_ => changeEvents++);

            source.BindTo(label);
            Assert.That(label.text, Is.EqualTo("a"));

            source.Value = "b";
            Assert.That(label.text, Is.EqualTo("b"));
            Assert.That(changeEvents, Is.EqualTo(0), "the binding does not raise ChangeEvent");

            label.RemoveFromHierarchy();
            yield return null;
            yield return null;

            source.Value = "c";
            Assert.That(label.text, Is.EqualTo("b"), "the detached element is unbound");
        }

        [UnityTest]
        public IEnumerator BindTo_ValueControl_FollowsTheSource()
        {
            OnityAsyncReactiveProperty<int> source = new OnityAsyncReactiveProperty<int>(4);
            SliderInt slider = new SliderInt(0, 100);
            m_root.Add(slider);
            int changeEvents = 0;
            slider.RegisterValueChangedCallback(_ => changeEvents++);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                source.BindTo(slider, cancellation.Token);
                Assert.That(slider.value, Is.EqualTo(4));

                source.Value = 40;
                yield return null;
                yield return null;

                Assert.That(slider.value, Is.EqualTo(40));
                Assert.That(changeEvents, Is.EqualTo(0), "the binding sets the value without notifying");

                slider.value = 55;
                for (int frame = 0; frame < 3 && changeEvents == 0; frame++)
                {
                    yield return null;
                }

                Assert.That(changeEvents, Is.EqualTo(1), "a user change still raises the event");
            }
        }

        private static void Send(VisualElement element)
        {
            using (PingEvent evt = PingEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }
    }
}
