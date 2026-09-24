#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>
    /// Letter spacing for a one-line legacy <see cref="Text"/>, which has none of its own: moves each glyph right by
    /// <see cref="Em"/> times the font size per character before it. Like CSS letter-spacing, the space also follows the
    /// last character, so centered and right-aligned text shifts back by half or all of it.
    /// </summary>
    [RequireComponent(typeof(Text))]
    internal sealed class Tracking : BaseMeshEffect
    {
        [SerializeField] private float _em;

        public float Em
        {
            get => _em;
            set
            {
                _em = value;
                if (graphic != null)
                {
                    graphic.SetVerticesDirty();
                }
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || _em == 0f || !(graphic is Text text) || text.text.Length == 0)
            {
                return;
            }

            string value = text.text;
            float step = _em * text.fontSize;
            float shift = AlignmentShift(text.alignment) * step * value.Length;
            float pixels = text.pixelsPerUnit;
            int quads = vh.currentVertCount / 4;

            // Whitespace may have no quad of its own; then quads follow the other characters in order.
            bool quadPerCharacter = quads == value.Length;
            int character = 0;
            var vertex = new UIVertex();
            for (int quad = 0; quad < quads; quad++)
            {
                if (!quadPerCharacter)
                {
                    while (character < value.Length && char.IsWhiteSpace(value[character]))
                    {
                        character++;
                    }
                }

                // Whole screen pixels, so glyphs stay on the pixel grid and sharp.
                float dx = Mathf.Round((shift + step * character) * pixels) / pixels;
                for (int i = quad * 4; i < quad * 4 + 4; i++)
                {
                    vh.PopulateUIVertex(ref vertex, i);
                    vertex.position.x += dx;
                    vh.SetUIVertex(vertex, i);
                }

                character++;
            }
        }

        /// <summary>The share of the added width that moves the text back: 0 left, 1/2 centered, 1 right.</summary>
        private static float AlignmentShift(TextAnchor alignment)
        {
            switch (alignment)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return -0.5f;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return -1f;
                default:
                    return 0f;
            }
        }
    }
}
