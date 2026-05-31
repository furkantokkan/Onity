using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Onity.Unity.Contexts;
using UnityEngine;
using OnityApi = Onity.Unity.Onity;

namespace Onity.Tests.EditMode
{
    public sealed class OnityEventShortcutTests
    {
        private readonly List<GameObject> m_roots = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = m_roots.Count - 1; i >= 0; i--)
            {
                if (m_roots[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(m_roots[i]);
                }
            }

            m_roots.Clear();
        }

        [Test]
        public void PublishAndSubscribe_UseActiveSceneContext()
        {
            CreateContext<SceneContext>("EventShortcutSceneContext");
            int received = 0;
            IDisposable subscription = OnityApi.Subscribe<ShortcutMessage>(
                message => received = message.Value);

            try
            {
                OnityApi.Publish(new ShortcutMessage(7));
            }
            finally
            {
                subscription.Dispose();
            }

            Assert.That(received, Is.EqualTo(7));
        }

        [Test]
        public void ComponentPublishAndSubscribe_UseNearestGameObjectContext()
        {
            SceneContext sceneContext = CreateContext<SceneContext>("EventShortcutSceneContext");
            GameObject objectContextRoot = CreateGameObject("EventShortcutObjectContext");
            objectContextRoot.transform.SetParent(sceneContext.transform);
            GameObjectContext objectContext = objectContextRoot.AddComponent<GameObjectContext>();
            InvokeAwake(objectContext);

            GameObject ownerObject = CreateGameObject("EventShortcutOwner");
            ownerObject.transform.SetParent(objectContextRoot.transform);
            Transform owner = ownerObject.transform;
            int received = 0;
            IDisposable subscription = OnityApi.Subscribe<ShortcutMessage>(
                owner,
                message => received = message.Value);

            try
            {
                OnityApi.Publish(owner, new ShortcutMessage(11));
            }
            finally
            {
                subscription.Dispose();
            }

            Assert.That(received, Is.EqualTo(11));
        }

        private TContext CreateContext<TContext>(string name)
            where TContext : OnityContext
        {
            GameObject root = CreateGameObject(name);
            TContext context = root.AddComponent<TContext>();
            InvokeAwake(context);
            return context;
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            m_roots.Add(gameObject);
            return gameObject;
        }

        private static void InvokeAwake(OnityContext context)
        {
            MethodInfo awake = typeof(OnityContext).GetMethod(
                "Awake",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(awake, Is.Not.Null);
            awake.Invoke(context, null);
        }

        private readonly struct ShortcutMessage
        {
            public ShortcutMessage(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }
    }
}
