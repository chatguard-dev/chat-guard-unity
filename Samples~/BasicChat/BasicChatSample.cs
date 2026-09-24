#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using ChatGuard.Core.Scoring;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ChatGuard.Samples
{
    /// <summary>
    /// A chat screen built at runtime with Unity UI, no prefabs. Type as any player, or play the example chat from
    /// chatguard.dev. Each line shows "checking" in the Chat Guard log for as long as its check takes, then its action,
    /// a short reason (see <see cref="Wording.Reason"/>) and, for a server answer, the time. Global chat shows what
    /// other players get: only allowed and flagged lines. Last check shows the selected line's six scores.
    /// </summary>
    /// <remarks>
    /// With a key (a <c>cg_test_</c> key in the Editor) you see the server's verdicts. With no key, the local filter
    /// (built-in word lists) answers on the device, and each result shows <c>degraded: offline</c>.
    /// The sliders affect only decisions the client makes itself with the local filter. By default it makes them with
    /// no key, with no usable server answer, and when it re-checks a degraded one (see
    /// <see cref="ChatGuardSettings.OfflineBehavior"/> and <see cref="ChatGuardSettings.LocalFilterWhenDegraded"/>).
    /// Other server verdicts use your dashboard thresholds.
    /// </remarks>
    public sealed class BasicChatSample : MonoBehaviour
    {
        /// <summary>Players you can speak as.</summary>
        internal static readonly string[] PlayerNames = { "nova", "brakk", "slayer_x", "mira", "g0ldshop" };

        /// <summary>
        /// Their author ids, sent with each message and as thread authors. Use your players' stable ids, never a real
        /// name or email.
        /// </summary>
        private static readonly string[] PlayerIds = { "player-1", "player-2", "player-3", "player-4", "player-5" };

        /// <summary>The example chat from chatguard.dev: who speaks and what they say. The players and lines are made up.</summary>
        private static readonly (int Player, string Text)[] Example =
        {
            (0, "gg, that last round was close"),
            (1, "ez game, too easy lol"),
            (2, "you're a worthless idiot, uninstall"),
            (3, "anyone trading an ice sword? paying 2k gold"),
            (4, "cheap accounts with all skins, $20 paypal, dm me"),
            (2, "you're a worthless idiot, uninstall"),
            (2, "I know where you live"),
            (0, "anyway, rematch?"),
        };

        /// <summary>Time between the example's lines.</summary>
        private static readonly WaitForSeconds ExampleStep = new WaitForSeconds(0.82f);

        /// <summary>Lines kept in the log; older ones are dropped.</summary>
        private const int MaxEntries = 40;

        /// <summary>
        /// Optional config asset. When empty, the sample uses the settings of <see cref="ChatGuardSdk.Client"/>.
        /// Thresholds come from this asset's <see cref="ChatGuardConfig.thresholds"/>, even with
        /// <see cref="ChatGuardConfig.overrideThresholds"/> unticked, or from the built-in defaults when this field is
        /// empty. The sliders replace three of them.
        /// </summary>
        public ChatGuardConfig? config;

        /// <summary>The fonts the UI uses. The sample scene assigns the bundled ones.</summary>
        public BasicChatFonts fonts = new BasicChatFonts();

        private readonly List<ChatEntry> _entries = new List<ChatEntry>();
        private CancellationTokenSource? _cancel;
        private ChatGuardClient? _client;
        private SampleTextures? _textures;
        private BasicChatView? _view;
        private Coroutine? _example;

        /// <summary>True when the client re-checks a degraded server answer on the device.</summary>
        private bool _rechecksDegraded;

        internal IReadOnlyList<ChatEntry> Entries => _entries;

        /// <summary>Index of the line Last check shows, or -1. Each new line is selected as it is sent.</summary>
        internal int Selected { get; private set; } = -1;

        internal bool Playing { get; private set; }

        /// <summary>Index of the player new messages are sent as.</summary>
        internal int Speaker { get; set; }

        internal float InsultHide { get; private set; } = 0.8f;
        internal float ThreatBlock { get; private set; } = 0.85f;
        internal float SeverityBlock { get; private set; } = 2.5f;

        internal bool HasKey => _client != null && _client.HasServer;

        /// <summary>The connection chip's text: which kind of key checks messages, or what answers without one.</summary>
        internal string ConnectionLong { get; private set; } = string.Empty;
        internal string ConnectionShort { get; private set; } = string.Empty;

        private void Awake()
        {
            _cancel = new CancellationTokenSource();
            RebuildClient();
        }

        private void OnEnable()
        {
            _textures = new SampleTextures();
            _view = new BasicChatView(this, transform, fonts, _textures);
            _view.Refresh();
        }

        private void Start()
        {
            if (EventSystem.current == null)
            {
                // A project that reads input only through the Input System package can't use StandaloneInputModule.
                // CHATGUARD_INPUT_SYSTEM comes from this sample's assembly definition when that package (1.1 or newer,
                // which gives a module added from code its default UI actions) is installed.
#if CHATGUARD_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            }
        }

        private void Update()
        {
            _view?.Tick(Time.unscaledTime);
        }

        private void OnDisable()
        {
            StopExample();
            _view?.Destroy();
            _view = null;
            _textures?.Destroy();
            _textures = null;
        }

        private void OnDestroy()
        {
            // Stops checks still in flight, so no callback runs after the sample is gone.
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
        }

        /// <summary>Sends <paramref name="text"/> as the given player, with the delivered lines before it as context.</summary>
        internal void Send(int player, string text)
        {
            text = text.Trim();
            if (_client == null || _cancel == null || text.Length == 0)
            {
                return;
            }

            var thread = new List<ThreadEntry>();
            foreach (ChatEntry earlier in _entries)
            {
                if (earlier.Delivered)
                {
                    thread.Add(new ThreadEntry(earlier.AuthorId, earlier.Text));
                }
            }

            var entry = new ChatEntry(PlayerNames[player], PlayerIds[player], text, SeverityBlock, _rechecksDegraded);
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            Selected = _entries.Count - 1;
            var request = new ModerationRequest(text, entry.AuthorId) { thread = thread };
            _client.Moderate(request, result => Land(entry, result), _cancel.Token);

            // An answer that lands inside Moderate, as it does with no key, has refreshed the view already.
            if (entry.Result == null)
            {
                _view?.Refresh();
            }
        }

        /// <summary>
        /// Empties the log and plays the example chat through the client, one line every 820 ms. Ignored while it plays.
        /// </summary>
        internal void PlayExample()
        {
            if (!Playing)
            {
                _example = StartCoroutine(PlayExampleLines());
            }
        }

        /// <summary>Stops the example and empties the log, the players' view and the selection.</summary>
        internal void Clear()
        {
            StopExample();
            Empty();
        }

        internal void Select(int entry)
        {
            Selected = entry;
            _view?.Refresh();
        }

        /// <summary>Sets insult hide (0), threat block (1) or severity block (2).</summary>
        internal void SetThreshold(int which, float value)
        {
            switch (which)
            {
                case 0: InsultHide = value; break;
                case 1: ThreatBlock = value; break;
                default: SeverityBlock = value; break;
            }

            RebuildClient();
        }

        private IEnumerator PlayExampleLines()
        {
            Playing = true;
            Empty();
            foreach ((int player, string text) in Example)
            {
                Send(player, text);
                yield return ExampleStep;
            }

            Playing = false;
            _example = null;
            _view?.Refresh();
        }

        private void StopExample()
        {
            if (_example != null)
            {
                StopCoroutine(_example);
                _example = null;
            }

            Playing = false;
        }

        private void Empty()
        {
            _entries.Clear();
            Selected = -1;
            _view?.Refresh();
        }

        private void Land(ChatEntry entry, ModerationResult result)
        {
            entry.Result = result;
            entry.LandedAt = Time.unscaledTime;
            _view?.Refresh();
        }

        /// <summary>
        /// Creates a new client with the slider values, because a client keeps the settings it was built with. It
        /// edits copies, so the config asset and the shared client stay unchanged.
        /// </summary>
        private void RebuildClient()
        {
            ChatGuardSettings settings = config != null ? config.ToSettings() : ChatGuardSdk.Client.Settings;
            Thresholds thresholds = (config != null ? config.thresholds : new ThresholdsOverride()).ToCore();
            thresholds.Insult.Hide = InsultHide;
            thresholds.Threat.Block = ThreatBlock;
            thresholds.SeverityBlock = SeverityBlock;
            settings.Thresholds = thresholds;
            _client = new ChatGuardClient(settings);
            _rechecksDegraded = settings.LocalFilterWhenDegraded && settings.OfflineBehavior == OfflineBehavior.LocalFilter;

            string key = settings.ApiKey ?? string.Empty;
            string kind = key.StartsWith("cg_test_", StringComparison.Ordinal) ? "Test key"
                : key.StartsWith("cg_pub_", StringComparison.Ordinal) ? "Publishable key"
                : key.StartsWith("cg_live_", StringComparison.Ordinal) ? "Server key"
                : "Key";
            string fallback = settings.OfflineBehavior == OfflineBehavior.AllowAll ? "allow all"
                : settings.OfflineBehavior == OfflineBehavior.BlockAll ? "block all"
                : "local word filter";
            ConnectionLong = _client.HasServer ? kind + ": checked by Chat Guard" : "No key: " + fallback;
            ConnectionShort = _client.HasServer ? kind : "No key";
        }
    }

    /// <summary>The fonts of the sample's UI.</summary>
    [Serializable]
    public sealed class BasicChatFonts
    {
        /// <summary>Headings and the wordmark: Inter Tight Medium. Empty: Unity's built-in font.</summary>
        public Font? display;

        /// <summary>Messages, notes and fields: Inter Regular. Empty: Unity's built-in font.</summary>
        public Font? text;

        /// <summary>Buttons and the player picker: Inter Medium. Empty: the text font.</summary>
        public Font? textMedium;

        /// <summary>Player names: Inter SemiBold. Empty: the text font.</summary>
        public Font? textSemiBold;

        /// <summary>Window bars, badges, scores and times: JetBrains Mono Regular. Empty: Unity's built-in font.</summary>
        public Font? mono;

        /// <summary>The score behind a decision in Last check: JetBrains Mono Medium. Empty: the mono font.</summary>
        public Font? monoMedium;
    }

    /// <summary>One sent line: who sent it, the text, and its result once the check lands.</summary>
    internal sealed class ChatEntry
    {
        public ChatEntry(string playerName, string authorId, string text, double localSeverityBlock, bool rechecksDegraded)
        {
            PlayerName = playerName;
            AuthorId = authorId;
            Text = text;
            LocalSeverityBlock = localSeverityBlock;
            RechecksDegraded = rechecksDegraded;
        }

        public string PlayerName { get; }
        public string AuthorId { get; }
        public string Text { get; }

        /// <summary>
        /// The severity block slider's value when the line was sent, the one a decision on the device uses.
        /// </summary>
        public double LocalSeverityBlock { get; }

        /// <summary>
        /// True when the client that checked the line re-checks a degraded server answer on the device, which
        /// recomputes its action with the sliders' values.
        /// </summary>
        public bool RechecksDegraded { get; }

        /// <summary>Null while checking.</summary>
        public ModerationResult? Result { get; set; }

        /// <summary>When the result landed, in unscaled seconds, for the players' view's enter animation.</summary>
        public float LandedAt { get; set; } = float.NegativeInfinity;

        /// <summary>True once other players see the line: its action is allow or flag.</summary>
        public bool Delivered => Result != null && Result.ShouldDeliver;
    }
}
