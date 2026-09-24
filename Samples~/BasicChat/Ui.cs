#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>The design's color tokens, light scheme.</summary>
    internal static class Palette
    {
        public static readonly Color Paper = Hex(0xfefefe);
        public static readonly Color Ink = Hex(0x1e1e1e);
        public static readonly Color InkSoft = Hex(0x1e1e1e, 0.86f);
        public static readonly Color Muted = Hex(0x5c5c5c);
        public static readonly Color Placeholder = Hex(0x737373);
        public static readonly Color Grey1 = Hex(0xdedede);
        public static readonly Color Grey2 = Hex(0xc4c4c4);
        public static readonly Color Hairline = Hex(0x1e1e1e, 0.25f);

        /// <summary>The hairline over paper, for borders drawn as a full rect under a paper fill.</summary>
        public static readonly Color HairlineOnPaper = Color.Lerp(Paper, Ink, 0.25f);
        public static readonly Color OnFrameMeta = Hex(0xfefefe, 0.72f);
        public static readonly Color Pink = Hex(0xf386a1);
        public static readonly Color Hover = Hex(0xf9c3d2);
        public static readonly Color RowHover = Hex(0xfcedf1);
        public static readonly Color Caret = Hex(0xd45bb6);
        public static readonly Color Dot = Hex(0x1e1e1e, 0.3f);
        public static readonly Color Ok = Hex(0x1f7a3a);
        public static readonly Color Warn = Hex(0xa14d08);
        public static readonly Color Danger = Hex(0xc62828);

        private static Color Hex(int rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);
        }
    }

    /// <summary>
    /// Creates and places UI objects. Positions are in reference pixels from the parent's top-left corner, y down, and
    /// are rounded to whole screen pixels so 1px lines stay sharp.
    /// </summary>
    internal static class Ui
    {
        private static readonly TextGenerator s_generator = new TextGenerator();

        /// <summary>Screen pixels per reference pixel: the canvas scale factor.</summary>
        public static float Scale { get; private set; } = 1f;

        /// <summary>A 1px border in reference pixels: whole screen pixels rounded down, at least one, as browsers draw borders.</summary>
        public static float Line { get; private set; } = 1f;

        /// <summary>
        /// A 1px decoration or 1px box (strikethrough, bracket, meter gap) in reference pixels: whole screen pixels
        /// rounded to nearest, at least one, as browsers draw them.
        /// </summary>
        public static float Stroke { get; private set; } = 1f;

        public static void SetScale(float scale)
        {
            Scale = scale > 0f ? scale : 1f;
            Line = Mathf.Max(1f, Mathf.Floor(Scale)) / Scale;
            Stroke = Mathf.Max(1f, Mathf.Round(Scale)) / Scale;
        }

        public static float Snap(float value)
        {
            return Mathf.Round(value * Scale) / Scale;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            return rect;
        }

        public static Image NewImage(string name, Transform parent, Color color)
        {
            var image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static RawImage NewRawImage(string name, Transform parent, Texture texture, Color color)
        {
            var image = NewRect(name, parent).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A one-line label; <paramref name="trackingEm"/> is letter spacing in ems, as in CSS.</summary>
        public static Text NewText(string name, Transform parent, Font font, int size, Color color, float trackingEm = 0f)
        {
            var text = NewRect(name, parent).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            if (trackingEm != 0f)
            {
                text.gameObject.AddComponent<Tracking>().Em = trackingEm;
            }

            return text;
        }

        /// <summary>Places a rect and returns its size after rounding.</summary>
        public static Vector2 Place(RectTransform rect, float x, float y, float width, float height)
        {
            float left = Snap(x);
            float top = Snap(y);
            var size = new Vector2(Mathf.Max(0f, Snap(x + width) - left), Mathf.Max(0f, Snap(y + height) - top));
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = size;
            return size;
        }

        public static Vector2 Place(Graphic graphic, float x, float y, float width, float height)
        {
            return Place(graphic.rectTransform, x, y, width, height);
        }

        /// <summary>Stretches a rect over its parent, <paramref name="inset"/> in from each edge (negative reaches out).</summary>
        public static void Fill(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Show(Component component, bool visible)
        {
            if (component.gameObject.activeSelf != visible)
            {
                component.gameObject.SetActive(visible);
            }
        }

        /// <summary>Width of <paramref name="value"/> set in <paramref name="style"/>'s font and size, letter spacing included.</summary>
        public static float Measure(Text style, string value)
        {
            if (value.Length == 0)
            {
                return 0f;
            }

            TextGenerationSettings settings = style.GetGenerationSettings(Vector2.zero);
            float tracking = style.TryGetComponent(out Tracking t) ? t.Em * style.fontSize * value.Length : 0f;
            return s_generator.GetPreferredWidth(value, settings) / style.pixelsPerUnit + tracking;
        }

        /// <summary>Width of the text a label shows.</summary>
        public static float Width(Text text)
        {
            return Measure(text, text.text);
        }

        /// <summary>
        /// <paramref name="value"/> cut to fit <paramref name="width"/> with "…" appended, as CSS text-overflow: ellipsis
        /// does; unchanged when it fits.
        /// </summary>
        public static string Ellipsize(Text style, string value, float width)
        {
            // Double a prefix until it overflows, so a long value is never measured whole.
            int over = Mathf.Min(value.Length, 32);
            while (Measure(style, value.Substring(0, over)) <= width)
            {
                if (over == value.Length)
                {
                    return value;
                }

                over = Mathf.Min(value.Length, 2 * over);
            }

            // The longest prefix that fits with "…" is shorter than the one that overflows without it.
            int low = 0;
            int high = over - 1;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Measure(style, value.Substring(0, mid) + "…") <= width)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return value.Substring(0, low) + "…";
        }

        /// <summary>
        /// Fills <paramref name="starts"/> with the index where each of the first <paramref name="maxLines"/> lines of
        /// <paramref name="value"/> starts when it wraps at <paramref name="firstWidth"/> for its first line and
        /// <paramref name="width"/> after that.
        /// </summary>
        public static void BreakLines(Text style, string value, float firstWidth, float width, int maxLines, List<int> starts)
        {
            starts.Clear();
            starts.Add(0);
            int start = 0;
            float lineWidth = firstWidth;
            while (starts.Count < maxLines)
            {
                int next = SecondLineStart(style, start == 0 ? value : value.Substring(start), lineWidth);
                if (next <= 0 || start + next >= value.Length)
                {
                    return;
                }

                start += next;
                starts.Add(start);
                lineWidth = width;
            }
        }

        /// <summary>Where the second line starts when <paramref name="value"/> wraps at <paramref name="width"/>; 0 when it fits on one.</summary>
        private static int SecondLineStart(Text style, string value, float width)
        {
            TextGenerationSettings settings = style.GetGenerationSettings(new Vector2(Mathf.Max(1f, width), 0f));
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;
            settings.textAnchor = TextAnchor.UpperLeft;
            settings.generateOutOfBounds = true;
            settings.updateBounds = false;
            s_generator.Populate(value, settings);
            IList<UILineInfo> lines = s_generator.lines;
            return lines.Count > 1 ? lines[1].startCharIdx : 0;
        }
    }

    /// <summary>
    /// Textures drawn in code: the halftone dot, the stage fade, the select triangle and the dashed border. Created once
    /// per enabled sample and destroyed with its UI.
    /// </summary>
    internal sealed class SampleTextures
    {
        /// <summary>Width of the stage fade in reference pixels.</summary>
        public const int FadeWidth = 32;

        /// <summary>
        /// One 4px halftone tile in screen pixels: a 1px square in its top-left corner, white, tinted with the dot color.
        /// <see cref="SetHalftoneScale"/> redraws it for the canvas scale.
        /// </summary>
        public readonly Texture2D Halftone;

        /// <summary>
        /// A white nine-slice sprite whose alpha rises to 1 over its 32px border, the complement of the stage's fade
        /// mask: drawn in the page color over the halftone, it fades the dots toward the stage edges.
        /// </summary>
        public readonly Sprite Fade;

        /// <summary>A downward triangle, white on transparent, with mipmaps so it stays smooth at any scale.</summary>
        public readonly Texture2D Triangle;

        /// <summary>Dash patterns, 3 texels on and 3 off: across for top and bottom edges, down for the sides.</summary>
        public readonly Texture2D DashAcross;
        public readonly Texture2D DashDown;

        private readonly Texture2D _fadeTexture;
        private bool _halftoneReady;

        public SampleTextures()
        {
            Halftone = NewTexture("Halftone", 4, 4, false, FilterMode.Bilinear, TextureWrapMode.Repeat);
            SetHalftoneScale(1f);

            int size = FadeWidth * 2 + 2;
            _fadeTexture = NewTexture("Stage fade", size, size, false, FilterMode.Bilinear, TextureWrapMode.Clamp);
            var fade = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = 1f - Ramp(x, size) * Ramp(y, size);
                    fade[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            _fadeTexture.SetPixels32(fade);
            _fadeTexture.Apply(false, true);
            Fade = Sprite.Create(_fadeTexture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(FadeWidth, FadeWidth, FadeWidth, FadeWidth));
            Fade.name = "Stage fade";

            Triangle = NewTexture("Select triangle", 32, 20, true, FilterMode.Trilinear, TextureWrapMode.Clamp);
            var triangle = new Color32[32 * 20];
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    triangle[y * 32 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(TriangleCoverage(x, 19 - y) * 255f));
                }
            }

            Triangle.SetPixels32(triangle);
            Triangle.Apply(true, true);

            var dash = new Color32[6];
            for (int i = 0; i < dash.Length; i++)
            {
                dash[i] = new Color32(255, 255, 255, (byte)(i < 3 ? 255 : 0));
            }

            DashAcross = NewTexture("Dash across", 6, 1, false, FilterMode.Point, TextureWrapMode.Repeat);
            DashAcross.SetPixels32(dash);
            DashAcross.Apply(false, true);
            DashDown = NewTexture("Dash down", 1, 6, false, FilterMode.Point, TextureWrapMode.Repeat);
            DashDown.SetPixels32(dash);
            DashDown.Apply(false, true);
        }

        /// <summary>
        /// Redraws the halftone tile at <paramref name="scale"/> screen pixels per reference pixel, with the dot's partly
        /// covered edge pixels in between, as a browser draws a 1px square at that scale. A tile of whole texels at the
        /// screen's own pixel size keeps every dot the same; a 4x4 tile sampled at a fractional scale would draw some
        /// dots one pixel wide and others two.
        /// </summary>
        public void SetHalftoneScale(float scale)
        {
            int size = Mathf.Max(4, Mathf.RoundToInt(4f * scale));
            if (size == Halftone.width && _halftoneReady)
            {
                return;
            }

            float dot = size / 4f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Texture rows run bottom up, so the tile's top row is the last one.
                    float coverage = Mathf.Clamp01(dot - x) * Mathf.Clamp01(dot - (size - 1 - y));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                }
            }

            Halftone.Reinitialize(size, size);
            Halftone.SetPixels32(pixels);
            Halftone.Apply(false, false);
            _halftoneReady = true;
        }

        public void Destroy()
        {
            Object.Destroy(Fade);
            Object.Destroy(_fadeTexture);
            Object.Destroy(Halftone);
            Object.Destroy(Triangle);
            Object.Destroy(DashAcross);
            Object.Destroy(DashDown);
        }

        private static Texture2D NewTexture(string name, int width, int height, bool mipmaps, FilterMode filter, TextureWrapMode wrap)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, mipmaps)
            {
                name = name,
                filterMode = filter,
                wrapMode = wrap,
                hideFlags = HideFlags.DontSave,
            };
        }

        /// <summary>The stage mask along one axis at texel <paramref name="i"/>: 0 at the edge, 1 from 32px in.</summary>
        private static float Ramp(int i, int size)
        {
            float fromEdge = Mathf.Min(i, size - 1 - i) + 0.5f;
            return Mathf.Clamp01(fromEdge / FadeWidth);
        }

        /// <summary>How much of texel (x, y), y down, the triangle (0, 0), (32, 0), (16, 20) covers, from 4x4 samples.</summary>
        private static float TriangleCoverage(int x, int y)
        {
            int inside = 0;
            for (int sy = 0; sy < 4; sy++)
            {
                for (int sx = 0; sx < 4; sx++)
                {
                    float px = x + (sx + 0.5f) / 4f;
                    float py = y + (sy + 0.5f) / 4f;
                    float halfWidth = 16f * (1f - py / 20f);
                    if (Mathf.Abs(px - 16f) <= halfWidth)
                    {
                        inside++;
                    }
                }
            }

            return inside / 16f;
        }
    }
}
