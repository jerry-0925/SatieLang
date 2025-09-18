using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Satie;

public class SatieAPIKeyManagerWindow : EditorWindow
{
    private Vector2 scrollPosition;
    private Dictionary<SatieAPIKeyManager.Provider, string> tempKeys = new Dictionary<SatieAPIKeyManager.Provider, string>();
    private Dictionary<SatieAPIKeyManager.Provider, bool> showKey = new Dictionary<SatieAPIKeyManager.Provider, bool>();
    private Dictionary<SatieAPIKeyManager.Provider, string> endpoints = new Dictionary<SatieAPIKeyManager.Provider, string>();

    private GUIStyle headerStyle;
    private GUIStyle successStyle;
    private GUIStyle errorStyle;
    private bool showEndpoints = false;
    private bool autoMigrate = true;

    [MenuItem("Satie/API Key Manager")]
    public static void ShowWindow()
    {
        var window = GetWindow<SatieAPIKeyManagerWindow>("Satie API Keys");
        window.minSize = new Vector2(450, 400);
        window.Show();
    }

    private void OnEnable()
    {
        RefreshKeys();

        // Auto-migrate on first open
        if (EditorPrefs.GetBool("Satie_FirstAPIKeyManagerOpen", true))
        {
            EditorPrefs.SetBool("Satie_FirstAPIKeyManagerOpen", false);

            if (autoMigrate)
            {
                SatieAPIKeyManager.MigrateLegacyKeys();
                RefreshKeys();
            }
        }
    }

    private void RefreshKeys()
    {
        tempKeys.Clear();
        showKey.Clear();
        endpoints.Clear();

        foreach (SatieAPIKeyManager.Provider provider in Enum.GetValues(typeof(SatieAPIKeyManager.Provider)))
        {
            if (provider == SatieAPIKeyManager.Provider.Custom) continue;

            string key = SatieAPIKeyManager.GetKey(provider);
            tempKeys[provider] = key ?? "";
            showKey[provider] = false;
            endpoints[provider] = SatieAPIKeyManager.GetEndpoint(provider);
        }
    }

    private void OnGUI()
    {
        InitializeStyles();

        // Header
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🔐 Satie API Key Manager", headerStyle);
        EditorGUILayout.Space(5);

        // Info box
        EditorGUILayout.HelpBox(
            "Manage all your API keys in one secure location.\n" +
            "Keys are encrypted and stored in: " + Application.persistentDataPath,
            MessageType.Info
        );

        EditorGUILayout.Space(10);

        // Toolbar
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Refresh", GUILayout.Width(80)))
        {
            RefreshKeys();
        }

        if (GUILayout.Button("Validate All", GUILayout.Width(80)))
        {
            SatieAPIKeyManager.ValidateAllKeys();
            RefreshKeys();
        }

        if (GUILayout.Button("Migrate Legacy", GUILayout.Width(100)))
        {
            SatieAPIKeyManager.MigrateLegacyKeys();
            RefreshKeys();
            ShowNotification(new GUIContent("Legacy keys migrated"));
        }

        GUILayout.FlexibleSpace();

        showEndpoints = EditorGUILayout.ToggleLeft("Show Endpoints", showEndpoints, GUILayout.Width(110));

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // Scroll view for providers
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // Create a copy of the keys to iterate over to avoid collection modification errors
        var providers = new List<SatieAPIKeyManager.Provider>(tempKeys.Keys);
        foreach (var provider in providers)
        {
            DrawProviderSection(provider);
            EditorGUILayout.Space(5);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);

        // Environment Variables Info
        if (EditorGUILayout.Foldout(EditorPrefs.GetBool("Satie_ShowEnvInfo", false), "Environment Variables"))
        {
            EditorPrefs.SetBool("Satie_ShowEnvInfo", true);
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "You can also set API keys using environment variables:\n" +
                "• SATIE_API_KEY_OPENAI\n" +
                "• SATIE_API_KEY_ELEVENLABS\n" +
                "• SATIE_API_KEY_ANTHROPIC\n" +
                "• SATIE_API_KEY_GOOGLE\n\n" +
                "Environment variables take priority over saved keys.",
                MessageType.None
            );
            EditorGUI.indentLevel--;
        }
        else
        {
            EditorPrefs.SetBool("Satie_ShowEnvInfo", false);
        }

        EditorGUILayout.Space(10);

        // Status bar
        DrawStatusBar();
    }

    private void DrawProviderSection(SatieAPIKeyManager.Provider provider)
    {
        // Check if the dictionaries contain the provider key to avoid errors
        if (!tempKeys.ContainsKey(provider) || !showKey.ContainsKey(provider) || !endpoints.ContainsKey(provider))
        {
            return;
        }

        bool hasKey = SatieAPIKeyManager.HasKey(provider);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Provider header
        EditorGUILayout.BeginHorizontal();

        // Provider name with status indicator
        string statusIcon = hasKey ? "✓" : "✗";
        Color statusColor = hasKey ? Color.green : Color.red;

        var prevColor = GUI.color;
        GUI.color = statusColor;
        EditorGUILayout.LabelField(statusIcon, GUILayout.Width(20));
        GUI.color = prevColor;

        EditorGUILayout.LabelField(GetProviderDisplayName(provider), EditorStyles.boldLabel);

        if (hasKey)
        {
            EditorGUILayout.LabelField("Configured", successStyle, GUILayout.Width(80));
        }
        else
        {
            EditorGUILayout.LabelField("Not Set", errorStyle, GUILayout.Width(80));
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(3);

        // API Key field
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("API Key:");

        if (!showKey[provider] && !string.IsNullOrEmpty(tempKeys[provider]))
        {
            // Show masked key
            string masked = MaskApiKey(tempKeys[provider]);
            EditorGUILayout.TextField(masked);
        }
        else
        {
            // Show actual key input
            string newKey = EditorGUILayout.TextField(tempKeys[provider]);
            tempKeys[provider] = newKey;
        }

        // Toggle show/hide
        if (!string.IsNullOrEmpty(tempKeys[provider]))
        {
            if (GUILayout.Button(showKey[provider] ? "Hide" : "Show", GUILayout.Width(50)))
            {
                showKey[provider] = !showKey[provider];
            }
        }

        EditorGUILayout.EndHorizontal();

        // Endpoint field (if shown)
        if (showEndpoints)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Endpoint:");
            string newEndpoint = EditorGUILayout.TextField(endpoints[provider]);
            endpoints[provider] = newEndpoint;
            EditorGUILayout.EndHorizontal();
        }

        // Action buttons
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (!string.IsNullOrEmpty(tempKeys[provider]))
        {
            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                SatieAPIKeyManager.SetKey(
                    provider,
                    tempKeys[provider],
                    showEndpoints ? endpoints[provider] : null
                );
                ShowNotification(new GUIContent($"{provider} key saved"));
                RefreshKeys();
            }
        }

        if (hasKey)
        {
            if (GUILayout.Button("Remove", GUILayout.Width(60)))
            {
                if (EditorUtility.DisplayDialog(
                    "Remove API Key",
                    $"Are you sure you want to remove the API key for {provider}?",
                    "Remove", "Cancel"))
                {
                    SatieAPIKeyManager.RemoveKey(provider);
                    ShowNotification(new GUIContent($"{provider} key removed"));
                    RefreshKeys();
                }
            }
        }

        // Provider-specific help button
        if (GUILayout.Button("?", GUILayout.Width(25)))
        {
            ShowProviderHelp(provider);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        var configured = SatieAPIKeyManager.GetConfiguredProviders();
        int totalProviders = Enum.GetValues(typeof(SatieAPIKeyManager.Provider)).Length - 1; // Exclude Custom

        EditorGUILayout.LabelField($"Status: {configured.Count}/{totalProviders} providers configured");

        EditorGUILayout.EndHorizontal();
    }

    private string GetProviderDisplayName(SatieAPIKeyManager.Provider provider)
    {
        return provider switch
        {
            SatieAPIKeyManager.Provider.OpenAI => "OpenAI (GPT, DALL-E, Whisper)",
            SatieAPIKeyManager.Provider.ElevenLabs => "ElevenLabs (Voice Synthesis)",
            SatieAPIKeyManager.Provider.Anthropic => "Anthropic (Claude)",
            SatieAPIKeyManager.Provider.Google => "Google (Gemini, PaLM)",
            SatieAPIKeyManager.Provider.Azure => "Azure OpenAI Service",
            _ => provider.ToString()
        };
    }

    private string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";

        int visibleChars = 8;
        if (key.Length <= visibleChars)
        {
            return new string('•', key.Length);
        }

        string prefix = key.Substring(0, 4);
        string suffix = key.Substring(key.Length - 4);
        int maskedLength = key.Length - 8;

        return $"{prefix}{new string('•', maskedLength)}{suffix}";
    }

    private void ShowProviderHelp(SatieAPIKeyManager.Provider provider)
    {
        string url = provider switch
        {
            SatieAPIKeyManager.Provider.OpenAI => "https://platform.openai.com/api-keys",
            SatieAPIKeyManager.Provider.ElevenLabs => "https://elevenlabs.io/api",
            SatieAPIKeyManager.Provider.Anthropic => "https://console.anthropic.com/settings/keys",
            SatieAPIKeyManager.Provider.Google => "https://makersuite.google.com/app/apikey",
            SatieAPIKeyManager.Provider.Azure => "https://portal.azure.com",
            _ => ""
        };

        if (!string.IsNullOrEmpty(url))
        {
            if (EditorUtility.DisplayDialog(
                $"Get {provider} API Key",
                $"Would you like to open the {provider} console to get an API key?",
                "Open Browser", "Cancel"))
            {
                Application.OpenURL(url);
            }
        }
    }

    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
        }

        if (successStyle == null)
        {
            successStyle = new GUIStyle(EditorStyles.label);
            successStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
        }

        if (errorStyle == null)
        {
            errorStyle = new GUIStyle(EditorStyles.label);
            errorStyle.normal.textColor = new Color(0.8f, 0.2f, 0.2f);
        }
    }
}