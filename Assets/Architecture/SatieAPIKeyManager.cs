using System;
using System.Collections.Generic;
using UnityEngine;

namespace Satie
{
    /// <summary>
    /// Centralized API key management system for all Satie services
    /// Now uses simple APIKeys.cs file - NO ENCRYPTION BULLSHIT
    /// </summary>
    public static class SatieAPIKeyManager
    {
        public enum Provider
        {
            OpenAI,
            ElevenLabs,
            Anthropic,
            Google,
            Azure,
            Custom
        }

        private const string ENV_PREFIX = "SATIE_API_KEY_";

        #region Public API

        /// <summary>
        /// Get API key for a specific provider
        /// Priority: 1) Environment variable, 2) APIKeys.cs file
        /// </summary>
        public static string GetKey(Provider provider)
        {
            // 1. Check environment variable first (highest priority)
            string envKey = GetEnvironmentKey(provider);
            if (!string.IsNullOrEmpty(envKey))
            {
                return envKey.Trim();
            }

            // 2. Check APIKeys.cs file
            string key = provider switch
            {
                Provider.Anthropic => APIKeys.ANTHROPIC,
                Provider.OpenAI => APIKeys.OPENAI,
                Provider.ElevenLabs => APIKeys.ELEVENLABS,
                Provider.Google => APIKeys.GOOGLE,
                _ => null
            };

            if (!string.IsNullOrEmpty(key) && !key.Contains("your-key-here"))
            {
                return key.Trim();
            }

            Debug.LogWarning($"[APIKeys] No valid API key found for {provider}. Please add your key to Assets/APIKeys.cs");
            return null;
        }


        /// <summary>
        /// Check if a provider has a valid key
        /// </summary>
        public static bool HasKey(Provider provider)
        {
            return !string.IsNullOrEmpty(GetKey(provider));
        }

        #endregion

        #region Helper Methods

        private static string GetEnvironmentKey(Provider provider)
        {
            string envVar = $"{ENV_PREFIX}{provider.ToString().ToUpper()}";
            return Environment.GetEnvironmentVariable(envVar);
        }

        #endregion
    }
}