#nullable enable
using System.Collections.Generic;
using ChatGuard.Core;
using ChatGuard.Core.Scoring;
using ChatGuard.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>
    /// Builds a minimal chat UI at runtime (no prefabs needed): an input field, three threshold sliders
    /// (insult hide, threat block, severity block) and a log panel showing each verdict.
    /// Assign a ChatGuardConfig with a cg_test_ key for a live run, or leave the key empty to see the local filter.
    /// </summary>
    public sealed class BasicChatSample : MonoBehaviour
    {
        public ChatGuardConfig? config;

        private ChatGuardClient? _client;
        private InputField? _input;
        private Text? _log;
        private Slider? _insultHide;
        private Slider? _threatBlock;
        private Slider? _severityBlock;
        private readonly List<ThreadEntry> _thread = new List<ThreadEntry>();
        private readonly List<string> _lines = new List<string>();

        private void Start()
        {
            BuildUi();
            RebuildClient();
        }

        /// <summary>Builds the client from plain settings so the sliders never write into the assigned config asset.</summary>
        private void RebuildClient()
        {
            ChatGuardSettings settings = config != null ? config.ToSettings() : new ChatGuardSettings();
            Thresholds thresholds = (config != null ? config.thresholds : new ThresholdsOverride()).ToCore();
            thresholds.Insult.Hide = _insultHide != null ? _insultHide.value : 0.8f;
            thresholds.Threat.Block = _threatBlock != null ? _threatBlock.value : 0.85f;
            thresholds.SeverityBlock = _severityBlock != null ? _severityBlock.value : 2.5f;
            settings.Thresholds = thresholds;
            _client = new ChatGuardClient(settings);
        }

        private void Send()
        {
            if (_input == null || _client == null || string.IsNullOrWhiteSpace(_input.text))
            {
                return;
            }

            string message = _input.text;
            _input.text = string.Empty;
            _input.ActivateInputField();
            var request = new ModerationRequest(message, "player-1") { thread = new List<ThreadEntry>(_thread) };
            _client.Moderate(request, result =>
            {
                string verdicts = string.Empty;
                foreach (VerdictCategory c in VerdictCategories.All)
                {
                    if (result.Verdicts[c] >= 0.3)
                    {
                        verdicts += " " + VerdictCategories.ToWireName(c) + "=" + result.Verdicts[c].ToString("F2");
                    }
                }

                Log((result.ShouldDeliver ? "<color=#8f8>" : "<color=#f88>") + ModerationActions.ToWireName(result.Action).ToUpperInvariant() + "</color> " + (result.ShouldDeliver ? message : "[" + (result.Action == ModerationAction.Hide ? "hidden" : "blocked") + "]") + "  <size=11>sev " + result.Severity.ToString("F2") + verdicts + (result.Degraded ? " degraded:" + DegradedReasons.ToWireName(result.DegradedReason) : string.Empty) + " " + result.LatencyMs + "ms</size>");
                if (result.ShouldDeliver)
                {
                    _thread.Add(new ThreadEntry("player-1", message));
                    if (_thread.Count > 5)
                    {
                        _thread.RemoveAt(0);
                    }
                }
            });
        }

        private void Log(string line)
        {
            _lines.Add(line);
            if (_lines.Count > 14)
            {
                _lines.RemoveAt(0);
            }

            if (_log != null)
            {
                _log.text = string.Join("\n", _lines);
            }
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280, 720);
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            // Unity 2022.2 renamed the built-in UI font; each name throws or returns null on the other side of that release.
#if UNITY_2022_2_OR_NEWER
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif

            _log = MakeText(canvasGo.transform, "Log", font, new Vector2(20, 120), new Vector2(880, 560), 16, TextAnchor.LowerLeft);
            _log.supportRichText = true;
            Log("<size=11>Chat Guard sample: type a message and press Enter. Sliders change the local thresholds.</size>");

            _input = MakeInput(canvasGo.transform, font, new Vector2(20, 40), new Vector2(880, 60));
            _input.onSubmit.AddListener(_ => Send());

            _insultHide = MakeSlider(canvasGo.transform, font, "insult hide ≥", new Vector2(940, 620), 0.8f, 0f, 1f);
            _threatBlock = MakeSlider(canvasGo.transform, font, "threat block ≥", new Vector2(940, 540), 0.85f, 0f, 1f);
            _severityBlock = MakeSlider(canvasGo.transform, font, "severity block ≥", new Vector2(940, 460), 2.5f, 0f, 3f);
            foreach (Slider s in new[] { _insultHide, _threatBlock, _severityBlock })
            {
                s.onValueChanged.AddListener(_ => RebuildClient());
            }
        }

        private static Text MakeText(Transform parent, string name, Font font, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            return text;
        }

        private static InputField MakeInput(Transform parent, Font font, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.18f, 0.95f);
            Text text = MakeText(go.transform, "Text", font, new Vector2(12, 0), new Vector2(size.x - 24, size.y), 20, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.sizeDelta = new Vector2(-24, 0);
            text.rectTransform.anchoredPosition = new Vector2(12, 0);
            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Slider MakeSlider(Transform parent, Font font, string label, Vector2 pos, float value, float min, float max)
        {
            Text caption = MakeText(parent, label, font, pos + new Vector2(0, 30), new Vector2(320, 24), 14, TextAnchor.MiddleLeft);
            var go = new GameObject(label, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320, 24);
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            bg.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            bg.GetComponent<RectTransform>().anchorMax = Vector2.one;
            bg.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.35f);
            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            fillArea.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            fillArea.GetComponent<RectTransform>().anchorMax = Vector2.one;
            fillArea.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            fill.GetComponent<Image>().color = new Color(0.35f, 0.55f, 0.95f);
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            handleArea.GetComponent<RectTransform>().anchorMin = Vector2.zero;
            handleArea.GetComponent<RectTransform>().anchorMax = Vector2.one;
            handleArea.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            handle.GetComponent<RectTransform>().sizeDelta = new Vector2(16, 0);
            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.onValueChanged.AddListener(v => caption.text = label + " " + v.ToString("F2"));
            caption.text = label + " " + value.ToString("F2");
            return slider;
        }
    }
}
