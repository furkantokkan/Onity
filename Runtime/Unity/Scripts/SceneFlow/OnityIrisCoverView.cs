using UnityEngine;
using UnityEngine.UIElements;

namespace Onity.Unity.SceneFlow
{
    /// <summary>
    /// A circle-wipe ("iris") cover: the <c>onity-iris-cover</c> element of its <see cref="UIDocument" /> draws the
    /// whole panel in one color with a circular hole that closes on <see cref="Center" /> and opens again.
    /// </summary>
    /// <remarks>
    /// Build the document from <c>OnityIrisCover.uxml</c>. The element's USS background previews the closed cover in
    /// UI Builder; at runtime the view clears that background and draws the cover with <see cref="Painter2D" />, a
    /// panel rectangle and a circle filled with <see cref="FillRule.OddEven" />. It repaints only while the cover
    /// changes.
    /// </remarks>
    public sealed class OnityIrisCoverView : OnitySceneCoverView
    {
        /// <summary>
        /// Name of the cover element in <c>OnityIrisCover.uxml</c>.
        /// </summary>
        public const string IrisElementName = "onity-iris-cover";

        [Header("Iris")]
        [Tooltip("Color of the cover around the hole.")]
        [SerializeField] private Color m_color = Color.black;

        [Tooltip("Where the hole closes, normalized over the panel: (0, 0) is the top left, (1, 1) the bottom right.")]
        [SerializeField] private Vector2 m_center = new Vector2(0.5f, 0.5f);

        private float m_drawnProgress;

        /// <summary>
        /// Gets or sets where the hole closes, normalized over the panel: (0, 0) is the top left and (1, 1) the
        /// bottom right. Values outside 0..1 close the hole off screen.
        /// </summary>
        public Vector2 Center
        {
            get => m_center;
            set
            {
                m_center = value;
                Element?.MarkDirtyRepaint();
            }
        }

        /// <summary>
        /// Gets the color of the cover around the hole.
        /// </summary>
        public Color Color => m_color;

        /// <inheritdoc />
        protected override string ElementName => IrisElementName;

        /// <summary>
        /// Returns the hole radius for a panel: from the distance between the center and the panel's farthest corner
        /// at progress 0 (nothing covered) down to 0 at progress 1 (fully covered).
        /// </summary>
        /// <param name="size">Panel size.</param>
        /// <param name="normalizedCenter">Hole center, normalized over the panel.</param>
        /// <param name="progress">0 is clear and 1 is fully covered; clamped to 0..1.</param>
        /// <returns>The hole radius in panel units.</returns>
        public static float CalculateHoleRadius(Vector2 size, Vector2 normalizedCenter, float progress)
        {
            float centerX = size.x * normalizedCenter.x;
            float centerY = size.y * normalizedCenter.y;
            float farthestX = Mathf.Max(Mathf.Abs(centerX), Mathf.Abs(size.x - centerX));
            float farthestY = Mathf.Max(Mathf.Abs(centerY), Mathf.Abs(size.y - centerY));
            float farthestCorner = Mathf.Sqrt((farthestX * farthestX) + (farthestY * farthestY));
            return farthestCorner * (1f - Mathf.Clamp01(progress));
        }

        /// <inheritdoc />
        protected override void OnBind(VisualElement element)
        {
            element.style.backgroundColor = Color.clear;
            element.generateVisualContent += DrawCover;
        }

        /// <inheritdoc />
        protected override void OnUnbind(VisualElement element)
        {
            element.generateVisualContent -= DrawCover;
            element.style.backgroundColor = StyleKeyword.Null;
        }

        /// <inheritdoc />
        protected override void ApplyProgress(float progress)
        {
            // Repaint only when the drawn cover changes.
            if (m_drawnProgress == progress)
            {
                return;
            }

            m_drawnProgress = progress;
            Element.MarkDirtyRepaint();
        }

        private void DrawCover(MeshGenerationContext context)
        {
            Rect rect = context.visualElement.contentRect;

            if (m_drawnProgress <= 0f || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.fillColor = m_color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();

            float radius = CalculateHoleRadius(rect.size, m_center, m_drawnProgress);

            if (radius > 0f)
            {
                Vector2 center = new Vector2(rect.xMin + (rect.width * m_center.x), rect.yMin + (rect.height * m_center.y));
                painter.MoveTo(new Vector2(center.x + radius, center.y));
                painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(180f));
                painter.Arc(center, radius, Angle.Degrees(180f), Angle.Degrees(360f));
                painter.ClosePath();
            }

            painter.Fill(FillRule.OddEven);
        }
    }
}
