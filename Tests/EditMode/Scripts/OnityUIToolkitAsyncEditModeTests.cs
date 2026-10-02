using System;
using System.Threading;
using NUnit.Framework;
using Onity.Core;
using Onity.Unity.Async;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers the UI Toolkit adapters that do not need a panel: argument checks, token handling and the
    /// <c>BindTo</c> bindings. Event delivery through a real panel is covered by the Play mode tests.
    /// </summary>
    public sealed class OnityUIToolkitAsyncEditModeTests
    {
        private sealed class PlainValueControl : INotifyValueChanged<int>
        {
            public int value { get; set; }

            public void SetValueWithoutNotify(int newValue)
            {
                value = newValue;
            }
        }

        private sealed class ScriptedSource : IOnityAsyncEnumerable<string>
        {
            private readonly string[][] m_runs;
            private int m_next;

            // Each run is one enumerator: the first entry "!" faults the first move, otherwise the
            // entries are yielded and the enumeration ends.
            public ScriptedSource(params string[][] runs)
            {
                m_runs = runs;
            }

            public int DisposedEnumerators { get; private set; }

            public IOnityAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                string[] run = m_runs[Math.Min(m_next, m_runs.Length - 1)];
                m_next++;
                return new ScriptedEnumerator(this, run);
            }

            private sealed class ScriptedEnumerator : IOnityAsyncEnumerator<string>
            {
                private readonly ScriptedSource m_owner;
                private readonly string[] m_run;
                private int m_index;

                public ScriptedEnumerator(ScriptedSource owner, string[] run)
                {
                    m_owner = owner;
                    m_run = run;
                }

                public string Current { get; private set; }

                public OnityTask<bool> MoveNextAsync()
                {
                    if (m_index >= m_run.Length)
                    {
                        return OnityTask<bool>.FromResult(false);
                    }

                    string entry = m_run[m_index++];
                    if (entry == "!")
                    {
                        return OnityTask<bool>.FromException(new InvalidOperationException("scripted fault"));
                    }

                    Current = entry;
                    return OnityTask<bool>.FromResult(true);
                }

                public OnityTask DisposeAsync()
                {
                    m_owner.DisposedEnumerators++;
                    return OnityTask.CompletedTask;
                }
            }
        }

        [Test]
        public void NullArguments_Throw()
        {
            Button button = null;
            VisualElement element = null;
            INotifyValueChanged<int> control = null;

            Assert.Throws<ArgumentNullException>(() => button.OnClickAsync());
            Assert.Throws<ArgumentNullException>(() => button.OnClickAsAsyncEnumerable());
            Assert.Throws<ArgumentNullException>(() => element.OnEventAsync<ClickEvent>());
            Assert.Throws<ArgumentNullException>(() => element.OnEventAsAsyncEnumerable<ClickEvent>());
            Assert.Throws<ArgumentNullException>(() => control.OnValueChangedAsync());
            Assert.Throws<ArgumentNullException>(() => control.OnValueChangedAsAsyncEnumerable());
            Assert.Throws<ArgumentNullException>(
                () => new OnityAsyncReactiveProperty<string>("x").BindTo((TextElement)null));
            Assert.Throws<ArgumentNullException>(() => ((IOnityAsyncEnumerable<string>)null).BindTo(new Label()));
        }

        [Test]
        public void OnValueChangedAsync_ControlThatIsNotAnElement_Throws()
        {
            PlainValueControl control = new PlainValueControl();

            Assert.Throws<ArgumentException>(() => control.OnValueChangedAsync());
            Assert.Throws<ArgumentException>(() => control.OnValueChangedAsAsyncEnumerable());
        }

        [Test]
        public void Waits_PreCanceledToken_AreCanceled()
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();
                Button button = new Button();
                TextField field = new TextField();

                Assert.That(button.OnClickAsync(source.Token).IsCanceled, Is.True);
                Assert.That(button.OnEventAsync<ClickEvent>(source.Token).IsCanceled, Is.True);
                Assert.That(field.OnValueChangedAsync(source.Token).IsCanceled, Is.True);
            }
        }

        [Test]
        public void Enumerators_PreCanceledToken_ReturnACanceledMove()
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                source.Cancel();
                Button button = new Button();
                TextField field = new TextField();

                Assert.That(
                    button.OnClickAsAsyncEnumerable(source.Token).GetAsyncEnumerator().MoveNextAsync().IsCanceled,
                    Is.True);
                Assert.That(
                    button.OnEventAsAsyncEnumerable<ClickEvent>(source.Token).GetAsyncEnumerator().MoveNextAsync()
                        .IsCanceled,
                    Is.True);
                Assert.That(
                    field.OnValueChangedAsAsyncEnumerable(source.Token).GetAsyncEnumerator().MoveNextAsync()
                        .IsCanceled,
                    Is.True);
            }
        }

        [Test]
        public void Waits_CancelWhileWaiting_CancelsTheWait()
        {
            using (CancellationTokenSource source = new CancellationTokenSource())
            {
                Button button = new Button();
                TextField field = new TextField();
                OnityTask click = button.OnClickAsync(source.Token);
                OnityTask<ClickEvent> evt = button.OnEventAsync<ClickEvent>(source.Token);
                OnityTask<string> changed = field.OnValueChangedAsync(source.Token);
                Assert.That(click.IsCompleted, Is.False);
                Assert.That(evt.IsCompleted, Is.False);
                Assert.That(changed.IsCompleted, Is.False);

                source.Cancel();

                Assert.That(click.IsCanceled, Is.True);
                Assert.That(evt.IsCanceled, Is.True);
                Assert.That(changed.IsCanceled, Is.True);
            }
        }

        [Test]
        public void BindTo_String_SetsTheTextForEveryValue_AndStopsWithTheToken()
        {
            OnityAsyncReactiveProperty<string> source = new OnityAsyncReactiveProperty<string>("first");
            Label label = new Label();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                source.BindTo(label, cancellation.Token);

                Assert.That(label.text, Is.EqualTo("first"));

                source.Value = "second";
                Assert.That(label.text, Is.EqualTo("second"));

                cancellation.Cancel();
                source.Value = "third";
                Assert.That(label.text, Is.EqualTo("second"));
            }
        }

        [Test]
        public void BindTo_TextElementTypedArgument_SetsTheText_ForStringsAndOtherValues()
        {
            // A statically typed TextElement resolves to the text overload, a Label to the value overload;
            // both set the text the same way.
            TextElement stringElement = new Label();
            TextElement numberElement = new Button();
            OnityAsyncReactiveProperty<string> strings = new OnityAsyncReactiveProperty<string>("one");
            OnityAsyncReactiveProperty<int> numbers = new OnityAsyncReactiveProperty<int>(5);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                strings.BindTo(stringElement, cancellation.Token);
                numbers.BindTo(numberElement, cancellation.Token);

                Assert.That(stringElement.text, Is.EqualTo("one"));
                Assert.That(numberElement.text, Is.EqualTo("5"));

                strings.Value = "two";
                numbers.Value = 6;

                Assert.That(stringElement.text, Is.EqualTo("two"));
                Assert.That(numberElement.text, Is.EqualTo("6"));
            }
        }

        [Test]
        public void BindTo_Generic_UsesToString_AndNullBecomesEmpty()
        {
            OnityAsyncReactiveProperty<int> number = new OnityAsyncReactiveProperty<int>(7);
            OnityAsyncReactiveProperty<object> nullable = new OnityAsyncReactiveProperty<object>("x");
            Label numberLabel = new Label();
            Button nullableButton = new Button();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                number.BindTo(numberLabel, cancellation.Token);
                nullable.BindTo(nullableButton, cancellation.Token);

                Assert.That(numberLabel.text, Is.EqualTo("7"));
                Assert.That(nullableButton.text, Is.EqualTo("x"));

                number.Value = 12;
                nullable.Value = null;

                Assert.That(numberLabel.text, Is.EqualTo("12"));
                Assert.That(nullableButton.text ?? string.Empty, Is.Empty);
            }
        }

        [Test]
        public void BindTo_Value_UsesSetValueWithoutNotify()
        {
            OnityAsyncReactiveProperty<int> source = new OnityAsyncReactiveProperty<int>(3);
            IntegerField field = new IntegerField();
            int changeEvents = 0;
            field.RegisterValueChangedCallback(_ => changeEvents++);
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                source.BindTo(field, cancellation.Token);

                Assert.That(field.value, Is.EqualTo(3));

                source.Value = 9;

                Assert.That(field.value, Is.EqualTo(9));
                Assert.That(changeEvents, Is.EqualTo(0));
            }
        }

        [Test]
        public void BindTo_PreCanceledToken_NeverTouchesTheElement()
        {
            OnityAsyncReactiveProperty<string> source = new OnityAsyncReactiveProperty<string>("value");
            Label label = new Label();
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                source.BindTo(label, cancellation.Token);
                source.Value = "changed";

                Assert.That(label.text ?? string.Empty, Is.Empty);
            }
        }

        [Test]
        public void BindTo_SourceThatEnds_StopsAndDisposesItsEnumerator()
        {
            ScriptedSource source = new ScriptedSource(new[] { "a", "b" });
            Label label = new Label();

            source.BindTo(label);

            Assert.That(label.text, Is.EqualTo("b"));
            Assert.That(source.DisposedEnumerators, Is.EqualTo(1));
        }

        [Test]
        public void BindTo_FaultWithRebind_RetriesOnceWithANewEnumerator()
        {
            ScriptedSource source = new ScriptedSource(new[] { "!" }, new[] { "ok" });
            Label label = new Label();

            source.BindTo(label);

            Assert.That(label.text, Is.EqualTo("ok"));
            Assert.That(source.DisposedEnumerators, Is.EqualTo(2));
        }

        [Test]
        public void BindTo_SecondConsecutiveFault_IsLogged_AndStopsTheBinding()
        {
            ScriptedSource source = new ScriptedSource(new[] { "!" }, new[] { "!" });
            Label label = new Label();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted fault"));

            source.BindTo(label);

            Assert.That(label.text ?? string.Empty, Is.Empty);
            Assert.That(source.DisposedEnumerators, Is.EqualTo(2));
        }

        [Test]
        public void BindTo_FaultWithoutRebind_IsLoggedOnce()
        {
            ScriptedSource source = new ScriptedSource(new[] { "!" }, new[] { "ok" });
            Label label = new Label();
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted fault"));

            source.BindTo(label, false);

            Assert.That(label.text ?? string.Empty, Is.Empty);
            Assert.That(source.DisposedEnumerators, Is.EqualTo(1));
        }
    }
}
