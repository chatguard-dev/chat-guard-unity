#nullable enable
using System.Globalization;
using ChatGuard.Core;
using ChatGuard.Core.Scoring;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>How the sample words a result: the stamp's reason, where it came from, and whom it targets.</summary>
    internal static class Wording
    {
        /// <summary>A score this high names its category in an allow's stamp; lower ones are left out.</summary>
        private const double NotableScore = 0.2;

        /// <summary>Category order in Last check: two per row in landscape, read across.</summary>
        public static readonly VerdictCategory[] ScoreOrder =
        {
            VerdictCategory.Insult, VerdictCategory.Sexual, VerdictCategory.Threat,
            VerdictCategory.Spam, VerdictCategory.Hate, VerdictCategory.Trading,
        };

        public static string Number(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>The category with the highest score, the first in API order on a tie.</summary>
        public static VerdictCategory Top(VerdictSet verdicts)
        {
            VerdictCategory top = VerdictCategory.Insult;
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                if (verdicts[category] > verdicts[top])
                {
                    top = category;
                }
            }

            return top;
        }

        /// <summary>True when the word filter or another on-device fallback decided.</summary>
        public static bool IsLocal(ModerationResult result)
        {
            return result.Source == ResultSource.Local || result.Model.StartsWith("local-filter", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The stamp's reason: "repeat" for a cached answer; "severity" and its value for a block at or above the
        /// severity block, which is checked before any category; otherwise the top category and its score when the
        /// action is not allow or the score is notable. "local" is added when the word filter or the device decided.
        /// A decision the client made on the device (its own answer, or a degraded server answer it re-checked) is
        /// compared with the slider's value when the line was sent. A server decision, the server's word filter
        /// included, is compared with the default, since the answer does not carry the dashboard's value.
        /// </summary>
        public static string Reason(ModerationResult result, ChatEntry entry)
        {
            bool onDevice = result.Source == ResultSource.Local || (result.Degraded && entry.RechecksDegraded);
            double severityBlock = onDevice ? entry.LocalSeverityBlock : Thresholds.DefaultSeverityBlock;
            VerdictCategory top = Top(result.Verdicts);
            double score = result.Verdicts[top];
            string reason = result.Cached ? "repeat"
                : result.Action == ModerationAction.Block && result.Severity >= severityBlock ? "severity " + Number(result.Severity, "F2")
                : result.Action != ModerationAction.Allow || score >= NotableScore ? VerdictCategories.ToWireName(top) + " " + Number(score, "F2")
                : string.Empty;
            if (!IsLocal(result))
            {
                return reason;
            }

            return reason.Length > 0 ? reason + " · local" : "local";
        }

        /// <summary>What decided, as Last check's facts show it.</summary>
        public static string Source(ModerationResult result)
        {
            string degraded = result.Degraded ? " · degraded: " + DegradedReasons.ToWireName(result.DegradedReason) : string.Empty;
            if (result.Model.StartsWith("local-filter", System.StringComparison.Ordinal))
            {
                return "word filter" + degraded;
            }

            if (result.Source == ResultSource.Local)
            {
                return result.Model + degraded;
            }

            return (result.Cached ? "cached" : "server") + degraded;
        }

        /// <summary>Last check's bar meta for a result.</summary>
        public static string Meta(ModerationResult result)
        {
            if (result.Model.StartsWith("local-filter", System.StringComparison.Ordinal))
            {
                return "Local filter";
            }

            return result.Source == ResultSource.Local ? "Fallback" : result.Cached ? "Cached" : "Server";
        }

        public static string Target(TargetVerdict? target)
        {
            if (target == null)
            {
                return "no target";
            }

            switch (target.Choice)
            {
                case TargetChoice.OtherUser: return "targets a player";
                case TargetChoice.Group: return "targets a group";
                case TargetChoice.Self: return "targets self";
                case TargetChoice.Nobody: return "no target";
                default: return "targets something else";
            }
        }

        /// <summary>
        /// The fill and value color of a category's meter: the action's tone for the top category of a line that is not
        /// allowed, muted otherwise.
        /// </summary>
        public static Color Tone(ModerationAction action, bool top, bool forText)
        {
            if (!top || action == ModerationAction.Allow)
            {
                return forText ? Palette.Muted : Palette.Grey2;
            }

            return action == ModerationAction.Flag ? Palette.Ink : action == ModerationAction.Hide ? Palette.Warn : Palette.Danger;
        }
    }

    /// <summary>A framed button: a transparent hit area, a body that moves while pressed, a frame and fill, and a label.</summary>
    internal sealed class ButtonView
    {
        public ButtonView(string name, Transform parent, Font font, int size, string label, ButtonTones tones, UnityAction onClick)
        {
            Root = Ui.NewRect(name, parent);
            Image hit = Root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            RectTransform body = Ui.NewRect("Body", Root);
            Ui.Fill(body);
            Box = new Box("Frame", body, Palette.Ink, Palette.Paper);
            Label = Ui.NewText("Label", body, font, size, Palette.Ink);
            Label.alignment = TextAnchor.MiddleCenter;
            Label.text = label;
            Button = Root.gameObject.AddComponent<SampleButton>();
            Button.targetGraphic = hit;
            Button.Frame = Box.Border;
            Button.FillGraphic = Box.Fill;
            Button.Label = Label;
            Button.Body = body;
            Button.Tones = tones;
            Button.onClick.AddListener(onClick);
        }

        public RectTransform Root { get; }
        public Box Box { get; }
        public Text Label { get; }
        public SampleButton Button { get; }

        /// <summary>Width with <paramref name="padding"/> on each side of the label, borders included.</summary>
        public float Fit(float padding)
        {
            return Ui.Width(Label) + 2f * padding + 2f * Ui.Line;
        }

        public Vector2 Place(float x, float y, float width, float height)
        {
            Vector2 size = Ui.Place(Root, x, y, width, height);
            Box.Place(0f, 0f, size.x, size.y);
            Ui.Place(Label, 0f, 0f, size.x, size.y);
            return size;
        }
    }

    /// <summary>One line in the players' view: the name in SemiBold, then the message, wrapping under the name.</summary>
    internal sealed class ChatLineView
    {
        private const float NameGap = 6f;
        private readonly Text _name;
        private readonly Paragraph _message;
        private readonly CanvasGroup _group;
        private float _landedAt = -1f;
        private float _x;
        private float _y;

        public ChatLineView(Transform parent, FontSet fonts)
        {
            Root = Ui.NewRect("Line", parent);
            _group = Root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _name = Ui.NewText("Name", Root, fonts.TextSemiBold, 15, Palette.Ink);
            _message = new Paragraph("Message", Root, fonts.Text, 15, Palette.InkSoft, 4);
        }

        public RectTransform Root { get; }

        /// <summary>Fills the line and returns its height.</summary>
        public float Set(ChatEntry entry, int size, float width)
        {
            _name.fontSize = size;
            _name.text = entry.PlayerName;
            float lineHeight = size * 1.45f;
            float nameWidth = Ui.Width(_name);
            Ui.Place(_name, 0f, 0f, nameWidth, lineHeight);
            Ui.Place(_message.Root, 0f, 0f, width, lineHeight);
            _message.FontSize = size;
            return _message.Set(entry.Text, width, nameWidth + NameGap, lineHeight);
        }

        /// <summary>Places the line, rising 4px into place as it fades in when its entry has just landed.</summary>
        public void Place(float x, float y, float width, float height, float landedAt, float now)
        {
            _x = x;
            _y = y;
            Ui.Place(Root, x, y, width, height);
            _landedAt = now - landedAt < 0.24f ? landedAt : -1f;
            Tick(now);
        }

        /// <summary>Advances the enter animation (240 ms); true while it runs.</summary>
        public bool Tick(float now)
        {
            if (_landedAt < 0f)
            {
                _group.alpha = 1f;
                return false;
            }

            float t = Mathf.Clamp01((now - _landedAt) / 0.24f);
            float eased = 1f - Mathf.Pow(1f - t, 4f);
            _group.alpha = eased;
            Root.anchoredPosition = new Vector2(Ui.Snap(_x), -Ui.Snap(_y + 4f * (1f - eased)));
            if (t >= 1f)
            {
                _landedAt = -1f;
            }

            return _landedAt >= 0f;
        }
    }

    /// <summary>
    /// One line of the verdict log: the message (struck through when players never see it), then a stamp of action and
    /// reason and, for a server answer, the time, in fixed columns. Checking shows a blinking block cursor. Clicking the
    /// row selects it.
    /// </summary>
    internal sealed class RowView
    {
        private const float NameGap = 6f;
        private readonly Image _background;
        private readonly Image _rule;
        private readonly Text _name;
        private readonly Paragraph _message;
        private readonly Badge _badge;
        private readonly Text _reason;
        private readonly Text _latency;
        private readonly Image _cursor;
        private readonly Text _checking;
        private readonly SampleButton _button;

        public RowView(Transform parent, FontSet fonts, UnityAction<RowView> onClick)
        {
            Root = Ui.NewRect("Row", parent);
            _background = Root.gameObject.AddComponent<Image>();
            _rule = Ui.NewImage("Rule", Root, Palette.Hairline);
            _name = Ui.NewText("Name", Root, fonts.TextSemiBold, 14, Palette.Ink);
            _message = new Paragraph("Message", Root, fonts.Text, 14, Palette.InkSoft, 3);
            _badge = new Badge(Root, fonts);
            _reason = Ui.NewText("Reason", Root, fonts.Mono, 11, Palette.Muted, 0.02f);
            _latency = Ui.NewText("Time", Root, fonts.Mono, 11, Palette.Muted, 0.02f);
            _cursor = Ui.NewImage("Cursor", Root, Palette.Ink);
            _checking = Ui.NewText("Checking", Root, fonts.Mono, 11, Palette.Muted, 0.02f);
            _checking.text = "checking";
            _button = Root.gameObject.AddComponent<SampleButton>();
            _button.targetGraphic = _background;
            _button.FillGraphic = _background;
            _button.onClick.AddListener(() => onClick(this));
        }

        public RectTransform Root { get; }

        /// <summary>Index of the entry the row shows.</summary>
        public int Entry { get; set; } = -1;

        /// <summary>The checking cursor, blinked by the view.</summary>
        public Graphic Cursor => _cursor;

        /// <summary>
        /// Fills the row for <paramref name="entry"/> and lays it out in <paramref name="width"/>, with room for a top
        /// rule; returns the height below the rule.
        /// </summary>
        public float Set(ChatEntry entry, bool selected, bool portrait, float width)
        {
            ModerationResult? result = entry.Result;
            _button.Tones = selected
                ? new ButtonTones(new ButtonTone(Palette.Grey1, Palette.Grey1, Palette.Ink), new ButtonTone(Palette.Grey1, Palette.Grey1, Palette.Ink), default)
                : new ButtonTones(new ButtonTone(Palette.Paper, Palette.Paper, Palette.Ink), new ButtonTone(Palette.RowHover, Palette.RowHover, Palette.Ink), default);

            bool struck = result != null && !result.ShouldDeliver;
            bool timed = result != null && result.Source != ResultSource.Local;
            _name.text = entry.PlayerName;
            _message.Color = struck ? Palette.Muted : Palette.InkSoft;
            if (result != null)
            {
                _badge.Set(result.Action);
                _reason.text = Wording.Reason(result, entry);
                _latency.text = result.LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms";
            }

            _badge.Show(result != null);
            Ui.Show(_reason, result != null);
            Ui.Show(_latency, timed);
            Ui.Show(_cursor, result == null);
            Ui.Show(_checking, result == null);

            float top = Ui.Line;
            float nameWidth = Ui.Width(_name);
            float height;
            if (!portrait)
            {
                // Columns: the message, a 190px stamp (badge and reason), a 58px time; 12px gaps, 16px insets.
                const float padding = 6f;
                float messageWidth = width - 32f - 190f - 58f - 24f;
                float stampX = 16f + messageWidth + 12f;
                float lineTop = top + padding;
                Ui.Place(_name, 16f, lineTop, nameWidth, 20f);
                Ui.Place(_message.Root, 16f, lineTop, messageWidth, 20f);
                _message.Set(entry.Text, messageWidth, nameWidth + NameGap, 20f, false, struck);
                PlaceStamp(result != null, stampX, lineTop + Metrics.TextBaseline(14, 20f));
                float latencyWidth = Ui.Width(_latency);
                Ui.Place(_latency, stampX + 190f + 12f + 58f - latencyWidth, lineTop + Metrics.TextBaseline(14, 20f) - Metrics.MonoBaseline(11, 16f), latencyWidth, 16f);
                height = padding + 20f + padding;
            }
            else
            {
                // Two rows: the message (wrapping), then the stamp; the time sits right, centered on both.
                const float padding = 7f;
                float latencyWidth = timed ? Ui.Width(_latency) : 0f;
                float messageWidth = width - 24f - (timed ? latencyWidth + 12f : 0f);
                float lineTop = top + padding;
                Ui.Place(_name, 12f, lineTop, nameWidth, 20f);
                Ui.Place(_message.Root, 12f, lineTop, messageWidth, 20f);
                float messageHeight = _message.Set(entry.Text, messageWidth, nameWidth + NameGap, 20f, true, struck);
                float stampTop = lineTop + messageHeight + 2f;
                float stampHeight = result != null ? Badge.Height : 16f;
                // A checking stamp is one 16px line with the cursor centered on it, so the cursor's bottom is 13px down.
                PlaceStamp(result != null, 12f, stampTop + (result != null ? Badge.Baseline : 13f));
                float content = messageHeight + 2f + stampHeight;
                Ui.Place(_latency, width - 12f - latencyWidth, lineTop + (content - 16f) / 2f, latencyWidth, 16f);
                height = padding + content + padding;
            }

            return height;
        }

        /// <summary>Places the row with its top rule shown or not; <paramref name="height"/> is what <see cref="Set"/> returned.</summary>
        public void Place(float y, float width, float height, bool rule)
        {
            Ui.Show(_rule, rule);
            Ui.Place(Root, 0f, y - Ui.Line, width, height + Ui.Line);
            Ui.Place(_rule, 0f, 0f, width, Ui.Line);
        }

        /// <summary>
        /// Lays out the stamp from <paramref name="x"/>: the badge and reason with their text on
        /// <paramref name="baseline"/>, or while checking the cursor standing on it.
        /// </summary>
        private void PlaceStamp(bool decided, float x, float baseline)
        {
            float textTop = baseline - Metrics.MonoBaseline(11, 16f);
            if (decided)
            {
                _badge.Place(x, baseline - Badge.Baseline);
                float reasonX = x + _badge.Width + 8f;
                Ui.Place(_reason, reasonX, textTop, Ui.Width(_reason), 16f);
                return;
            }

            // The cursor's bottom sits on the baseline; the label is centered on the cursor.
            Ui.Place(_cursor, x, baseline - 10f, 6f, 10f);
            Ui.Place(_checking, x + 12f, baseline - 5f - 8f, Ui.Width(_checking), 16f);
        }
    }

    /// <summary>
    /// Last check: the selected line's action and text, its six category scores as meters, and the facts (severity,
    /// target, source and, for a server answer, time). While the line is checking, a dashed slot with a blinking cursor
    /// stands in for the badge.
    /// </summary>
    internal sealed class DetailView
    {
        private readonly FontSet _fonts;
        private readonly Text _empty;
        private readonly Badge _badge;
        private readonly RectTransform _slot;
        private readonly RawImage[] _dashes = new RawImage[4];
        private readonly Image _slotCursor;
        private readonly Text _who;
        private readonly Text _note;
        private readonly RectTransform _scores;
        private readonly Text[] _keys = new Text[6];
        private readonly Meter[] _meters = new Meter[6];
        private readonly Text[] _values = new Text[6];
        private readonly RectTransform _facts;
        private readonly Text _severityKey;
        private readonly Box[] _severity = new Box[3];
        private readonly Text[] _factTexts = new Text[7];

        public DetailView(Transform parent, FontSet fonts, SampleTextures textures)
        {
            _fonts = fonts;
            Root = Ui.NewRect("Last check", parent);
            _empty = Ui.NewText("Empty", Root, fonts.Text, 13, Palette.Muted);
            _empty.alignment = TextAnchor.MiddleCenter;
            _empty.text = "Select a line to see its scores.";
            _badge = new Badge(Root, fonts);
            _slot = Ui.NewRect("Slot", Root);
            for (int i = 0; i < _dashes.Length; i++)
            {
                _dashes[i] = Ui.NewRawImage("Dash", _slot, i < 2 ? textures.DashAcross : textures.DashDown, Palette.Grey2);
            }

            _slotCursor = Ui.NewImage("Cursor", _slot, Palette.Ink);
            _who = Ui.NewText("Line", Root, fonts.Text, 13, Palette.Muted);
            _note = Ui.NewText("Note", Root, fonts.Text, 13, Palette.Muted);
            _note.text = "Checking with the thread as context.";

            _scores = Ui.NewRect("Scores", Root);
            for (int i = 0; i < 6; i++)
            {
                _keys[i] = Ui.NewText("Category", _scores, fonts.Mono, 11, Palette.Ink, 0.02f);
                _keys[i].text = VerdictCategories.ToWireName(Wording.ScoreOrder[i]);
                _meters[i] = new Meter(_scores);
                _values[i] = Ui.NewText("Score", _scores, fonts.Mono, 11, Palette.Muted, 0.02f);
            }

            _facts = Ui.NewRect("Facts", Root);
            _severityKey = Ui.NewText("Severity", _facts, fonts.Mono, 11, Palette.Ink, 0.02f);
            _severityKey.text = "severity";
            for (int i = 0; i < _severity.Length; i++)
            {
                _severity[i] = new Box("Level", _facts, Palette.Ink, Palette.Paper);
            }

            for (int i = 0; i < _factTexts.Length; i++)
            {
                _factTexts[i] = Ui.NewText("Fact", _facts, fonts.Mono, 11, Palette.Muted, 0.02f);
            }
        }

        public RectTransform Root { get; }

        /// <summary>The checking cursor, blinked by the view.</summary>
        public Graphic Cursor => _slotCursor;

        /// <summary>Shows <paramref name="entry"/> (null for none) in <paramref name="width"/>; scores stack in one column on portrait.</summary>
        public void Set(ChatEntry? entry, float width, bool portrait, float now)
        {
            const float left = 16f;
            const float top = 12f;
            float content = width - 32f;
            ModerationResult? result = entry?.Result;
            Ui.Show(_empty, entry == null);
            _badge.Show(result != null);
            Ui.Show(_slot, entry != null && result == null);
            Ui.Show(_who, entry != null);
            Ui.Show(_note, entry != null && result == null);
            Ui.Show(_scores, result != null);
            Ui.Show(_facts, result != null);
            if (entry == null)
            {
                Ui.Place(_empty, 0f, top + 24f, width, 13f * 1.45f);
                return;
            }

            // The head aligns the badge (or slot) and the line on a shared baseline.
            float whoBaseline = Metrics.TextBaseline(13, 13f * 1.45f);
            float markWidth;
            float headHeight;
            if (result == null)
            {
                // The slot's baseline is its cursor's bottom, 14px down; the line's is 14.15px down.
                markWidth = 44f;
                float slotTop = top + whoBaseline - 14f;
                Vector2 size = Ui.Place(_slot, left, slotTop, 44f, 18f);
                PlaceDashes(size);
                Ui.Place(_slotCursor, (size.x - 6f) / 2f, (size.y - 10f) / 2f, 6f, 10f);
                headHeight = 13f * 1.45f;
            }
            else
            {
                _badge.Set(result.Action);
                markWidth = _badge.Width;
                _badge.Place(left, top + whoBaseline - Badge.Baseline);
                headHeight = whoBaseline + Mathf.Max(13f * 1.45f - whoBaseline, Badge.Height - Badge.Baseline);
            }

            float whoX = left + markWidth + 8f;
            _who.text = Ui.Ellipsize(_who, entry.PlayerName + ": " + entry.Text, left + content - whoX);
            Ui.Place(_who, whoX, top, Ui.Width(_who), 13f * 1.45f);
            float next = top + headHeight + 10f;
            if (result == null)
            {
                Ui.Place(_note, left, next, content, 13f * 1.45f);
                return;
            }

            VerdictCategory topCategory = Wording.Top(result.Verdicts);
            int columns = portrait ? 1 : 2;
            float fixedWidth = portrait ? 58f + 34f + 20f : 58f + 34f + 8f + 58f + 34f + 60f;
            float meterWidth = (content - fixedWidth) / columns;
            float cell = 58f + 10f + meterWidth + 10f + 34f;
            int rows = 6 / columns;
            float scoresHeight = rows * 16f + (rows - 1) * 6f;
            Ui.Place(_scores, left, next, content, scoresHeight);
            for (int i = 0; i < 6; i++)
            {
                VerdictCategory category = Wording.ScoreOrder[i];
                double score = result.Verdicts[category];
                bool decisive = category == topCategory;
                float x = (i % columns) * (cell + 10f + 8f + 10f);
                float y = (i / columns) * 22f;
                Ui.Place(_keys[i], x, y, 58f, 16f);
                _meters[i].Place(x + 68f, y + 3f, meterWidth);
                _meters[i].Set((float)score, Wording.Tone(result.Action, decisive, false), now);
                _values[i].text = Wording.Number(score, "F2");
                _values[i].color = Wording.Tone(result.Action, decisive, true);
                _values[i].font = decisive && result.Action != ModerationAction.Allow ? _fonts.MonoMedium : _fonts.Mono;
                float valueWidth = Ui.Width(_values[i]);
                Ui.Place(_values[i], x + cell - valueWidth, y, valueWidth, 16f);
            }

            next += scoresHeight + 10f;
            SetFacts(result, left, next, content);
        }

        /// <summary>Advances the meters; true while any is moving.</summary>
        public bool Tick(float now)
        {
            bool moving = false;
            foreach (Meter meter in _meters)
            {
                moving |= meter.Tick(now);
            }

            return moving;
        }

        private void SetFacts(ModerationResult result, float x, float y, float width)
        {
            int level = Mathf.Min(3, Mathf.FloorToInt((float)result.Severity + 0.5f));
            _factTexts[0].text = Wording.Number(result.Severity, "F2") + " of 3";
            _factTexts[1].text = "·";
            _factTexts[2].text = Wording.Target(result.Target);
            _factTexts[3].text = "·";
            _factTexts[4].text = Wording.Source(result);
            _factTexts[5].text = "·";
            _factTexts[6].text = result.LatencyMs.ToString(CultureInfo.InvariantCulture) + " ms";
            bool timed = result.Source != ResultSource.Local;
            int count = timed ? _factTexts.Length : _factTexts.Length - 2;
            Ui.Show(_factTexts[5], timed);
            Ui.Show(_factTexts[6], timed);

            // Items run left to right 10px apart and wrap to a new 16px line when the next one would not fit.
            const float gap = 10f;
            float cx = 0f;
            float cy = 0f;
            float keyWidth = Ui.Width(_severityKey);
            Ui.Place(_severityKey, cx, cy, keyWidth, 16f);
            cx += keyWidth + gap;
            for (int i = 0; i < _severity.Length; i++)
            {
                _severity[i].Place(cx + i * 16f, cy + 3f, 14f, 10f);
                _severity[i].Fill.color = i < level ? Palette.Ink : Palette.Paper;
            }

            cx += 3f * 14f + 2f * 2f + gap;
            for (int i = 0; i < count; i++)
            {
                Text fact = _factTexts[i];
                float itemWidth = Ui.Width(fact);
                if (i % 2 == 1)
                {
                    // A separator wraps with the item after it and is dropped at the line break.
                    bool fits = cx + itemWidth + gap + Ui.Width(_factTexts[i + 1]) <= width;
                    Ui.Show(fact, fits);
                    if (!fits)
                    {
                        cx = 0f;
                        cy += 16f + 4f;
                        continue;
                    }
                }

                Ui.Place(fact, cx, cy, itemWidth, 16f);
                cx += itemWidth + gap;
            }

            Ui.Place(_facts, x, y, width, cy + 16f);
        }

        private void PlaceDashes(Vector2 size)
        {
            float line = Ui.Line;
            float period = 6f / Ui.Scale;
            Ui.Place(_dashes[0], 0f, 0f, size.x, line);
            Ui.Place(_dashes[1], 0f, size.y - line, size.x, line);
            Ui.Place(_dashes[2], 0f, 0f, line, size.y);
            Ui.Place(_dashes[3], size.x - line, 0f, line, size.y);
            _dashes[0].uvRect = _dashes[1].uvRect = new Rect(0f, 0f, size.x / period, 1f);
            _dashes[2].uvRect = _dashes[3].uvRect = new Rect(0f, 0f, 1f, size.y / period);
        }
    }

    /// <summary>Local thresholds: three sliders and the note on when they apply.</summary>
    internal sealed class TuneView
    {
        private readonly ThresholdSlider[] _sliders = new ThresholdSlider[3];
        private readonly Paragraph _note;

        public TuneView(Transform parent, FontSet fonts, float insultHide, float threatBlock, float severityBlock, UnityAction<int, float> changed)
        {
            Root = Ui.NewRect("Local thresholds", parent);
            _sliders[0] = new ThresholdSlider("insult hide", 1f, insultHide, Root, fonts, v => changed(0, v));
            _sliders[1] = new ThresholdSlider("threat block", 1f, threatBlock, Root, fonts, v => changed(1, v));
            _sliders[2] = new ThresholdSlider("severity block", 3f, severityBlock, Root, fonts, v => changed(2, v));
            _note = new Paragraph("Note", Root, fonts.Text, 12, Palette.Muted, 4);
        }

        public RectTransform Root { get; }

        /// <summary>
        /// Lays the view out in a window body of <paramref name="width"/> by <paramref name="height"/>: three columns
        /// centered vertically on landscape, one column from the top with 44px touch tracks on portrait.
        /// </summary>
        public void Place(float width, float height, bool portrait)
        {
            const float left = 16f;
            float content = width - 32f;
            const float noteLine = 12f * 1.45f;
            Ui.Place(_note.Root, 0f, 0f, content, noteLine);
            float noteHeight = _note.Set("Used only when the package decides on the device: with no key, no answer, or a degraded one. Your dashboard thresholds decide everything else.", content, 0f, noteLine);
            float slidersHeight = portrait ? 3f * ThresholdSlider.Height + 2f * 14f : ThresholdSlider.Height;
            float total = slidersHeight + 14f + noteHeight;
            float top = portrait ? 12f : 12f + Mathf.Max(0f, (height - 12f - 14f - total) / 2f);
            float column = portrait ? content : (content - 2f * 24f) / 3f;
            for (int i = 0; i < _sliders.Length; i++)
            {
                float x = portrait ? left : left + i * (column + 24f);
                float y = portrait ? top + i * (ThresholdSlider.Height + 14f) : top;
                _sliders[i].Place(x, y, column, portrait ? 44f : 16f);
            }

            Ui.Place(_note.Root, left, top + slidersHeight + 14f, content, noteHeight);
        }
    }
}
