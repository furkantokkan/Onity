using NUnit.Framework;
using Onity.Unity.SceneFlow;
using UnityEngine;

namespace Onity.Tests.EditMode
{
    [TestFixture]
    public sealed class OnityIrisCoverGeometryTests
    {
        private const float k_tolerance = 0.001f;

        private static readonly Vector2[] s_sizes =
        {
            new Vector2(1080f, 1920f),
            new Vector2(1920f, 1080f),
            new Vector2(800f, 800f),
            new Vector2(2400f, 1080f)
        };

        private static readonly Vector2[] s_centers =
        {
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, 0f),
            new Vector2(0.2f, 0.8f),
            new Vector2(1f, 0.3f)
        };

        [Test]
        public void CalculateHoleRadius_Clear_ReachesTheFarthestCorner()
        {
            foreach (Vector2 size in s_sizes)
            {
                foreach (Vector2 center in s_centers)
                {
                    Vector2 point = new Vector2(size.x * center.x, size.y * center.y);
                    float farthest = FarthestCornerDistance(size, point);

                    float radius = OnityIrisCoverView.CalculateHoleRadius(size, center, 0f);

                    Assert.That(radius, Is.EqualTo(farthest).Within(k_tolerance), $"size {size}, center {center}");
                }
            }
        }

        [Test]
        public void CalculateHoleRadius_FullyCovered_IsZero()
        {
            foreach (Vector2 size in s_sizes)
            {
                foreach (Vector2 center in s_centers)
                {
                    Assert.That(OnityIrisCoverView.CalculateHoleRadius(size, center, 1f), Is.EqualTo(0f));
                }
            }
        }

        [Test]
        public void CalculateHoleRadius_ShrinksMonotonicallyWithProgress()
        {
            foreach (Vector2 size in s_sizes)
            {
                float previous = float.MaxValue;

                for (int step = 0; step <= 20; step++)
                {
                    float radius = OnityIrisCoverView.CalculateHoleRadius(size, new Vector2(0.3f, 0.6f), step / 20f);

                    Assert.That(radius, Is.LessThanOrEqualTo(previous));
                    previous = radius;
                }
            }
        }

        [Test]
        public void CalculateHoleRadius_ClampsProgress()
        {
            Vector2 size = new Vector2(1000f, 500f);
            Vector2 center = new Vector2(0.5f, 0.5f);

            Assert.That(OnityIrisCoverView.CalculateHoleRadius(size, center, -1f),
                Is.EqualTo(OnityIrisCoverView.CalculateHoleRadius(size, center, 0f)));
            Assert.That(OnityIrisCoverView.CalculateHoleRadius(size, center, 2f), Is.EqualTo(0f));
        }

        [Test]
        public void CalculateHoleRadius_CenterOutsideThePanel_StillUncoversEveryCorner()
        {
            Vector2 size = new Vector2(1000f, 500f);
            Vector2 center = new Vector2(-0.5f, 1.5f);
            Vector2 point = new Vector2(size.x * center.x, size.y * center.y);

            float radius = OnityIrisCoverView.CalculateHoleRadius(size, center, 0f);

            Assert.That(radius, Is.EqualTo(FarthestCornerDistance(size, point)).Within(k_tolerance));
        }

        private static float FarthestCornerDistance(Vector2 size, Vector2 point)
        {
            float farthest = 0f;
            farthest = Mathf.Max(farthest, Vector2.Distance(point, new Vector2(0f, 0f)));
            farthest = Mathf.Max(farthest, Vector2.Distance(point, new Vector2(size.x, 0f)));
            farthest = Mathf.Max(farthest, Vector2.Distance(point, new Vector2(0f, size.y)));
            farthest = Mathf.Max(farthest, Vector2.Distance(point, new Vector2(size.x, size.y)));
            return farthest;
        }
    }
}
