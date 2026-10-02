using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Onity.Tests.UGUI.PlayMode
{
    /// <summary>
    /// Covers the uGUI adapters of the optional <c>Onity.Unity.UGUI</c> assembly: the component event waits,
    /// sequences and handlers, and the <c>BindTo</c> overloads. The engines behind them are covered by the
    /// Onity.Unity tests, so these tests check that each adapter listens to the right event and ends with the
    /// destroy of its component.
    /// </summary>
    public sealed class OnityUguiAsyncPlayModeTests
    {
        private readonly List<GameObject> m_objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < m_objects.Count; i++)
            {
                if (m_objects[i] != null)
                {
                    Object.Destroy(m_objects[i]);
                }
            }

            m_objects.Clear();
        }

        private T Create<T>()
            where T : Component
        {
            GameObject owner = new GameObject(typeof(T).Name);
            m_objects.Add(owner);
            return owner.AddComponent<T>();
        }

        [Test]
        public void Button_Click_WaitHandlerAndSequence()
        {
            Button button = Create<Button>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask withToken = button.OnClickAsync(source.Token);
                OnityTask onDestroy = button.OnClickAsync();
                IOnityAsyncClickEventHandler handler = button.GetAsyncClickEventHandler(source.Token);
                OnityTask handlerWait = handler.OnClickAsync();
                IOnityAsyncEnumerator<Unit> clicks = button.OnClickAsAsyncEnumerable(source.Token)
                    .GetAsyncEnumerator();
                OnityTask<bool> move = clicks.MoveNextAsync();

                button.onClick.Invoke();

                Assert.That(withToken.IsCompletedSuccessfully, Is.True);
                Assert.That(onDestroy.IsCompletedSuccessfully, Is.True);
                Assert.That(handlerWait.IsCompletedSuccessfully, Is.True);
                Assert.That(move.GetAwaiter().GetResult(), Is.True);

                handler.Dispose();
                clicks.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void Toggle_ValueChanged_ListensToOnValueChanged()
        {
            Toggle toggle = Create<Toggle>();
            CheckValueEvent(
                toggle.onValueChanged,
                true,
                token => toggle.OnValueChangedAsync(token),
                () => toggle.OnValueChangedAsync(),
                token => toggle.OnValueChangedAsAsyncEnumerable(token),
                () => toggle.OnValueChangedAsAsyncEnumerable(),
                token => toggle.GetAsyncValueChangedEventHandler(token),
                () => toggle.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void Scrollbar_ValueChanged_ListensToOnValueChanged()
        {
            Scrollbar scrollbar = Create<Scrollbar>();
            CheckValueEvent(
                scrollbar.onValueChanged,
                0.25f,
                token => scrollbar.OnValueChangedAsync(token),
                () => scrollbar.OnValueChangedAsync(),
                token => scrollbar.OnValueChangedAsAsyncEnumerable(token),
                () => scrollbar.OnValueChangedAsAsyncEnumerable(),
                token => scrollbar.GetAsyncValueChangedEventHandler(token),
                () => scrollbar.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void ScrollRect_ValueChanged_ListensToOnValueChanged()
        {
            ScrollRect scrollRect = Create<ScrollRect>();
            CheckValueEvent(
                scrollRect.onValueChanged,
                new Vector2(0.5f, 0.75f),
                token => scrollRect.OnValueChangedAsync(token),
                () => scrollRect.OnValueChangedAsync(),
                token => scrollRect.OnValueChangedAsAsyncEnumerable(token),
                () => scrollRect.OnValueChangedAsAsyncEnumerable(),
                token => scrollRect.GetAsyncValueChangedEventHandler(token),
                () => scrollRect.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void Slider_ValueChanged_ListensToOnValueChanged()
        {
            Slider slider = Create<Slider>();
            CheckValueEvent(
                slider.onValueChanged,
                0.6f,
                token => slider.OnValueChangedAsync(token),
                () => slider.OnValueChangedAsync(),
                token => slider.OnValueChangedAsAsyncEnumerable(token),
                () => slider.OnValueChangedAsAsyncEnumerable(),
                token => slider.GetAsyncValueChangedEventHandler(token),
                () => slider.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void Dropdown_ValueChanged_ListensToOnValueChanged()
        {
            Dropdown dropdown = Create<Dropdown>();
            CheckValueEvent(
                dropdown.onValueChanged,
                3,
                token => dropdown.OnValueChangedAsync(token),
                () => dropdown.OnValueChangedAsync(),
                token => dropdown.OnValueChangedAsAsyncEnumerable(token),
                () => dropdown.OnValueChangedAsAsyncEnumerable(),
                token => dropdown.GetAsyncValueChangedEventHandler(token),
                () => dropdown.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void InputField_ValueChanged_ListensToOnValueChanged()
        {
            InputField inputField = Create<InputField>();
            CheckValueEvent(
                inputField.onValueChanged,
                "typed",
                token => inputField.OnValueChangedAsync(token),
                () => inputField.OnValueChangedAsync(),
                token => inputField.OnValueChangedAsAsyncEnumerable(token),
                () => inputField.OnValueChangedAsAsyncEnumerable(),
                token => inputField.GetAsyncValueChangedEventHandler(token),
                () => inputField.GetAsyncValueChangedEventHandler());
        }

        [Test]
        public void InputField_EndEdit_ListensToOnEndEdit()
        {
            InputField inputField = Create<InputField>();
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask<string> withToken = inputField.OnEndEditAsync(source.Token);
                OnityTask<string> onDestroy = inputField.OnEndEditAsync();
                IOnityAsyncEndEditEventHandler<string> handler = inputField.GetAsyncEndEditEventHandler(source.Token);
                OnityTask<string> handlerWait = handler.OnEndEditAsync();
                IOnityAsyncEnumerator<string> edits = inputField.OnEndEditAsAsyncEnumerable(source.Token)
                    .GetAsyncEnumerator();
                OnityTask<bool> move = edits.MoveNextAsync();

                inputField.onValueChanged.Invoke("not an end edit");
                Assert.That(withToken.IsCompleted, Is.False, "value changes are not end edits");

                inputField.onEndEdit.Invoke("done");

                Assert.That(withToken.GetAwaiter().GetResult(), Is.EqualTo("done"));
                Assert.That(onDestroy.GetAwaiter().GetResult(), Is.EqualTo("done"));
                Assert.That(handlerWait.GetAwaiter().GetResult(), Is.EqualTo("done"));
                Assert.That(move.GetAwaiter().GetResult(), Is.True);
                Assert.That(edits.Current, Is.EqualTo("done"));

                handler.Dispose();
                edits.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void NullComponents_Throw()
        {
            Button button = null;
            Toggle toggle = null;
            InputField inputField = null;

            Assert.Throws<ArgumentNullException>(() => button.OnClickAsync());
            Assert.Throws<ArgumentNullException>(() => button.GetAsyncClickEventHandler());
            Assert.Throws<ArgumentNullException>(() => toggle.OnValueChangedAsAsyncEnumerable());
            Assert.Throws<ArgumentNullException>(() => inputField.OnEndEditAsync(CancellationToken.None));
        }

        [UnityTest]
        public IEnumerator DefaultOverloads_EndWhenTheComponentIsDestroyed()
        {
            Button button = Create<Button>();
            Slider slider = Create<Slider>();
            OnityTask click = button.OnClickAsync();
            OnityTask<float> change = slider.OnValueChangedAsync();
            IOnityAsyncEnumerator<Unit> clicks = button.OnClickAsAsyncEnumerable().GetAsyncEnumerator();
            OnityTask<bool> move = clicks.MoveNextAsync();
            IOnityAsyncClickEventHandler handler = button.GetAsyncClickEventHandler();
            OnityTask handlerWait = handler.OnClickAsync();
            yield return null;

            Assert.That(click.IsCompleted, Is.False);

            Object.Destroy(button.gameObject);
            Object.Destroy(slider.gameObject);
            for (int frame = 0; frame < 3 && !(click.IsCompleted && change.IsCompleted); frame++)
            {
                yield return null;
            }

            Assert.That(click.IsCanceled, Is.True);
            Assert.That(change.IsCanceled, Is.True);
            Assert.That(move.IsCanceled, Is.True);
            Assert.That(handlerWait.IsCanceled, Is.True);
        }

        [Test]
        public void BindTo_Text_FollowsTheSource_ForStringsAndGenericValues()
        {
            Text stringText = Create<Text>();
            Text numberText = Create<Text>();
            OnityAsyncReactiveProperty<string> strings = new OnityAsyncReactiveProperty<string>("first");
            OnityAsyncReactiveProperty<int> numbers = new OnityAsyncReactiveProperty<int>(7);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                strings.BindTo(stringText, cancellation.Token);
                numbers.BindTo(numberText, cancellation.Token);

                Assert.That(stringText.text, Is.EqualTo("first"));
                Assert.That(numberText.text, Is.EqualTo("7"));

                strings.Value = "second";
                numbers.Value = 12;

                Assert.That(stringText.text, Is.EqualTo("second"));
                Assert.That(numberText.text, Is.EqualTo("12"));

                cancellation.Cancel();
                strings.Value = "third";
                numbers.Value = 99;

                Assert.That(stringText.text, Is.EqualTo("second"));
                Assert.That(numberText.text, Is.EqualTo("12"));
            }
        }

        [Test]
        public void BindTo_Text_NullItem_SetsAnEmptyText()
        {
            Text text = Create<Text>();
            OnityAsyncReactiveProperty<object> source = new OnityAsyncReactiveProperty<object>("x");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                source.BindTo(text, cancellation.Token);
                Assert.That(text.text, Is.EqualTo("x"));

                source.Value = null;

                Assert.That(text.text, Is.Empty);
            }
        }

        [Test]
        public void BindTo_Selectable_FollowsTheFlag()
        {
            Button button = Create<Button>();
            OnityAsyncReactiveProperty<bool> source = new OnityAsyncReactiveProperty<bool>(false);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                source.BindTo(button, cancellation.Token);
                Assert.That(button.interactable, Is.False);

                source.Value = true;
                Assert.That(button.interactable, Is.True);

                cancellation.Cancel();
                source.Value = false;
                Assert.That(button.interactable, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator BindTo_DefaultOverload_EndsSilentlyWhenTheComponentIsDestroyed()
        {
            Text text = Create<Text>();
            Button button = Create<Button>();
            OnityAsyncReactiveProperty<string> strings = new OnityAsyncReactiveProperty<string>("a");
            OnityAsyncReactiveProperty<bool> flags = new OnityAsyncReactiveProperty<bool>(true);
            strings.BindTo(text);
            flags.BindTo(button);
            Assert.That(text.text, Is.EqualTo("a"));
            yield return null;

            Object.Destroy(text.gameObject);
            Object.Destroy(button.gameObject);
            yield return null;
            yield return null;

            // A binding that outlived its component would log a MissingReferenceException here.
            strings.Value = "b";
            flags.Value = false;
            yield return null;
        }

        [Test]
        public void BindTo_NullArguments_Throw()
        {
            OnityAsyncReactiveProperty<string> source = new OnityAsyncReactiveProperty<string>("x");

            Assert.Throws<ArgumentNullException>(() => source.BindTo((Text)null));
            Assert.Throws<ArgumentNullException>(() => ((IOnityAsyncEnumerable<string>)null).BindTo(Create<Text>()));
            Assert.Throws<ArgumentNullException>(
                () => new OnityAsyncReactiveProperty<bool>(true).BindTo((Selectable)null));
        }

        private static void CheckValueEvent<T>(
            UnityEvent<T> unityEvent,
            T value,
            Func<CancellationToken, OnityTask<T>> oneShot,
            Func<OnityTask<T>> oneShotOnDestroy,
            Func<CancellationToken, IOnityAsyncEnumerable<T>> sequence,
            Func<IOnityAsyncEnumerable<T>> sequenceOnDestroy,
            Func<CancellationToken, IOnityAsyncValueChangedEventHandler<T>> handler,
            Func<IOnityAsyncValueChangedEventHandler<T>> handlerOnDestroy)
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                OnityTask<T> withToken = oneShot(source.Token);
                OnityTask<T> onDestroy = oneShotOnDestroy();
                IOnityAsyncValueChangedEventHandler<T> withTokenHandler = handler(source.Token);
                IOnityAsyncValueChangedEventHandler<T> onDestroyHandler = handlerOnDestroy();
                OnityTask<T> withTokenHandlerWait = withTokenHandler.OnValueChangedAsync();
                OnityTask<T> onDestroyHandlerWait = onDestroyHandler.OnValueChangedAsync();
                IOnityAsyncEnumerator<T> withTokenSequence = sequence(source.Token).GetAsyncEnumerator();
                IOnityAsyncEnumerator<T> onDestroySequence = sequenceOnDestroy().GetAsyncEnumerator();
                OnityTask<bool> withTokenMove = withTokenSequence.MoveNextAsync();
                OnityTask<bool> onDestroyMove = onDestroySequence.MoveNextAsync();

                unityEvent.Invoke(value);

                Assert.That(withToken.GetAwaiter().GetResult(), Is.EqualTo(value));
                Assert.That(onDestroy.GetAwaiter().GetResult(), Is.EqualTo(value));
                Assert.That(withTokenHandlerWait.GetAwaiter().GetResult(), Is.EqualTo(value));
                Assert.That(onDestroyHandlerWait.GetAwaiter().GetResult(), Is.EqualTo(value));
                Assert.That(withTokenMove.GetAwaiter().GetResult(), Is.True);
                Assert.That(withTokenSequence.Current, Is.EqualTo(value));
                Assert.That(onDestroyMove.GetAwaiter().GetResult(), Is.True);
                Assert.That(onDestroySequence.Current, Is.EqualTo(value));

                withTokenHandler.Dispose();
                onDestroyHandler.Dispose();
                withTokenSequence.DisposeAsync().GetAwaiter().GetResult();
                onDestroySequence.DisposeAsync().GetAwaiter().GetResult();
            }
        }
    }
}
