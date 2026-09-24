#nullable enable
using System;
using ChatGuard.Core;
using UnityEditor;
using UnityEngine;

namespace ChatGuard.Editor
{
    /// <summary>
    /// Editor window (Window → Chat Guard → Tester) that moderates one message with a <see cref="ChatGuardConfig"/>
    /// you pick, without entering Play mode. It shows the action, severity, per-category probabilities, and whether
    /// the server or a local fallback answered.
    /// </summary>
    /// <remarks>
    /// Use a <c>cg_test_</c> key: its calls are free but have a daily limit (see
    /// <see cref="ChatGuardSettings.ApiKey"/>). With no key, nothing is sent and the config's
    /// <see cref="ChatGuardConfig.offlineBehavior"/> answers (by default, the local filter).
    /// </remarks>
    public sealed class ChatGuardTesterWindow : EditorWindow
    {
        private ChatGuardConfig? _config;
        private string _message = string.Empty;
        private string _authorId = "editor-tester";
        private string _language = string.Empty;
        private ModerationResult? _result;
        private string? _status;
        private bool _busy;
        private ModerationOperation? _operation;

        [MenuItem("Window/Chat Guard/Tester")]
        public static void Open()
        {
            GetWindow<ChatGuardTesterWindow>("Chat Guard Tester");
        }

        private void OnGUI()
        {
            _config = (ChatGuardConfig?)EditorGUILayout.ObjectField("Config", _config, typeof(ChatGuardConfig), false);
            _authorId = EditorGUILayout.TextField("Author id", _authorId);
            _language = EditorGUILayout.TextField("Language (blank = config)", _language);
            EditorGUILayout.LabelField("Message");
            _message = EditorGUILayout.TextArea(_message, GUILayout.MinHeight(60));

            using (new EditorGUI.DisabledScope(_busy || _config == null || string.IsNullOrWhiteSpace(_message)))
            {
                if (GUILayout.Button(_busy ? "Moderating…" : "Moderate"))
                {
                    Run();
                }
            }

            if (_config != null && string.IsNullOrEmpty(_config.apiKey))
            {
                EditorGUILayout.HelpBox("The config has no API key: results come from the local filter only. Paste a cg_test_ key from the dashboard's API keys page to reach the server.", MessageType.Info);
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }

            if (_result != null)
            {
                DrawResult(_result);
            }
        }

        private void Run()
        {
            if (_config == null)
            {
                return;
            }

            ChatGuardClient client;
            try
            {
                client = new ChatGuardClient(_config);
            }
            catch (ArgumentException ex)
            {
                // Invalid base URL or timeout in the config: show why instead of throwing out of OnGUI.
                _status = ex.Message;
                _result = null;
                return;
            }

            _operation?.Cancel();
            _busy = true;
            _status = null;
            var request = new ModerationRequest(_message, _authorId) { language = string.IsNullOrEmpty(_language) ? null : _language };
            _operation = client.Moderate(request, result =>
            {
                _result = result;
                _status = result.Source == ResultSource.Server ? "Server answered in " + result.LatencyMs + " ms (" + result.Model + ")" : "Local filter: " + (result.Error ?? "offline");
                _busy = false;
                Repaint();
            });
        }

        private void OnDestroy()
        {
            _operation?.Cancel();
            _operation = null;
        }

        private static void DrawResult(ModerationResult result)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Action", ModerationActions.ToWireName(result.Action).ToUpperInvariant(), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Severity", result.Severity.ToString("F2"));
            foreach (VerdictCategory category in VerdictCategories.All)
            {
                float p = (float)result.Verdicts[category];
                Rect rect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(rect, p, VerdictCategories.ToWireName(category) + " " + p.ToString("F2"));
            }

            if (result.Target != null)
            {
                EditorGUILayout.LabelField("Target", TargetChoices.ToWireName(result.Target.Choice) + " (" + result.Target.Confidence.ToString("F2") + ")");
            }

            EditorGUILayout.LabelField("Degraded", result.Degraded ? DegradedReasons.ToWireName(result.DegradedReason) ?? "yes" : "no");
            if (result.QuotaLimit > 0)
            {
                EditorGUILayout.LabelField("Quota", result.QuotaUsed + " / " + result.QuotaLimit);
            }
        }
    }
}
