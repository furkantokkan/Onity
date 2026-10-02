using System;
using System.Collections.Generic;
using NUnit.Framework;
using Onity.Unity.Async;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="OnityProgress"/>: the float reporter, the generic reporter, and the
    /// reporter that forwards only changed values.
    /// </summary>
    [TestFixture]
    public sealed class OnityProgressEditModeTests
    {
        private sealed class CaseInsensitiveComparer : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(string obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
            }
        }

        [Test]
        public void Create_Float_ForwardsEveryValueInline()
        {
            List<float> received = new List<float>();
            Action<float> callback = received.Add;

            IProgress<float> progress = OnityProgress.Create(callback);
            progress.Report(0.25f);
            progress.Report(0.25f);
            progress.Report(1f);

            Assert.That(received, Is.EqualTo(new[] { 0.25f, 0.25f, 1f }));
        }

        [Test]
        public void Create_Float_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityProgress.Create((Action<float>)null));
        }

        [Test]
        public void Create_Generic_ForwardsEveryValueOfAnyType()
        {
            List<string> received = new List<string>();
            List<Vector2> vectors = new List<Vector2>();

            IProgress<string> text = OnityProgress.Create<string>(received.Add);
            IProgress<Vector2> vector = OnityProgress.Create<Vector2>(vectors.Add);
            text.Report("a");
            text.Report("a");
            vector.Report(new Vector2(1f, 2f));

            Assert.That(received, Is.EqualTo(new[] { "a", "a" }));
            Assert.That(vectors, Is.EqualTo(new[] { new Vector2(1f, 2f) }));
        }

        [Test]
        public void Create_Generic_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityProgress.Create<int>(null));
        }

        [Test]
        public void Create_Generic_RunsTheCallbackOnTheReportingThread()
        {
            int callbackThread = 0;
            IProgress<int> progress = OnityProgress.Create<int>(_ => callbackThread = Environment.CurrentManagedThreadId);

            progress.Report(1);

            Assert.That(callbackThread, Is.EqualTo(Environment.CurrentManagedThreadId));
        }

        [Test]
        public void CreateOnlyValueChanged_ForwardsTheFirstValueAndEveryChange()
        {
            List<int> received = new List<int>();

            IProgress<int> progress = OnityProgress.CreateOnlyValueChanged<int>(received.Add);
            progress.Report(0);
            progress.Report(0);
            progress.Report(1);
            progress.Report(1);
            progress.Report(0);

            Assert.That(received, Is.EqualTo(new[] { 0, 1, 0 }));
        }

        [Test]
        public void CreateOnlyValueChanged_FirstValueIsForwardedEvenWhenItIsTheDefault()
        {
            List<float> received = new List<float>();

            OnityProgress.CreateOnlyValueChanged<float>(received.Add).Report(0f);

            Assert.That(received, Is.EqualTo(new[] { 0f }));
        }

        [Test]
        public void CreateOnlyValueChanged_UsesTheSuppliedComparer()
        {
            List<string> received = new List<string>();

            IProgress<string> progress =
                OnityProgress.CreateOnlyValueChanged<string>(received.Add, new CaseInsensitiveComparer());
            progress.Report("Loading");
            progress.Report("LOADING");
            progress.Report("Done");

            Assert.That(received, Is.EqualTo(new[] { "Loading", "Done" }));
        }

        [Test]
        public void CreateOnlyValueChanged_StructValues_UseValueEquality()
        {
            List<Vector3> received = new List<Vector3>();

            IProgress<Vector3> progress = OnityProgress.CreateOnlyValueChanged<Vector3>(received.Add);
            progress.Report(new Vector3(1f, 2f, 3f));
            progress.Report(new Vector3(1f, 2f, 3f));
            progress.Report(new Vector3(1f, 2f, 4f));

            Assert.That(received.Count, Is.EqualTo(2));
        }

        [Test]
        public void CreateOnlyValueChanged_NullCallback_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => OnityProgress.CreateOnlyValueChanged<int>(null));
        }
    }
}
