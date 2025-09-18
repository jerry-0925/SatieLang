using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Satie
{
    /// <summary>
    /// Centralized API key management system for all Satie services
    /// Supports multiple providers, secure storage, and environment variables
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

        [System.Serializable]
        public class APIKeyConfig
        {
            public Provider provider;
            public string key;
            public string endpoint;
            public bool isValid;
            public DateTime lastValidated;
        }

        [System.Serializable]
        private class APIKeyStorage
        {
            public List<APIKeyConfig> keys = new List<APIKeyConfig>();
            public bool encrypted;
            public string encryptionCheck;
        }

        private static Dictionary<Provider, APIKeyConfig> _keyCache = new Dictionary<Provider, APIKeyConfig>();
        private static bool _initialized = false;
        private static string _configPath;
        private static byte[] _entropy;

        private const string CONFIG_FILENAME = "satie_api_keys.json";
        private const string ENV_PREFIX = "SATIE_API_KEY_";
        private const string ENCRYPTION_CHECK = "SATIE_ENCRYPTED_V1";

        static SatieAPIKeyManager()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            // Set config path - in persistent data path for security
            _configPath = Path.Combine(Application.persistentDataPath, CONFIG_FILENAME);

            // Generate machine-specific entropy for encryption
            string machineId = SystemInfo.deviceUniqueIdentifier;
            _entropy = Encoding.UTF8.GetBytes(machineId);

            // Load existing keys
            LoadKeys();

            _initialized = true;
        }

        #region Public API

        /// <summary>
        /// Get API key for a specific provider
        /// </summary>
        public static string GetKey(Provider provider)
        {
            Initialize();

            // 1. Check environment variable first (highest priority)
            string envKey = GetEnvironmentKey(provider);
            if (!string.IsNullOrEmpty(envKey))
            {
                Debug.Log($"[APIKeys] Using environment variable for {provider}");
                return envKey;
            }

            // 2. Check cached keys
            if (_keyCache.TryGetValue(provider, out var config) && config.isValid)
            {
                return config.key;
            }

            // 3. Check legacy file locations for backward compatibility
            string legacyKey = GetLegacyKey(provider);
            if (!string.IsNullOrEmpty(legacyKey))
            {
                Debug.Log($"[APIKeys] Migrating legacy key for {provider}");
                SetKey(provider, legacyKey);
                return legacyKey;
            }

            Debug.LogWarning($"[APIKeys] No valid API key found for {provider}");
            return null;
        }

        /// <summary>
        /// Set API key for a provider
        /// </summary>
        public static void SetKey(Provider provider, string key, string endpoint = null)
        {
            Initialize();

            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogError($"[APIKeys] Cannot set empty key for {provider}");
                return;
            }

            var config = new APIKeyConfig
            {
                provider = provider,
                key = key,
                endpoint = endpoint ?? GetDefaultEndpoint(provider),
                isValid = true,
                lastValidated = DateTime.Now
            };

            _keyCache[provider] = config;
            SaveKeys();

            Debug.Log($"[APIKeys] Key set for {provider}");
        }

        /// <summary>
        /// Remove API key for a provider
        /// </summary>
        public static void RemoveKey(Provider provider)
        {
            Initialize();

            if (_keyCache.ContainsKey(provider))
            {
                _keyCache.Remove(provider);
                SaveKeys();
                Debug.Log($"[APIKeys] Key removed for {provider}");
            }
        }

        /// <summary>
        /// Check if a provider has a valid key
        /// </summary>
        public static bool HasKey(Provider provider)
        {
            Initialize();
            return !string.IsNullOrEmpty(GetKey(provider));
        }

        /// <summary>
        /// Get all configured providers
        /// </summary>
        public static List<Provider> GetConfiguredProviders()
        {
            Initialize();
            return _keyCache.Keys.ToList();
        }

        /// <summary>
        /// Get endpoint for a provider
        /// </summary>
        public static string GetEndpoint(Provider provider)
        {
            Initialize();

            if (_keyCache.TryGetValue(provider, out var config))
            {
                return config.endpoint;
            }

            return GetDefaultEndpoint(provider);
        }

        /// <summary>
        /// Validate all keys (async in real implementation)
        /// </summary>
        public static void ValidateAllKeys()
        {
            Initialize();

            foreach (var kvp in _keyCache.ToList())
            {
                // In production, this would make API calls to validate
                // For now, just check format
                bool isValid = ValidateKeyFormat(kvp.Key, kvp.Value.key);

                var config = kvp.Value;
                config.isValid = isValid;
                config.lastValidated = DateTime.Now;
                _keyCache[kvp.Key] = config;
            }

            SaveKeys();
        }

        #endregion

        #region Key Storage

        private static void SaveKeys()
        {
            try
            {
                var storage = new APIKeyStorage
                {
                    keys = _keyCache.Values.ToList(),
                    encrypted = true,
                    encryptionCheck = ENCRYPTION_CHECK
                };

                // Encrypt sensitive data
                foreach (var key in storage.keys)
                {
                    key.key = EncryptString(key.key);
                }

                string json = JsonUtility.ToJson(storage, true);
                File.WriteAllText(_configPath, json);

                Debug.Log($"[APIKeys] Saved {storage.keys.Count} keys to {_configPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[APIKeys] Failed to save keys: {e.Message}");
            }
        }

        private static void LoadKeys()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Debug.Log($"[APIKeys] No config file found at {_configPath}");
                    return;
                }

                string json = File.ReadAllText(_configPath);
                var storage = JsonUtility.FromJson<APIKeyStorage>(json);

                if (storage == null || storage.keys == null)
                {
                    Debug.LogWarning("[APIKeys] Invalid config file");
                    return;
                }

                // Decrypt keys
                if (storage.encrypted && storage.encryptionCheck == ENCRYPTION_CHECK)
                {
                    foreach (var key in storage.keys)
                    {
                        try
                        {
                            key.key = DecryptString(key.key);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[APIKeys] Failed to decrypt key for {key.provider}: {e.Message}");
                            key.isValid = false;
                        }
                    }
                }

                // Populate cache
                _keyCache.Clear();
                foreach (var key in storage.keys)
                {
                    _keyCache[key.provider] = key;
                }

                Debug.Log($"[APIKeys] Loaded {storage.keys.Count} keys");
            }
            catch (Exception e)
            {
                Debug.LogError($"[APIKeys] Failed to load keys: {e.Message}");
            }
        }

        #endregion

        #region Encryption

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            try
            {
                // Use AES encryption for cross-platform compatibility
                using (Aes aes = Aes.Create())
                {
                    // Derive key from machine ID and entropy
                    var key = DeriveKey(_entropy);
                    aes.Key = key;
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor())
                    using (var msEncrypt = new MemoryStream())
                    {
                        // Prepend IV to the encrypted data
                        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                        using (var swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }

                        return Convert.ToBase64String(msEncrypt.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[APIKeys] Encryption failed, using base64: {e.Message}");
                // Fallback to base64 if encryption fails
                return "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
            }
        }

        private static string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            try
            {
                // Check if it's base64 fallback
                if (cipherText.StartsWith("B64:"))
                {
                    byte[] bytes = Convert.FromBase64String(cipherText.Substring(4));
                    return Encoding.UTF8.GetString(bytes);
                }

                // Decrypt AES encrypted data
                byte[] cipherBytes = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    var key = DeriveKey(_entropy);
                    aes.Key = key;

                    // Extract IV from the beginning of cipher text
                    byte[] iv = new byte[aes.BlockSize / 8];
                    Array.Copy(cipherBytes, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var msDecrypt = new MemoryStream(cipherBytes, iv.Length, cipherBytes.Length - iv.Length))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[APIKeys] Decryption failed: {e.Message}");

                // Try legacy base64 fallback
                try
                {
                    byte[] bytes = Convert.FromBase64String(cipherText);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    Debug.LogError($"[APIKeys] All decryption methods failed for key");
                    return cipherText; // Return as-is if all decryption fails
                }
            }
        }

        private static byte[] DeriveKey(byte[] entropy)
        {
            // Use PBKDF2 to derive a 256-bit key from entropy
            using (var pbkdf2 = new Rfc2898DeriveBytes(entropy, entropy, 10000))
            {
                return pbkdf2.GetBytes(32); // 256 bits
            }
        }

        #endregion

        #region Helper Methods

        private static string GetEnvironmentKey(Provider provider)
        {
            string envVar = $"{ENV_PREFIX}{provider.ToString().ToUpper()}";
            return Environment.GetEnvironmentVariable(envVar);
        }

        private static string GetLegacyKey(Provider provider)
        {
            // Check old file locations for backward compatibility
            string[] possiblePaths = provider switch
            {
                Provider.OpenAI => new[] { "api_key.txt", "openai_key.txt", ".env" },
                Provider.ElevenLabs => new[] { "elevenlabs_key.txt", "eleven_labs_key.txt" },
                _ => new string[0]
            };

            foreach (string filename in possiblePaths)
            {
                string path = Path.Combine(Application.dataPath, filename);
                if (File.Exists(path))
                {
                    try
                    {
                        string key = File.ReadAllText(path).Trim();

                        // For .env files, parse the content
                        if (filename == ".env")
                        {
                            var lines = key.Split('\n');
                            foreach (var line in lines)
                            {
                                if (line.StartsWith($"{provider.ToString().ToUpper()}_API_KEY="))
                                {
                                    return line.Split('=')[1].Trim();
                                }
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(key))
                        {
                            return key;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[APIKeys] Failed to read legacy key from {path}: {e.Message}");
                    }
                }
            }

            return null;
        }

        private static string GetDefaultEndpoint(Provider provider)
        {
            return provider switch
            {
                Provider.OpenAI => "https://api.openai.com/v1",
                Provider.ElevenLabs => "https://api.elevenlabs.io/v1",
                Provider.Anthropic => "https://api.anthropic.com/v1",
                Provider.Google => "https://generativelanguage.googleapis.com/v1",
                Provider.Azure => "", // User must provide
                _ => ""
            };
        }

        private static bool ValidateKeyFormat(Provider provider, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            return provider switch
            {
                Provider.OpenAI => key.StartsWith("sk-") && key.Length > 20,
                Provider.ElevenLabs => key.Length == 32, // ElevenLabs uses 32-char keys
                Provider.Anthropic => key.StartsWith("sk-ant-"),
                Provider.Google => key.Length > 30, // Google API keys are typically 39 chars
                _ => key.Length > 10 // Basic validation for unknown providers
            };
        }

        #endregion

        #region Migration Helper

        /// <summary>
        /// Migrate all legacy API keys to the new system
        /// </summary>
        public static void MigrateLegacyKeys()
        {
            int migrated = 0;

            // Try to migrate each provider
            foreach (Provider provider in Enum.GetValues(typeof(Provider)))
            {
                if (provider == Provider.Custom) continue;

                string legacyKey = GetLegacyKey(provider);
                if (!string.IsNullOrEmpty(legacyKey) && !HasKey(provider))
                {
                    SetKey(provider, legacyKey);
                    migrated++;
                    Debug.Log($"[APIKeys] Migrated legacy key for {provider}");
                }
            }

            if (migrated > 0)
            {
                Debug.Log($"[APIKeys] Successfully migrated {migrated} legacy keys");
            }
        }

        #endregion
    }
}