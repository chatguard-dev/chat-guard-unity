#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>
    /// The sample's screen, built once in code and laid out for the screen's shape: landscape (at least 1.2 times as wide
    /// as tall) on a 1280x720 reference, portrait on 390x844, where the log, Last check and the thresholds share the
    /// Chat Guard window as tabs. <see cref="Refresh"/> lays everything out and fills it from the sample's state.
    /// </summary>
    internal sealed class BasicChatView
    {
        private static readonly Vector2 LandscapeReference = new Vector2(1280f, 720f);
        private static readonly Vector2 PortraitReference = new Vector2(390f, 844f);
        private const float LandscapeAspect = 1.2f;
        private const string Lede = "Type as any player. Chat Guard checks each message before the others see it.";
        private const string EmptyLog = "Each message shows its decision here: the action, a short reason and, for a server answer, how long the check took.";

        private readonly BasicChatSample _owner;
        private readonly FontSet _fonts;
        private readonly SampleTextures _textures;
        private readonly GameObject _root;
        private readonly Canvas _canvas;
        private readonly CanvasScaler _scaler;

        private readonly RawImage _stage;
        private readonly Image _stageFade;
        private readonly Image[] _brackets = new Image[8];

        private readonly Text _title;
        private readonly Image _ledeRule;
        private readonly Paragraph _lede;
        private readonly Box _mark;
        private readonly Image _markDot;
        private readonly Text _wordmark;
        private readonly Box _chip;
        private readonly Image _chipSquare;
        private readonly Text _chipText;

        private readonly Window _chatWindow;
        private readonly Window _guardWindow;
        private readonly Window _detailWindow;
        private readonly Window _tuneWindow;

        private readonly RectTransform _linesArea;
        private readonly ChatLineView[] _lines = new ChatLineView[14];
        private readonly Text _chatEmpty;
        private readonly Image _footRule;
        private readonly Text _footNote;
        private readonly RectTransform _speakerRoot;
        private readonly Image _speakerFill;
        private readonly Text _speakerLabel;
        private readonly RawImage _speakerArrow;
        private readonly RectTransform _speakerContent;
        private readonly RectTransform _speakerItem;
        private readonly InputField _input;
        private readonly Image _inputFill;
        private readonly Image[] _focusRing = new Image[4];
        private readonly ButtonView _send;

        private readonly ButtonView[] _tabs = new ButtonView[3];
        private readonly Image[] _tabRules = new Image[3];
        private readonly Image _tabsRule;
        private readonly RectTransform _logPanel;
        private readonly Text _summary;
        private readonly ButtonView _play;
        private readonly Image[] _steps = new Image[3];
        private readonly ButtonView _clear;
        private readonly Image _toolbarRule;
        private readonly Box _emptyPanel;
        private readonly RawImage _emptyDots;
        private readonly Box _emptyChip;
        private readonly Paragraph _emptyChipText;
        private readonly ButtonView _emptyPlay;
        private readonly RectTransform _log;
        private readonly RowView[] _rows = new RowView[12];
        private readonly RectTransform _panelHost;
        private readonly DetailView _detail;
        private readonly TuneView _tune;

        private bool _portrait;
        private int _tab;
        private int _screenWidth;
        private int _screenHeight;
        private Rect _safeArea;
        private bool _animating;
        private bool _blinkOn;
        private bool _blinkApplied;
        private bool _wasPlaying;
        private float _playStarted;
        private int _step = -1;
        private bool _focusShown;

        public BasicChatView(BasicChatSample owner, Transform parent, BasicChatFonts fonts, SampleTextures textures)
        {
            _owner = owner;
            _fonts = new FontSet(fonts);
            _textures = textures;

            _root = new GameObject("Basic Chat UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(parent, false);
            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.pixelPerfect = true;
            _scaler = _root.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;
            _scaler.referenceResolution = LandscapeReference;
            Transform root = _root.transform;

            // The page: pink under everything, whatever the camera clears to, then the halftone stage and its fade.
            Image page = Ui.NewImage("Page", root, Palette.Pink);
            Ui.Fill(page.rectTransform);
            _stage = Ui.NewRawImage("Stage", root, textures.Halftone, Palette.Dot);
            _stageFade = Ui.NewImage("Stage fade", root, Palette.Pink);
            _stageFade.sprite = textures.Fade;
            _stageFade.type = Image.Type.Sliced;
            for (int i = 0; i < _brackets.Length; i++)
            {
                _brackets[i] = Ui.NewImage("Bracket", root, Palette.Ink);
            }

            _title = Ui.NewText("Heading", root, _fonts.Display, 44, Palette.Ink, -0.03f);
            _title.text = "Basic Chat";
            _ledeRule = Ui.NewImage("Lede rule", root, Palette.Ink);
            _lede = new Paragraph("Lede", root, _fonts.Text, 15, Palette.InkSoft, 3);
            _mark = new Box("Mark", root, Palette.Ink, Palette.Paper);
            _markDot = Ui.NewImage("Mark dot", _mark.Border.transform, Palette.Pink);
            _wordmark = Ui.NewText("Wordmark", root, _fonts.Display, 20, Palette.Ink, -0.03f);
            _wordmark.text = "Chat Guard";
            _chip = new Box("Connection", root, Palette.Ink, Palette.Paper);
            _chipSquare = Ui.NewImage("State", _chip.Border.transform, Palette.Ok);
            _chipText = Ui.NewText("Label", _chip.Border.transform, _fonts.Mono, 12, Palette.Ink, 0.01f);

            _chatWindow = new Window("Global chat", "What players see", root, _fonts);
            _guardWindow = new Window("Chat Guard", "What you see", root, _fonts);
            _tuneWindow = new Window("Local thresholds", "On device", root, _fonts);
            _detailWindow = new Window("Last check", "No check yet", root, _fonts);

            // Global chat: the lines players get, then the note and the compose row.
            Transform chat = _chatWindow.Body;
            _linesArea = Ui.NewRect("Lines", chat);
            _linesArea.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = new ChatLineView(_linesArea, _fonts);
            }

            _chatEmpty = Ui.NewText("Empty", _linesArea, _fonts.Text, 13, Palette.Muted);
            _chatEmpty.alignment = TextAnchor.MiddleCenter;
            _chatEmpty.text = "No messages yet. Say something below.";
            _footRule = Ui.NewImage("Rule", chat, Palette.Ink);
            _footNote = Ui.NewText("Note", chat, _fonts.Text, 13, Palette.Muted);
            _footNote.text = "Hidden and blocked lines never reach them.";
            BuildSpeaker(chat, out _speakerRoot, out _speakerFill, out _speakerLabel, out _speakerArrow, out _speakerContent, out _speakerItem);
            _input = BuildInput(chat, out _inputFill);
            for (int i = 0; i < _focusRing.Length; i++)
            {
                _focusRing[i] = Ui.NewImage("Focus", chat, Palette.Ink);
                _focusRing[i].gameObject.SetActive(false);
            }

            _send = new ButtonView("Send", chat, _fonts.TextMedium, 14, "Send", ButtonTones.Primary, Submit);

            // Chat Guard: tabs on portrait, the toolbar, and the log or its empty state.
            Transform guard = _guardWindow.Body;
            string[] tabNames = { "Log", "Last check", "Thresholds" };
            for (int i = 0; i < _tabs.Length; i++)
            {
                int tab = i;
                _tabs[i] = new ButtonView(tabNames[i], guard, _fonts.Mono, 12, tabNames[i], ButtonTones.Secondary, () => SelectTab(tab));
                _tabs[i].Button.Body = null;
                _tabs[i].Label.gameObject.AddComponent<Tracking>().Em = 0.01f;
                _tabRules[i] = Ui.NewImage("Rule", guard, Palette.Ink);
            }

            _tabsRule = Ui.NewImage("Tabs rule", guard, Palette.Ink);
            _logPanel = Ui.NewRect("Log panel", guard);
            _summary = Ui.NewText("Summary", _logPanel, _fonts.Text, 13, Palette.Muted);
            _play = new ButtonView("Play example", _logPanel, _fonts.TextMedium, 12, "Play example", ButtonTones.Secondary, owner.PlayExample);
            for (int i = 0; i < _steps.Length; i++)
            {
                _steps[i] = Ui.NewImage("Step", _play.Button.Body!, Palette.Ink);
            }

            _play.Button.Marks = _steps;
            _clear = new ButtonView("Clear", _logPanel, _fonts.TextMedium, 12, "Clear", ButtonTones.Secondary, owner.Clear);
            _toolbarRule = Ui.NewImage("Toolbar rule", _logPanel, Palette.Ink);
            _emptyPanel = new Box("Empty", _logPanel, Palette.HairlineOnPaper, Palette.Paper);
            _emptyDots = Ui.NewRawImage("Halftone", _emptyPanel.Fill.transform, textures.Halftone, Palette.Dot);
            _emptyChip = new Box("Chip", _emptyPanel.Fill.transform, Palette.Ink, Palette.Paper);
            _emptyChipText = new Paragraph("Text", _emptyChip.Border.transform, _fonts.Text, 13, Palette.InkSoft, 4);
            _emptyChipText.Alignment = TextAnchor.MiddleCenter;
            _emptyPlay = new ButtonView("Play example", _emptyPanel.Fill.transform, _fonts.TextMedium, 14, "Play example", ButtonTones.Secondary, owner.PlayExample);
            _log = Ui.NewRect("Log", _logPanel);
            _log.gameObject.AddComponent<RectMask2D>();
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = new RowView(_log, _fonts, row => owner.Select(row.Entry));
            }

            _panelHost = Ui.NewRect("Panel", guard);
            _panelHost.gameObject.AddComponent<RectMask2D>();
            _detail = new DetailView(_detailWindow.Body, _fonts, textures);
            _tune = new TuneView(_tuneWindow.Body, _fonts, owner.InsultHide, owner.ThreatBlock, owner.SeverityBlock, owner.SetThreshold);
        }

        public void Destroy()
        {
            Object.Destroy(_root);
        }

        /// <summary>Lays the screen out for the current screen size and fills it from the sample's state.</summary>
        public void Refresh()
        {
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            _safeArea = Screen.safeArea;
            float screenWidth = Mathf.Max(1, _screenWidth);
            float screenHeight = Mathf.Max(1, _screenHeight);
            bool portrait = screenWidth / screenHeight < LandscapeAspect;
            if (portrait != _portrait)
            {
                _portrait = portrait;
                _tab = 0;
            }

            // CanvasScaler's own formula (scale with screen size, width and height weighed equally), applied now so this
            // pass measures text at the scale it renders at; the scaler arrives at the same factor on its next update.
            Vector2 reference = portrait ? PortraitReference : LandscapeReference;
            float scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(screenWidth / reference.x, 2f), Mathf.Log(screenHeight / reference.y, 2f), 0.5f));
            _scaler.referenceResolution = reference;
            _canvas.scaleFactor = scale;
            Ui.SetScale(scale);
            _textures.SetHalftoneScale(scale);

            float now = Time.unscaledTime;
            if (_owner.Playing && !_wasPlaying)
            {
                _playStarted = now;
            }

            _wasPlaying = _owner.Playing;
            _animating = false;
            _chipSquare.color = _owner.HasKey ? Palette.Ok : Palette.Warn;
            ChatEntry? selected = SelectedEntry();
            _detailWindow.Meta = selected == null ? "No check yet" : selected.Result == null ? "Checking" : Wording.Meta(selected.Result);

            // Content stays inside Screen.safeArea (bottom-left origin); the page, stage and brackets run to the edges.
            Rect content = Rect.MinMaxRect(
                Mathf.Max(0f, _safeArea.xMin) / scale,
                Mathf.Max(0f, screenHeight - _safeArea.yMax) / scale,
                Mathf.Min(screenWidth, _safeArea.xMax) / scale,
                Mathf.Min(screenHeight, screenHeight - _safeArea.yMin) / scale);
            if (portrait)
            {
                LayoutPortrait(screenWidth / scale, screenHeight / scale, content, now);
            }
            else
            {
                LayoutLandscape(screenWidth / scale, screenHeight / scale, content, now);
            }

            // Cursors and busy squares shown by this pass take the current phase.
            _step = -1;
            _blinkApplied = false;
            Tick(now);
        }

        /// <summary>Per-frame work: follows screen size changes, blinks cursors, steps the busy squares, runs animations.</summary>
        public void Tick(float now)
        {
            if (Screen.width != _screenWidth || Screen.height != _screenHeight || Screen.safeArea != _safeArea)
            {
                Refresh();
                return;
            }

            // Cursors blink once a second, on for the first half.
            bool blinkOn = now % 1f < 0.5f;
            if (blinkOn != _blinkOn || !_blinkApplied)
            {
                _blinkOn = blinkOn;
                _blinkApplied = true;
                float alpha = blinkOn ? 1f : 0f;
                foreach (RowView row in _rows)
                {
                    row.Cursor.canvasRenderer.SetAlpha(alpha);
                }

                _detail.Cursor.canvasRenderer.SetAlpha(alpha);
            }

            // While the example plays, one of the three squares is full and the others faint, stepping every 300 ms.
            if (_owner.Playing)
            {
                int step = (int)((now - _playStarted) / 0.3f) % 3;
                if (step != _step)
                {
                    _step = step;
                    for (int i = 0; i < _steps.Length; i++)
                    {
                        _steps[i].canvasRenderer.SetAlpha(i == step ? 1f : 0.25f);
                    }
                }
            }

            bool focused = _input.isFocused;
            if (focused != _focusShown)
            {
                _focusShown = focused;
                foreach (Image edge in _focusRing)
                {
                    edge.gameObject.SetActive(focused);
                }
            }

            if (_animating)
            {
                bool moving = _detail.Tick(now);
                foreach (ChatLineView line in _lines)
                {
                    moving |= line.Tick(now);
                }

                _animating = moving;
            }
        }

        private void LayoutLandscape(float width, float height, Rect content, float now)
        {
            // A 12-column grid 40px in from the sides of the content with 24px gutters; the head, then two rows of
            // windows 20px apart, the lower one 170px tall.
            const float gutter = 24f;
            const float rowGap = 20f;
            const float lowerRow = 170f;
            float left = content.xMin + 40f;
            float top = content.yMin + 32f;
            float right = content.xMax - 40f;
            float bottom = content.yMax - 32f;
            float column = (right - left - 11f * gutter) / 12f;
            float leftWidth = 5f * column + 4f * gutter;
            float rightX = left + 5f * (column + gutter);
            float rightWidth = right - rightX;
            PlaceStage(16f, 112f, width - 32f, height - 128f);
            PlaceBrackets(width, height, 16f);

            // The heading and its ruled lede on the left; the wordmark over the connection chip on the right, both
            // standing on the head's bottom edge.
            float chipWidth = ChipWidth();
            float brandWidth = Mathf.Max(chipWidth, 14f + 8f + Ui.Width(_wordmark));
            float titleLine = 44f * 0.95f;
            _title.fontSize = 44;
            Ui.Place(_title, left, top, right - left - brandWidth - gutter, titleLine);
            float ledeTop = top + titleLine + 12f;
            float ledeHeight = PlaceLede(left, ledeTop, Mathf.Min(576f, right - left - brandWidth - gutter), 15);
            float headBottom = top + Mathf.Max(titleLine + 12f + ledeHeight, 20f + 10f + 28f);
            PlaceChip(right - chipWidth, headBottom - 28f, chipWidth);
            PlaceWordmark(right, headBottom - 28f - 10f - 20f, true);

            float upperTop = headBottom + rowGap;
            float lowerTop = bottom - lowerRow;
            float upperHeight = Mathf.Max(0f, lowerTop - rowGap - upperTop);
            _chatWindow.Place(left, upperTop, leftWidth, upperHeight);
            _guardWindow.Place(rightX, upperTop, rightWidth, upperHeight);
            _tuneWindow.Place(left, lowerTop, leftWidth, lowerRow);
            _detailWindow.Place(rightX, lowerTop, rightWidth, lowerRow);
            Ui.Show(_tuneWindow.Frame.Border, true);
            Ui.Show(_detailWindow.Frame.Border, true);

            LayoutChat(false, now);
            LayoutGuard(false, now);
            Adopt(_detail.Root, _detailWindow.Body);
            Adopt(_tune.Root, _tuneWindow.Body);
            Ui.Show(_detail.Root, true);
            Ui.Show(_tune.Root, true);
            SetDetail(_detailWindow.BodySize, false, now);
            Ui.Place(_tune.Root, 0f, 0f, _tuneWindow.BodySize.x, _tuneWindow.BodySize.y);
            _tune.Place(_tuneWindow.BodySize.x, _tuneWindow.BodySize.y, false);
        }

        private void LayoutPortrait(float width, float height, Rect content, float now)
        {
            // One column 16px in from the sides of the content: the head, then the Chat Guard and Global chat windows
            // sharing the rest 1.1 to 1, 14px apart.
            const float rowGap = 14f;
            float left = content.xMin + 16f;
            float top = content.yMin + 20f;
            float right = content.xMax - 16f;
            float bottom = content.yMax - 16f;
            PlaceStage(0f, 150f, width, height - 144f);
            PlaceBrackets(width, height, 6f);

            float titleLine = 32f * 0.95f;
            _title.fontSize = 32;
            Ui.Place(_title, left, top, right - left, titleLine);
            float ledeTop = top + titleLine + 8f;
            float ledeHeight = PlaceLede(left, ledeTop, Mathf.Min(576f, right - left), 14);
            float brandTop = ledeTop + ledeHeight + 10f;
            float chipWidth = ChipWidth();
            PlaceChip(right - chipWidth, brandTop, chipWidth);
            PlaceWordmark(left, brandTop + 4f, false);
            float headBottom = brandTop + 28f;

            float shared = Mathf.Max(0f, bottom - headBottom - 2f * rowGap);
            float guardHeight = shared * 1.1f / 2.1f;
            _guardWindow.Place(left, headBottom + rowGap, right - left, guardHeight);
            _chatWindow.Place(left, headBottom + rowGap + guardHeight + rowGap, right - left, shared - guardHeight);
            Ui.Show(_tuneWindow.Frame.Border, false);
            Ui.Show(_detailWindow.Frame.Border, false);

            LayoutChat(true, now);
            LayoutGuard(true, now);
        }

        private float PlaceLede(float x, float y, float width, int size)
        {
            float line = size * 1.45f;
            _lede.FontSize = size;
            Ui.Place(_lede.Root, x + 11f, y, width - 11f, line);
            float height = _lede.Set(Lede, width - 11f, 0f, line);
            Ui.Place(_ledeRule, x, y, Ui.Line, height);
            return height;
        }

        private float ChipWidth()
        {
            _chipText.text = _portrait ? _owner.ConnectionShort : _owner.ConnectionLong;
            return 2f * Ui.Line + 10f + 8f + 8f + Ui.Width(_chipText) + 10f;
        }

        private void PlaceChip(float x, float y, float width)
        {
            Vector2 size = _chip.Place(x, y, width, 28f);
            Ui.Place(_chipSquare, Ui.Line + 10f, (size.y - 8f) / 2f, 8f, 8f);
            Ui.Place(_chipText, Ui.Line + 10f + 8f + 8f, (size.y - 16f) / 2f, Ui.Width(_chipText), 16f);
        }

        /// <summary>Places the mark and wordmark on a 20px line from <paramref name="y"/>, ending at <paramref name="x"/> or starting there.</summary>
        private void PlaceWordmark(float x, float y, bool endsAtX)
        {
            float textWidth = Ui.Width(_wordmark);
            float start = endsAtX ? x - textWidth - 8f - 14f : x;
            Vector2 mark = _mark.Place(start, y + 3f, 14f, 14f);
            // The pink square sits 1px inside the mark's frame, in its top-right corner.
            Ui.Place(_markDot, mark.x - Ui.Line - 1f - 5f, Ui.Line + 1f, 5f, 5f);
            Ui.Place(_wordmark, start + 14f + 8f, y, textWidth, 20f);
        }

        private void PlaceStage(float x, float y, float width, float height)
        {
            Vector2 size = Ui.Place(_stage, x, y, width, height);
            // One 4x4 tile per 4px, aligned to the stage's top-left corner (texture rows run bottom up).
            _stage.uvRect = new Rect(0f, 1f - size.y / 4f, size.x / 4f, size.y / 4f);
            Ui.Place(_stageFade, x, y, width, height);
        }

        private void PlaceBrackets(float width, float height, float inset)
        {
            // Drawn as 1px boxes, not borders, so they round to the nearest screen pixel like a decoration.
            float line = Ui.Stroke;
            float far = width - inset;
            float low = height - inset;
            Ui.Place(_brackets[0], inset, inset, 10f, line);
            Ui.Place(_brackets[1], inset, inset, line, 10f);
            Ui.Place(_brackets[2], far - 10f, inset, 10f, line);
            Ui.Place(_brackets[3], far - line, inset, line, 10f);
            Ui.Place(_brackets[4], inset, low - line, 10f, line);
            Ui.Place(_brackets[5], inset, low - 10f, line, 10f);
            Ui.Place(_brackets[6], far - 10f, low - line, 10f, line);
            Ui.Place(_brackets[7], far - line, low - 10f, line, 10f);
        }

        private void LayoutChat(bool portrait, float now)
        {
            Vector2 body = _chatWindow.BodySize;
            int size = portrait ? 14 : 15;
            float control = portrait ? 44f : 36f;
            const float noteLine = 13f * 1.45f;
            float footHeight = Ui.Line + 12f + noteLine + 8f + control + 12f;
            float linesHeight = Mathf.Max(0f, body.y - footHeight);
            Ui.Place(_linesArea, 0f, 0f, body.x, linesHeight);
            FillLines(body.x, linesHeight, size, now);

            Ui.Place(_footRule, 0f, linesHeight, body.x, Ui.Line);
            float noteTop = linesHeight + Ui.Line + 12f;
            Ui.Place(_footNote, 16f, noteTop, body.x - 32f, noteLine);
            float rowTop = noteTop + noteLine + 8f;

            // The select fits its longest name (12px in, 30px for the triangle), at most 104px on portrait.
            float widest = 0f;
            foreach (string name in BasicChatSample.PlayerNames)
            {
                widest = Mathf.Max(widest, Ui.Measure(_speakerLabel, name));
            }

            float selectWidth = widest + 12f + 30f + 2f * Ui.Line;
            if (portrait)
            {
                selectWidth = Mathf.Min(104f, selectWidth);
            }

            float sendWidth = _send.Fit(portrait ? 12f : 16f);
            PlaceSpeaker(12f, rowTop, selectWidth, control);
            float inputX = 12f + selectWidth + 8f;
            PlaceInput(inputX, rowTop, body.x - 12f - sendWidth - 8f - inputX, control);
            _send.Place(body.x - 12f - sendWidth, rowTop, sendWidth, control);
        }

        /// <summary>
        /// Fills the players' view from the bottom up with the newest delivered lines, 10px apart in a 16px inset,
        /// showing whole lines only.
        /// </summary>
        private void FillLines(float width, float height, int size, float now)
        {
            IReadOnlyList<ChatEntry> entries = _owner.Entries;
            float content = width - 32f;
            float bottom = height - 16f;
            int used = 0;
            bool anyDelivered = false;
            for (int i = entries.Count - 1; i >= 0 && used < _lines.Length; i--)
            {
                ChatEntry entry = entries[i];
                if (!entry.Delivered)
                {
                    continue;
                }

                anyDelivered = true;
                ChatLineView line = _lines[used];
                float lineHeight = line.Set(entry, size, content);
                float y = bottom - lineHeight;
                if (y < 16f - 1f)
                {
                    break;
                }

                Ui.Show(line.Root, true);
                line.Place(16f, y, content, lineHeight, entry.LandedAt, now);
                _animating |= now - entry.LandedAt < 0.24f;
                bottom = y - 10f;
                used++;
            }

            for (int i = used; i < _lines.Length; i++)
            {
                Ui.Show(_lines[i].Root, false);
            }

            Ui.Show(_chatEmpty, !anyDelivered);
            Ui.Place(_chatEmpty, 16f, (height - 13f * 1.45f) / 2f, content, 13f * 1.45f);
        }

        private void LayoutGuard(bool portrait, float now)
        {
            Vector2 body = _guardWindow.BodySize;
            float top = 0f;
            for (int i = 0; i < _tabs.Length; i++)
            {
                Ui.Show(_tabs[i].Root, portrait);
                Ui.Show(_tabRules[i], portrait);
            }

            Ui.Show(_tabsRule, portrait);
            if (portrait)
            {
                // Tabs: 44px tall, 12px in, a rule on each one's right and one under the row.
                float x = 0f;
                for (int i = 0; i < _tabs.Length; i++)
                {
                    ButtonView tab = _tabs[i];
                    bool selected = i == _tab;
                    ButtonTone tone = selected ? new ButtonTone(Palette.Ink, Palette.Ink, Palette.Paper) : new ButtonTone(Palette.Paper, Palette.Paper, Palette.Ink);
                    tab.Button.Tones = new ButtonTones(tone, selected ? tone : new ButtonTone(Palette.Hover, Palette.Hover, Palette.Ink), tone);
                    float tabWidth = Ui.Width(tab.Label) + 24f;
                    tab.Place(x, 0f, tabWidth, 44f);
                    Ui.Place(_tabRules[i], x + tabWidth, 0f, Ui.Line, 44f);
                    x += tabWidth + Ui.Line;
                }

                Ui.Place(_tabsRule, 0f, 44f, body.x, Ui.Line);
                top = 44f + Ui.Line;
            }

            bool logShown = !portrait || _tab == 0;
            Ui.Show(_logPanel, logShown);
            if (logShown)
            {
                LayoutLog(body.x, top, body.y - top, portrait, now);
            }

            Ui.Show(_panelHost, portrait && _tab != 0);
            if (portrait && _tab != 0)
            {
                Vector2 host = Ui.Place(_panelHost, 0f, top, body.x, body.y - top);
                Ui.Show(_detail.Root, _tab == 1);
                Ui.Show(_tune.Root, _tab == 2);
                if (_tab == 1)
                {
                    Adopt(_detail.Root, _panelHost);
                    SetDetail(host, true, now);
                }
                else
                {
                    Adopt(_tune.Root, _panelHost);
                    Ui.Place(_tune.Root, 0f, 0f, host.x, host.y);
                    _tune.Place(host.x, host.y, true);
                }
            }
        }

        private void LayoutLog(float width, float top, float height, bool portrait, float now)
        {
            IReadOnlyList<ChatEntry> entries = _owner.Entries;
            Vector2 panel = Ui.Place(_logPanel, 0f, top, width, height);

            // Toolbar: Play example and Clear on the right, 8px from the top, and the summary on the left, centered on
            // them; 44px with its rule, or 44px buttons with 8px around them on portrait.
            int done = 0;
            int kept = 0;
            foreach (ChatEntry entry in entries)
            {
                if (entry.Result != null)
                {
                    done++;
                    kept += entry.Result.ShouldDeliver ? 0 : 1;
                }
            }

            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            if (entries.Count == 0)
            {
                _summary.text = "No messages yet.";
            }
            else if (done == 0)
            {
                _summary.text = portrait ? "Checking" : entries.Count == 1 ? "Checking 1 line." : "Checking " + entries.Count.ToString(invariant) + " lines.";
            }
            else
            {
                string count = kept.ToString(invariant) + " of " + done.ToString(invariant);
                _summary.text = portrait ? count + " kept" : count + " lines kept from other players.";
            }

            const float controlTop = 8f;
            float control = portrait ? 44f : 28f;
            float padding = portrait ? 14f : 10f;
            int fontSize = portrait ? 14 : 12;
            float toolbar = portrait ? 60f : 44f - Ui.Line;
            _summary.fontSize = portrait ? 12 : 13;
            float summaryLine = _summary.fontSize * 1.45f;
            Ui.Place(_summary, 16f, controlTop + (control - summaryLine) / 2f, Ui.Width(_summary), summaryLine);

            _clear.Label.fontSize = fontSize;
            _clear.Button.interactable = entries.Count > 0;
            float clearWidth = _clear.Fit(padding);
            _clear.Place(panel.x - 12f - clearWidth, controlTop, clearWidth, control);

            bool playing = _owner.Playing;
            Ui.Show(_play.Root, entries.Count > 0 || playing);
            _play.Label.fontSize = fontSize;
            _play.Label.text = playing ? "Playing" : "Play example";
            _play.Button.Busy = playing;
            float labelWidth = Ui.Width(_play.Label);
            float playWidth = playing ? labelWidth + 8f + 18f + 2f * padding + 2f * Ui.Line : _play.Fit(padding);
            Vector2 play = _play.Place(panel.x - 12f - clearWidth - 8f - playWidth, controlTop, playWidth, control);
            foreach (Image step in _steps)
            {
                Ui.Show(step, playing);
            }

            if (playing)
            {
                // "Playing" and the three 4px squares, 3px apart, centered together.
                float start = (play.x - (labelWidth + 8f + 18f)) / 2f;
                _play.Label.alignment = TextAnchor.MiddleLeft;
                Ui.Place(_play.Label, start, 0f, labelWidth, play.y);
                for (int i = 0; i < _steps.Length; i++)
                {
                    Ui.Place(_steps[i], start + labelWidth + 8f + i * 7f, (play.y - 4f) / 2f, 4f, 4f);
                }
            }
            else
            {
                _play.Label.alignment = TextAnchor.MiddleCenter;
            }

            Ui.Place(_toolbarRule, 0f, toolbar, panel.x, Ui.Line);
            float listTop = toolbar + Ui.Line;
            float listHeight = Mathf.Max(0f, panel.y - listTop);
            bool empty = entries.Count == 0;
            Ui.Show(_emptyPanel.Border, empty);
            Ui.Show(_log, !empty);
            if (empty)
            {
                PlaceEmptyLog(panel.x, listTop, listHeight, portrait);
                return;
            }

            // Rows from the bottom up, newest last, whole rows only; each has a hairline above it except the top one.
            Vector2 list = Ui.Place(_log, 0f, listTop, panel.x, listHeight);
            int first = Mathf.Max(0, entries.Count - _rows.Length);
            float bottom = list.y;
            int used = 0;
            float topY = 0f;
            float topHeight = 0f;
            for (int i = entries.Count - 1; i >= first; i--)
            {
                RowView row = _rows[used];
                float rowHeight = row.Set(entries[i], i == _owner.Selected, portrait, list.x);
                float y = bottom - rowHeight;
                if (y < -1f)
                {
                    break;
                }

                row.Entry = i;
                Ui.Show(row.Root, true);
                row.Place(y, list.x, rowHeight, true);
                topY = y;
                topHeight = rowHeight;
                bottom = y - Ui.Line;
                used++;
            }

            if (used > 0)
            {
                _rows[used - 1].Place(topY, list.x, topHeight, false);
            }

            for (int i = used; i < _rows.Length; i++)
            {
                Ui.Show(_rows[i].Root, false);
            }
        }

        /// <summary>The empty log: a halftone panel in a hairline frame with a chip explaining the log and Play example under it.</summary>
        private void PlaceEmptyLog(float width, float top, float height, bool portrait)
        {
            Vector2 size = _emptyPanel.Place(16f, top + 16f, width - 32f, height - 32f);
            float innerWidth = size.x - 2f * Ui.Line;
            float innerHeight = size.y - 2f * Ui.Line;
            Ui.Place(_emptyDots, 0f, 0f, innerWidth, innerHeight);
            _emptyDots.uvRect = new Rect(0f, 1f - innerHeight / 4f, innerWidth / 4f, innerHeight / 4f);

            const float chipLine = 13f * 1.45f;
            float chipWidth = Mathf.Min(352f, innerWidth, Ui.Measure(_emptyChipText.Style, EmptyLog) + 24f + 2f * Ui.Line);
            float textWidth = chipWidth - 24f - 2f * Ui.Line;
            Ui.Place(_emptyChipText.Root, Ui.Line + 12f, Ui.Line + 8f, textWidth, chipLine);
            float textHeight = _emptyChipText.Set(EmptyLog, textWidth, 0f, chipLine);
            float chipHeight = 2f * Ui.Line + 16f + textHeight;
            float button = portrait ? 44f : 36f;
            float buttonWidth = _emptyPlay.Fit(16f);
            float groupTop = (innerHeight - (chipHeight + 12f + button)) / 2f;
            _emptyChip.Place((innerWidth - chipWidth) / 2f, groupTop, chipWidth, chipHeight);
            _emptyPlay.Place((innerWidth - buttonWidth) / 2f, groupTop + chipHeight + 12f, buttonWidth, button);
        }

        private ChatEntry? SelectedEntry()
        {
            int selected = _owner.Selected;
            return selected >= 0 && selected < _owner.Entries.Count ? _owner.Entries[selected] : null;
        }

        private void SetDetail(Vector2 size, bool portrait, float now)
        {
            Ui.Place(_detail.Root, 0f, 0f, size.x, size.y);
            _detail.Set(SelectedEntry(), size.x, portrait, now);
            _animating = true;
        }

        private void SelectTab(int tab)
        {
            _tab = tab;
            Refresh();
        }

        private void Submit()
        {
            string text = _input.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _input.text = string.Empty;
            _input.ActivateInputField();
            _owner.Send(_owner.Speaker, text);
        }

        private static void Adopt(RectTransform child, Transform parent)
        {
            if (child.parent != parent)
            {
                child.SetParent(parent, false);
            }
        }

        private void BuildSpeaker(Transform parent, out RectTransform root, out Image fill, out Text label, out RawImage arrow, out RectTransform content, out RectTransform item)
        {
            root = Ui.NewRect("Speaking as", parent);
            Image frame = root.gameObject.AddComponent<Image>();
            frame.color = Palette.Ink;
            fill = Ui.NewImage("Fill", root, Palette.Paper);
            label = Ui.NewText("Label", root, _fonts.TextMedium, 14, Palette.Ink);
            arrow = Ui.NewRawImage("Triangle", root, _textures.Triangle, Palette.Ink);

            // The list floats: a double rule (1px ink, 2px paper, 1px ink) around paper rows.
            RectTransform template = Ui.NewRect("Template", root);
            template.anchorMin = Vector2.zero;
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -4f);
            template.sizeDelta = new Vector2(0f, 300f);
            template.gameObject.AddComponent<Image>().color = Palette.Ink;
            Ui.Fill(Ui.NewImage("Paper ring", template, Palette.Paper).rectTransform, 1f);
            Ui.Fill(Ui.NewImage("Inner frame", template, Palette.Ink).rectTransform, 3f);
            RectTransform viewport = Ui.NewRect("Viewport", template);
            Ui.Fill(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Ui.NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            item = Ui.NewRect("Item", content);
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.anchoredPosition = Vector2.zero;
            Image itemFill = item.gameObject.AddComponent<Image>();
            Text itemLabel = Ui.NewText("Item Label", item, _fonts.Text, 14, Palette.Ink);
            Ui.Fill(itemLabel.rectTransform);
            itemLabel.rectTransform.offsetMin = new Vector2(12f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-24f, 0f);
            Image itemMark = Ui.NewImage("Item Mark", item, Palette.Ink);
            RectTransform mark = itemMark.rectTransform;
            mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(1f, 0.5f);
            mark.anchoredPosition = new Vector2(-12f, 0f);
            mark.sizeDelta = new Vector2(6f, 6f);
            var toggle = item.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = itemFill;
            toggle.graphic = itemMark;
            toggle.isOn = true;
            ColorBlock colors = toggle.colors;
            colors.normalColor = Palette.Paper;
            colors.highlightedColor = Palette.Hover;
            colors.pressedColor = Palette.Hover;
            colors.selectedColor = Palette.RowHover;
            colors.disabledColor = Palette.Paper;
            colors.fadeDuration = 0f;
            toggle.colors = colors;
            template.gameObject.SetActive(false);

            var dropdown = root.gameObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = frame;
            dropdown.transition = Selectable.Transition.None;
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            dropdown.template = template;
            dropdown.captionText = label;
            dropdown.itemText = itemLabel;
            dropdown.options.Clear();
            foreach (string name in BasicChatSample.PlayerNames)
            {
                dropdown.options.Add(new Dropdown.OptionData(name));
            }

            dropdown.SetValueWithoutNotify(_owner.Speaker);
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.AddListener(value => _owner.Speaker = value);
        }

        private void PlaceSpeaker(float x, float y, float width, float height)
        {
            Vector2 size = Ui.Place(_speakerRoot, x, y, width, height);
            Ui.Place(_speakerFill, Ui.Line, Ui.Line, size.x - 2f * Ui.Line, size.y - 2f * Ui.Line);
            Ui.Place(_speakerLabel, 12f, 0f, size.x - 12f - 30f, size.y);
            // The triangle (8px wide, 5px tall) sits 12px from the right, centered vertically.
            Ui.Place(_speakerArrow, size.x - 12f - 8f, (size.y - 5f) / 2f, 8f, 5f);
            _speakerItem.sizeDelta = new Vector2(-8f, height);
            _speakerContent.sizeDelta = new Vector2(0f, height + 8f);
        }

        private InputField BuildInput(Transform parent, out Image fill)
        {
            RectTransform root = Ui.NewRect("Message", parent);
            Image frame = root.gameObject.AddComponent<Image>();
            frame.color = Palette.Ink;
            fill = Ui.NewImage("Fill", root, Palette.Paper);
            Text text = Ui.NewText("Text", root, _fonts.Text, 14, Palette.Ink);
            Text placeholder = Ui.NewText("Placeholder", root, _fonts.Text, 14, Palette.Placeholder);
            placeholder.text = "Say something";
            foreach (Text t in new[] { text, placeholder })
            {
                // The field scrolls and clips its own text, as its default text setup expects.
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }

            var input = root.gameObject.AddComponent<InputField>();
            input.targetGraphic = frame;
            input.transition = Selectable.Transition.None;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = 2000;
            input.customCaretColor = true;
            input.caretColor = Palette.Caret;
            input.selectionColor = Palette.Pink;
            input.onSubmit.AddListener(_ => Submit());
            return input;
        }

        private void PlaceInput(float x, float y, float width, float height)
        {
            var root = (RectTransform)_input.transform;
            Vector2 size = Ui.Place(root, x, y, width, height);
            Ui.Place(_inputFill, Ui.Line, Ui.Line, size.x - 2f * Ui.Line, size.y - 2f * Ui.Line);
            Ui.Place(_input.textComponent, 12f, 0f, size.x - 24f, size.y);
            Ui.Place((RectTransform)_input.placeholder.transform, 12f, 0f, size.x - 24f, size.y);

            // Focus: a 2px ink outline 1px outside the field.
            float ring = Ui.Snap(2f);
            float outX = Ui.Snap(x) - Ui.Line - ring;
            float outY = Ui.Snap(y) - Ui.Line - ring;
            float outWidth = size.x + 2f * (Ui.Line + ring);
            float outHeight = size.y + 2f * (Ui.Line + ring);
            Ui.Place(_focusRing[0], outX, outY, outWidth, ring);
            Ui.Place(_focusRing[1], outX, outY + outHeight - ring, outWidth, ring);
            Ui.Place(_focusRing[2], outX, outY, ring, outHeight);
            Ui.Place(_focusRing[3], outX + outWidth - ring, outY, ring, outHeight);
        }
    }
}
