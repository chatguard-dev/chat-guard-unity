#nullable enable
using System;
using ChatGuard.Core.Scoring;
using UnityEngine;

namespace ChatGuard
{
    /// <summary>
    /// What the client answers when the server gives no usable answer (see
    /// <see cref="ChatGuardSettings.OfflineBehavior"/>). These answers are degraded
    /// (<see cref="ModerationResult.Degraded"/>) and come from <see cref="ResultSource.Local"/>.
    /// </summary>
    public enum OfflineBehavior
    {
        /// <summary>Allow every message (severity 0).</summary>
        AllowAll,

        /// <summary>
        /// Check each message with the local filter (built-in word lists, run on the device).
        /// <see cref="ChatGuardSettings.Thresholds"/> sets the action.
        /// </summary>
        LocalFilter,

        /// <summary>Block every message (severity 3).</summary>
        BlockAll,
    }

    /// <summary>
    /// Inspector form of <see cref="CategoryThresholds"/>: flag, hide and block levels (0 to 1) for one category. A
    /// negative value turns a level off, like an empty field on the dashboard.
    /// </summary>
    [Serializable]
    public sealed class CategoryThresholdsOverride
    {
        [Range(-1f, 1f)] public float flag = 0.55f;

        [Range(-1f, 1f)] public float hide = 0.8f;

        [Range(-1f, 1f)] public float block = -1f;

        public CategoryThresholds ToCore()
        {
            return new CategoryThresholds(flag < 0 ? (double?)null : flag, hide < 0 ? (double?)null : hide, block < 0 ? (double?)null : block);
        }
    }

    /// <summary>
    /// Inspector form of <see cref="Thresholds"/>, used when <see cref="ChatGuardConfig.overrideThresholds"/> is
    /// ticked. Its defaults are the built-in ones.
    /// </summary>
    [Serializable]
    public sealed class ThresholdsOverride
    {
        public CategoryThresholdsOverride insult = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride threat = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = 0.85f };

        public CategoryThresholdsOverride hate = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride sexual = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride spam = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = -1f };

        public CategoryThresholdsOverride trading = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = -1f };

        /// <summary>
        /// Severity (0 to 3) at or above which a message is blocked. Above 3 turns severity blocking off.
        /// </summary>
        [Range(0f, 4f)] public float severityBlock = 2.5f;

        /// <summary>
        /// Flags a message when the model's score confidence is below this. No effect in the Unity client, whose local
        /// decisions have no model score.
        /// </summary>
        [Range(0f, 1f)] public float flagBelowScoreConfidence = 0.5f;

        public Thresholds ToCore()
        {
            return new Thresholds
            {
                Insult = insult.ToCore(),
                Threat = threat.ToCore(),
                Hate = hate.ToCore(),
                Sexual = sexual.ToCore(),
                Spam = spam.ToCore(),
                Trading = trading.ToCore(),
                SeverityBlock = severityBlock,
                FlagBelowScoreConfidence = flagBelowScoreConfidence,
            };
        }
    }

    /// <summary>
    /// Optional settings asset for <see cref="ChatGuardClient"/>, edited in the Inspector.
    /// <see cref="ChatGuardSettings"/> holds the same options for setup from code, and <see cref="ToSettings"/>
    /// converts an asset into one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For setup without code, create one (Assets → Create → Chat Guard → Config) and save it as
    /// <c>Assets/Resources/ChatGuardConfig.asset</c>. <see cref="ChatGuardSdk"/> loads it on first use unless
    /// <c>Configure</c> was called. <see cref="ChatGuardSdk.Configure(ChatGuardConfig)"/> and a
    /// <see cref="ChatGuardUnityHook"/> also take any config asset.
    /// </para>
    /// <para>
    /// For which key to use, see <see cref="ChatGuardSettings.ApiKey"/>. A player build other than a Dedicated Server
    /// build fails if a config asset it ships holds a <c>cg_live_</c> key. A build with Development Build unticked
    /// also fails on a <c>cg_test_</c> key.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Chat Guard/Config", fileName = "ChatGuardConfig")]
    public sealed class ChatGuardConfig : ScriptableObject
    {
        [Header("Connection")]
        [Tooltip("The key from the dashboard (API keys page); the only field you must fill in. Client builds ship a cg_pub_ (publishable) key; cg_test_ keys only in the Editor and development builds; cg_live_ server keys stay on your server or relay. Empty = local filter only.")]
        public string apiKey = string.Empty;

        [Tooltip("Where requests go. Keep the default (https://api.chatguard.dev) unless a proxy of your own serves /v1/moderate. Empty also means the default.")]
        public string baseUrl = ChatGuardSettings.DefaultBaseUrl;

        [Tooltip("Whole request budget in seconds, rounded up to whole seconds (minimum 1 s); on expiry the offline behavior applies.")]
        [Range(0.5f, 10f)] public float timeoutSeconds = 2f;

        [Header("Defaults sent with every message")]
        public string defaultLanguage = "en";

        [Tooltip("global | team | dm | guild")]
        public string channelType = "global";

        [Tooltip("Age rating sent as channel.age_rating, for example 16+. Empty sends none.")]
        public string ageRating = "16+";

        [Header("Fallback")]
        public OfflineBehavior offlineBehavior = OfflineBehavior.LocalFilter;

        [Tooltip("Re-run the local filter with the thresholds below when the server answers with degraded=true.")]
        public bool localFilterWhenDegraded = true;

        /// <summary>
        /// When ticked, the client's local-filter decisions use <see cref="thresholds"/> instead of the built-in
        /// defaults. Your project's thresholds on the dashboard are not affected.
        /// </summary>
        [Header("Thresholds (override the project's server-side thresholds for local decisions)")]
        public bool overrideThresholds;

        public ThresholdsOverride thresholds = new ThresholdsOverride();

        /// <summary>
        /// The thresholds a client set up from this asset uses for its local-filter decisions:
        /// <see cref="thresholds"/> when <see cref="overrideThresholds"/> is ticked, otherwise the built-in defaults.
        /// A new object on every read.
        /// </summary>
        public Thresholds EffectiveThresholds => overrideThresholds ? thresholds.ToCore() : Thresholds.Default();

        /// <summary>
        /// Returns the asset's values as a new <see cref="ChatGuardSettings"/>, which you can adjust in code. Its
        /// <see cref="ChatGuardSettings.Thresholds"/> is null (the built-in defaults) unless
        /// <see cref="overrideThresholds"/> is ticked.
        /// </summary>
        public ChatGuardSettings ToSettings()
        {
            return new ChatGuardSettings
            {
                ApiKey = apiKey ?? string.Empty,
                BaseUrl = baseUrl ?? string.Empty,
                TimeoutSeconds = timeoutSeconds,
                OfflineBehavior = offlineBehavior,
                LocalFilterWhenDegraded = localFilterWhenDegraded,
                DefaultLanguage = defaultLanguage ?? string.Empty,
                ChannelType = channelType ?? string.Empty,
                AgeRating = ageRating,
                Thresholds = overrideThresholds ? thresholds.ToCore() : null,
            };
        }
    }
}
