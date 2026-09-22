#nullable enable
using System;
using ChatGuard.Core.Scoring;
using UnityEngine;

namespace ChatGuard.Unity
{
    public enum OfflineBehavior
    {
        /// <summary>Deliver every message when the server cannot be reached.</summary>
        AllowAll,

        /// <summary>Run the built-in dictionary filter (same word lists as the server's fallback).</summary>
        LocalFilter,

        /// <summary>Hold every message until the server is reachable again.</summary>
        BlockAll,
    }

    /// <summary>Per-category thresholds; a negative value disables that level (mirrors the dashboard editor).</summary>
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

    [Serializable]
    public sealed class ThresholdsOverride
    {
        public CategoryThresholdsOverride insult = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride threat = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = 0.85f };

        public CategoryThresholdsOverride hate = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride sexual = new CategoryThresholdsOverride();

        public CategoryThresholdsOverride spam = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = -1f };

        public CategoryThresholdsOverride trading = new CategoryThresholdsOverride { flag = 0.55f, hide = -1f, block = -1f };

        [Range(0f, 4f)] public float severityBlock = 2.5f;

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
    /// Optional inspector-editable settings asset for <see cref="ChatGuardClient"/>. Create one via Assets → Create →
    /// Chat Guard → Config and save it as <c>Assets/Resources/ChatGuardConfig.asset</c> for the zero-code path; the same
    /// options are available from code through <see cref="ChatGuardSettings"/> (<see cref="ToSettings"/> converts).
    /// A client build may only contain a publishable (cg_pub_) or test (cg_test_) key; cg_live_ server keys
    /// belong on your game server or relay (see README, "Where the key lives").
    /// </summary>
    [CreateAssetMenu(menuName = "Chat Guard/Config", fileName = "ChatGuardConfig")]
    public sealed class ChatGuardConfig : ScriptableObject
    {
        [Header("Connection")]
        [Tooltip("Only cg_pub_ (publishable) or cg_test_ keys may ship in a client build. cg_live_ server keys stay on your server or relay. Empty = local filter only.")]
        public string apiKey = string.Empty;

        [Tooltip("Chat Guard API base URL, for example https://api.chatguard.dev. Empty = local filter only.")]
        public string baseUrl = string.Empty;

        [Tooltip("Whole request budget in seconds, rounded up to whole seconds (minimum 1 s); on expiry the offline behaviour applies.")]
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

        [Header("Thresholds (override the project's server-side thresholds for local decisions)")]
        public bool overrideThresholds;

        public ThresholdsOverride thresholds = new ThresholdsOverride();

        public Thresholds EffectiveThresholds => overrideThresholds ? thresholds.ToCore() : Thresholds.Default();

        /// <summary>
        /// The asset's values as a plain <see cref="ChatGuardSettings"/> (a fresh object each call). The thresholds
        /// override is copied only when <see cref="overrideThresholds"/> is ticked; otherwise
        /// <see cref="ChatGuardSettings.Thresholds"/> is null, which means the built-in defaults.
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
