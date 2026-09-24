#nullable enable
using System.Collections.Generic;
using ChatGuard.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>The fonts the UI sets its text in, from <see cref="BasicChatFonts"/>.</summary>
    internal sealed class FontSet
    {
        public FontSet(BasicChatFonts fonts)
        {
            // Unity 2022.2 renamed the built-in UI font; each name throws or returns null on the other side of that release.
#if UNITY_2022_2_OR_NEWER
            Font builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            Font builtIn = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            Display = fonts.display != null ? fonts.display : builtIn;
            Text = fonts.text != null ? fonts.text : builtIn;
            TextMedium = fonts.textMedium != null ? fonts.textMedium : Text;
            TextSemiBold = fonts.textSemiBold != null ? fonts.textSemiBold : Text;
            Mono = fonts.mono != null ? fonts.mono : builtIn;
            MonoMedium = fonts.monoMedium != null ? fonts.monoMedium : Mono;
        }

        public Font Display { get; }
        public Font Text { get; }
        public Font TextMedium { get; }
        public Font TextSemiBold { get; }
        public Font Mono { get; }
        public Font MonoMedium { get; }
    }

    /// <summary>
    /// Vertical metrics of the bundled fonts, in ems, to put a baseline where a browser puts it: in a line box, the
    /// ascent plus descent is centered and the baseline sits the ascent below its top.
    /// </summary>
    internal static class Metrics
    {
        /// <summary>Inter's ascent plus descent (hhea and typo metrics agree).</summary>
        public const float TextContent = 1.21f;
        public const float TextAscent = 0.96875f;

        /// <summary>JetBrains Mono's ascent plus descent.</summary>
        public const float MonoContent = 1.32f;
        public const float MonoAscent = 1.02f;

        /// <summary>Inter's baseline below the top of a line box of <paramref name="lineHeight"/>.</summary>
        public static float TextBaseline(int size, float lineHeight)
        {
            return (lineHeight - TextContent * size) / 2f + TextAscent * size;
        }

        /// <summary>JetBrains Mono's baseline below the top of a line box of <paramref name="lineHeight"/>.</summary>
        public static float MonoBaseline(int size, float lineHeight)
        {
            return (lineHeight - MonoContent * size) / 2f + MonoAscent * size;
        }
    }

    /// <summary>A rectangle with a 1px border: a border-colored image with a fill image inset by one line.</summary>
    internal sealed class Box
    {
        public Box(string name, Transform parent, Color border, Color fill)
        {
            Border = Ui.NewImage(name, parent, border);
            Fill = Ui.NewImage("Fill", Border.transform, fill);
        }

        public Image Border { get; }
        public Image Fill { get; }
        public RectTransform Rect => Border.rectTransform;

        public Vector2 Place(float x, float y, float width, float height)
        {
            Vector2 size = Ui.Place(Border, x, y, width, height);
            Ui.Place(Fill, Ui.Line, Ui.Line, size.x - 2f * Ui.Line, size.y - 2f * Ui.Line);
            return size;
        }
    }

    /// <summary>
    /// The design's window: a 1px ink frame, a 28px ink title bar with a mono title on the left and meta on the right,
    /// and a paper body. Content goes in <see cref="Body"/>, whose size is <see cref="BodySize"/>.
    /// </summary>
    internal sealed class Window
    {
        public const float BarHeight = 28f;
        private const float Inset = 16f;

        private readonly Text _title;
        private readonly Text _meta;

        public Window(string title, string meta, Transform parent, FontSet fonts)
        {
            Frame = new Box(title, parent, Palette.Ink, Palette.Paper);
            Bar = Ui.NewImage("Bar", Frame.Border.transform, Palette.Ink);
            _title = Ui.NewText("Title", Bar.transform, fonts.Mono, 12, Palette.Paper, 0.01f);
            _title.text = title;
            _meta = Ui.NewText("Meta", Bar.transform, fonts.Mono, 12, Palette.OnFrameMeta, 0.01f);
            _meta.text = meta;
            Body = Ui.NewRect("Body", Frame.Border.transform);
        }

        public Box Frame { get; }
        public Image Bar { get; }
        public RectTransform Body { get; }
        public Vector2 BodySize { get; private set; }

        public string Meta
        {
            get => _meta.text;
            set => _meta.text = value;
        }

        public void Place(float x, float y, float width, float height)
        {
            Vector2 size = Frame.Place(x, y, width, height);
            float inner = size.x - 2f * Ui.Line;
            Ui.Place(Bar, Ui.Line, Ui.Line, inner, BarHeight);
            Ui.Place(_title, Inset, 6f, inner - 2f * Inset, 16f);
            float metaWidth = Ui.Width(_meta);
            Ui.Place(_meta, inner - Inset - metaWidth, 6f, metaWidth, 16f);
            BodySize = Ui.Place(Body, Ui.Line, Ui.Line + BarHeight, inner, size.y - 2f * Ui.Line - BarHeight);
        }
    }

    /// <summary>
    /// An action badge: 11px mono in a 1px frame, at least 44px wide and 18px tall. Weight follows strictness: allow is
    /// quiet, flag an ink outline, hide an amber outline, block a solid red block.
    /// </summary>
    internal sealed class Badge
    {
        /// <summary>A 16px mono line between two borders.</summary>
        public static float Height => 16f + 2f * Ui.Line;

        /// <summary>The label's baseline below the badge's top.</summary>
        public static float Baseline => Ui.Line + Metrics.MonoBaseline(11, 16f);

        private readonly Box _box;
        private readonly Text _label;

        public Badge(Transform parent, FontSet fonts)
        {
            _box = new Box("Badge", parent, Palette.Ink, Palette.Paper);
            _label = Ui.NewText("Action", _box.Border.transform, fonts.Mono, 11, Palette.Ink, 0.02f);
            _label.alignment = TextAnchor.MiddleCenter;
        }

        public float Width { get; private set; }

        public void Show(bool visible)
        {
            Ui.Show(_box.Border, visible);
        }

        public void Set(ModerationAction action)
        {
            _label.text = ModerationActions.ToWireName(action);
            switch (action)
            {
                case ModerationAction.Allow:
                    Paint(Palette.HairlineOnPaper, Palette.Paper, Palette.Muted);
                    break;
                case ModerationAction.Flag:
                    Paint(Palette.Ink, Palette.Paper, Palette.Ink);
                    break;
                case ModerationAction.Hide:
                    Paint(Palette.Warn, Palette.Paper, Palette.Warn);
                    break;
                default:
                    Paint(Palette.Danger, Palette.Danger, Palette.Paper);
                    break;
            }

            Width = Mathf.Max(44f, Ui.Width(_label) + 12f + 2f * Ui.Line);
        }

        public void Place(float x, float y)
        {
            Vector2 size = _box.Place(x, y, Width, Height);
            Ui.Place(_label, 0f, Ui.Line, size.x, 16f);
        }

        private void Paint(Color border, Color fill, Color label)
        {
            _box.Border.color = border;
            _box.Fill.color = fill;
            _label.color = label;
        }
    }

    /// <summary>
    /// Text set as one-line labels, one per line, so each line sits in its own line box as in the design's CSS and the
    /// first can start after an inline name. Lines past <see cref="MaxLines"/> are cut with "…". Struck text draws a
    /// 1px line through each line.
    /// </summary>
    internal sealed class Paragraph
    {
        private readonly Text[] _lines;
        private readonly Image[] _strikes;
        private readonly List<int> _starts = new List<int>(8);

        public Paragraph(string name, Transform parent, Font font, int size, Color color, int maxLines)
        {
            Root = Ui.NewRect(name, parent);
            _lines = new Text[maxLines];
            _strikes = new Image[maxLines];
            for (int i = 0; i < maxLines; i++)
            {
                _lines[i] = Ui.NewText("Line", Root, font, size, color);
            }

            for (int i = 0; i < maxLines; i++)
            {
                _strikes[i] = Ui.NewImage("Strike", Root, Palette.Muted);
                _strikes[i].gameObject.SetActive(false);
            }
        }

        public RectTransform Root { get; }

        /// <summary>The first line's label, whose font and size every line shares.</summary>
        public Text Style => _lines[0];
        public int MaxLines => _lines.Length;
        public int LineCount { get; private set; }

        public Color Color
        {
            set
            {
                foreach (Text line in _lines)
                {
                    line.color = value;
                }
            }
        }

        public int FontSize
        {
            set
            {
                foreach (Text line in _lines)
                {
                    line.fontSize = value;
                }
            }
        }

        public TextAnchor Alignment
        {
            set
            {
                foreach (Text line in _lines)
                {
                    line.alignment = value;
                }
            }
        }

        /// <summary>
        /// Sets <paramref name="value"/> wrapped to <paramref name="width"/>, its first line starting
        /// <paramref name="indent"/> in, with <paramref name="lineHeight"/> per line, and returns the height used. Place
        /// <see cref="Root"/> at the paragraph's top-left first. With <paramref name="wrap"/> off it keeps one line and
        /// cuts it with "…".
        /// </summary>
        public float Set(string value, float width, float indent, float lineHeight, bool wrap = true, bool struck = false)
        {
            Text style = _lines[0];
            if (wrap)
            {
                Ui.BreakLines(style, value, width - indent, width, _lines.Length, _starts);
            }
            else
            {
                _starts.Clear();
                _starts.Add(0);
            }

            LineCount = _starts.Count;
            for (int i = 0; i < _lines.Length; i++)
            {
                Text line = _lines[i];
                bool used = i < LineCount;
                Ui.Show(line, used);
                if (!used)
                {
                    Ui.Show(_strikes[i], false);
                    continue;
                }

                bool last = i == LineCount - 1;
                int start = _starts[i];
                int end = last ? value.Length : _starts[i + 1];
                float x = i == 0 ? indent : 0f;
                string part = value.Substring(start, end - start).Trim();
                line.text = last ? Ui.Ellipsize(style, part, width - x) : part;
                Ui.Place(line, x, i * lineHeight, width - x, lineHeight);

                Ui.Show(_strikes[i], struck);
                if (struck)
                {
                    // Where browsers draw line-through: three eighths of the ascent above the baseline.
                    float baseline = i * lineHeight + Metrics.TextBaseline(line.fontSize, lineHeight);
                    float middle = baseline - 0.375f * Metrics.TextAscent * line.fontSize;
                    float textWidth = Mathf.Min(Ui.Width(line), width - x);
                    float left = line.alignment == TextAnchor.MiddleCenter ? x + (width - x - textWidth) / 2f : x;
                    Ui.Place(_strikes[i], left, middle - Ui.Stroke / 2f, textWidth, Ui.Stroke);
                }
            }

            return LineCount * lineHeight;
        }
    }

    /// <summary>
    /// A score bar: 10px tall in a 1px ink frame, a fill that grows from the left, and paper gaps at the quarters. The
    /// fill eases to a new score over 320 ms.
    /// </summary>
    internal sealed class Meter
    {
        private const float Duration = 0.32f;

        private readonly Box _box;
        private readonly Image _fill;
        private readonly Image[] _quarters = new Image[3];
        private float _from;
        private float _to;
        private float _started = -1f;

        public Meter(Transform parent)
        {
            _box = new Box("Meter", parent, Palette.Ink, Palette.Paper);
            _fill = Ui.NewImage("Score", _box.Fill.transform, Palette.Grey2);
            Ui.Fill(_fill.rectTransform);
            _fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            _fill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
            for (int i = 0; i < _quarters.Length; i++)
            {
                _quarters[i] = Ui.NewImage("Quarter", _box.Fill.transform, Palette.Paper);
            }
        }

        public void Place(float x, float y, float width)
        {
            Vector2 size = _box.Place(x, y, width, 10f);
            float inner = size.x - 2f * Ui.Line;
            for (int i = 0; i < _quarters.Length; i++)
            {
                Ui.Place(_quarters[i], inner * (i + 1) / 4f, 0f, Ui.Stroke, size.y - 2f * Ui.Line);
            }
        }

        public void Set(float score, Color tone, float now)
        {
            _fill.color = tone;
            float target = Mathf.Clamp01(score);
            if (Mathf.Approximately(target, _to))
            {
                return;
            }

            _from = _fill.rectTransform.localScale.x;
            _to = target;
            _started = now;
        }

        /// <summary>Advances the fill; true while it is still moving.</summary>
        public bool Tick(float now)
        {
            if (_started < 0f)
            {
                return false;
            }

            float t = Mathf.Clamp01((now - _started) / Duration);
            float eased = 1f - Mathf.Pow(1f - t, 4f);
            _fill.rectTransform.localScale = new Vector3(Mathf.Lerp(_from, _to, eased), 1f, 1f);
            if (t >= 1f)
            {
                _started = -1f;
            }

            return _started >= 0f;
        }
    }

    /// <summary>
    /// A threshold slider: a 3px track filled in ink up to the value over gray, and a square 16px ink thumb, in a 2px
    /// paper ring and a 1px ink ring, that turns pink under the pointer. Values snap to steps of 0.05.
    /// </summary>
    internal sealed class ThresholdSlider
    {
        private readonly Text _label;
        private readonly Text _value;
        private readonly RectTransform _hit;
        private readonly Image _rail;
        private readonly RectTransform _fillArea;
        private readonly RectTransform _handleArea;
        private readonly Image _outer;
        private readonly Image _ring;

        /// <summary>The value shown and last reported; a move that snaps to it again reports nothing.</summary>
        private float _shown;

        public ThresholdSlider(string label, float max, float value, Transform parent, FontSet fonts, System.Action<float> changed)
        {
            Root = Ui.NewRect(label, parent);
            _label = Ui.NewText("Label", Root, fonts.Mono, 12, Palette.Ink, 0.01f);
            _label.text = label;
            _value = Ui.NewText("Value", Root, fonts.Mono, 12, Palette.Ink, 0.01f);

            // A transparent hit area, taller than the track on touch layouts.
            Image hit = Ui.NewImage("Track", Root, Color.clear);
            hit.raycastTarget = true;
            _hit = hit.rectTransform;
            _rail = Ui.NewImage("Rail", _hit, Palette.Grey2);
            _fillArea = Ui.NewRect("Fill Area", _hit);
            Image run = Ui.NewImage("Run", _fillArea, Palette.Ink);
            Ui.Fill(run.rectTransform);
            _handleArea = Ui.NewRect("Handle Area", _hit);
            RectTransform handle = Ui.NewRect("Thumb", _handleArea);
            handle.pivot = new Vector2(0.5f, 0.5f);
            _outer = Ui.NewImage("Outer ring", handle, Palette.Ink);
            _ring = Ui.NewImage("Paper ring", handle, Palette.Paper);
            Image core = Ui.NewImage("Core", handle, Color.white);

            Slider = hit.gameObject.AddComponent<Slider>();
            Slider.fillRect = run.rectTransform;
            Slider.handleRect = handle;
            Slider.targetGraphic = core;
            Slider.navigation = new Navigation { mode = Navigation.Mode.None };
            Slider.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = Slider.colors;
            colors.normalColor = Palette.Ink;
            colors.highlightedColor = Palette.Pink;
            colors.pressedColor = Palette.Pink;
            colors.selectedColor = Palette.Ink;
            colors.disabledColor = Palette.Grey2;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0f;
            Slider.colors = colors;
            Slider.minValue = 0f;
            Slider.maxValue = max;
            Slider.SetValueWithoutNotify(value);
            Ui.Fill(core.rectTransform);
            _shown = value;
            ShowValue(value);
            Slider.onValueChanged.AddListener(v =>
            {
                float snapped = Mathf.Round(v * 20f) / 20f;
                if (!Mathf.Approximately(snapped, v))
                {
                    Slider.SetValueWithoutNotify(snapped);
                }

                if (snapped != _shown)
                {
                    _shown = snapped;
                    ShowValue(snapped);
                    changed(snapped);
                }
            });
        }

        public RectTransform Root { get; }
        public Slider Slider { get; }

        /// <summary>Height of the label, the 6px gap and the track row.</summary>
        public const float Height = 38f;

        /// <summary>Lays the slider out in <paramref name="width"/>; <paramref name="hitHeight"/> is the track's touch height.</summary>
        public void Place(float x, float y, float width, float hitHeight)
        {
            Ui.Place(Root, x, y, width, Height);
            Ui.Place(_label, 0f, 0f, width, 16f);
            float track = width - 12f - 34f;
            float valueWidth = Ui.Width(_value);
            Ui.Place(_value, width - valueWidth, 22f, valueWidth, 16f);

            // The track row is 16px; a taller hit area stays centered on it.
            Vector2 hit = Ui.Place(_hit, 0f, 22f + (16f - hitHeight) / 2f, track, hitHeight);
            float top = (hit.y - 16f) / 2f;
            Ui.Place(_rail, 0f, top + 6.5f, hit.x, 3f);
            Ui.Place(_fillArea, 0f, top + 6.5f, hit.x, 3f);
            Ui.Place(_handleArea, 0f, top, hit.x, 16f);

            RectTransform handle = Slider.handleRect;
            handle.sizeDelta = new Vector2(16f, 0f);
            handle.anchoredPosition = Vector2.zero;
            float ring = Ui.Snap(2f);
            Ui.Fill(_ring.rectTransform, -ring);
            Ui.Fill(_outer.rectTransform, -ring - Ui.Line);
        }

        private void ShowValue(float value)
        {
            _value.text = value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
