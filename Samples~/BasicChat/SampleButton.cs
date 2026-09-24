#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChatGuard.Samples
{
    /// <summary>Frame, fill and label colors of a button in one state.</summary>
    internal readonly struct ButtonTone
    {
        public ButtonTone(Color frame, Color fill, Color label)
        {
            Frame = frame;
            Fill = fill;
            Label = label;
        }

        public Color Frame { get; }
        public Color Fill { get; }
        public Color Label { get; }
    }

    /// <summary>The tones of a button at rest, under the pointer and disabled.</summary>
    internal readonly struct ButtonTones
    {
        public static readonly ButtonTones Primary = new ButtonTones(
            new ButtonTone(Palette.Ink, Palette.Ink, Palette.Paper),
            new ButtonTone(Palette.Ink, Palette.Pink, Palette.Ink),
            new ButtonTone(Palette.Grey2, Palette.Grey1, Palette.Muted));

        public static readonly ButtonTones Secondary = new ButtonTones(
            new ButtonTone(Palette.Ink, Palette.Paper, Palette.Ink),
            new ButtonTone(Palette.Ink, Palette.Ink, Palette.Paper),
            new ButtonTone(Palette.Grey2, Palette.Grey1, Palette.Muted));

        public ButtonTones(ButtonTone normal, ButtonTone hover, ButtonTone disabled)
        {
            Normal = normal;
            Hover = hover;
            Disabled = disabled;
        }

        public ButtonTone Normal { get; }
        public ButtonTone Hover { get; }
        public ButtonTone Disabled { get; }
    }

    /// <summary>
    /// A <see cref="Button"/> that paints its frame, fill and label (and any <see cref="Marks"/>, in the label's color)
    /// from <see cref="Tones"/>, and moves its <see cref="Body"/> 1px down while pressed. It never takes keyboard
    /// selection, so clicking it does not leave it stuck in a selected look.
    /// </summary>
    internal sealed class SampleButton : Button
    {
        private ButtonTones _tones = ButtonTones.Secondary;
        private bool _pointerInside;
        private bool _busy;

        public Graphic? Frame { get; set; }
        public Graphic? FillGraphic { get; set; }
        public Text? Label { get; set; }
        public Graphic[]? Marks { get; set; }

        /// <summary>Moved 1px down while pressed; null for buttons that do not move.</summary>
        public RectTransform? Body { get; set; }

        public ButtonTones Tones
        {
            get => _tones;
            set
            {
                _tones = value;
                Paint();
            }
        }

        /// <summary>While busy the button keeps its resting colors and ignores clicks.</summary>
        public bool Busy
        {
            get => _busy;
            set
            {
                _busy = value;
                Paint();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            transition = Transition.None;
            navigation = new Navigation { mode = Navigation.Mode.None };
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            base.OnPointerEnter(eventData);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            base.OnPointerExit(eventData);
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (!_busy)
            {
                base.OnPointerClick(eventData);
            }
        }

        /// <summary>Runs on disable, which no pointer exit follows, and on focus loss while pressed.</summary>
        protected override void InstantClearState()
        {
            _pointerInside = false;
            base.InstantClearState();
            Paint();
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            Paint();
        }

        private void Paint()
        {
            bool disabled = !IsInteractable();
            bool hover = _pointerInside && !disabled && !_busy;
            ButtonTone tone = disabled ? _tones.Disabled : hover ? _tones.Hover : _tones.Normal;
            if (Frame != null)
            {
                Frame.color = tone.Frame;
            }

            if (FillGraphic != null)
            {
                FillGraphic.color = tone.Fill;
            }

            if (Label != null)
            {
                Label.color = tone.Label;
            }

            if (Marks != null)
            {
                foreach (Graphic mark in Marks)
                {
                    mark.color = tone.Label;
                }
            }

            if (Body != null)
            {
                Body.anchoredPosition = new Vector2(0f, IsPressed() && !_busy ? -1f : 0f);
            }
        }
    }
}
