#nullable enable
using System;
using ChatGuard.Core;
using ChatGuard.Unity;
using UnityEditor;
using UnityEngine;

namespace ChatGuard.Editor
{
    /// <summary>Window → Chat Guard → Tester: paste a message and see the verdicts (uses the config's key, ideally a cg_test_ key).</summary>
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

            if (_config != null)
            {
                bool noKey = string.IsNullOrEmpty(_config.apiKey);
                bool noUrl = string.IsNullOrEmpty(_config.baseUrl);
                if (noKey || noUrl)
                {
                    string missing = noKey && noUrl ? "no API key and no base URL" : noKey ? "no API key" : "no base URL";
                    EditorGUILayout.HelpBox("The config has " + missing + ": results come from the local filter only. Both are needed to reach the server.", MessageType.Info);
                }
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
                // Invalid asset values (base URL, timeout): show the reason instead of getting stuck on "Moderating…".
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
