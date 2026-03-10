// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Configuration persistence — DPAPI-encrypted API key and app settings
// ============================================================================
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AideLite.Models;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.Services;

[SupportedOSPlatform("windows")]
public class ConfigurationService
{
    private readonly ILogService _logService;
    private readonly string _configFilePath;
    private AideLiteConfig _cachedConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ConfigurationService(ILogService logService, IExtensionFileService? extensionFileService)
    {
        _logService = logService;

        // APPDATA provides per-user persistence that survives project switches and reinstalls
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AideLite");
        Directory.CreateDirectory(appDataDir);
        _configFilePath = Path.Combine(appDataDir, "config.json");

        _cachedConfig = LoadFromDisk();
    }

    public AideLiteConfig GetConfig()
    {
        _cachedConfig = NormalizeConfig(LoadFromDisk());
        return _cachedConfig;
    }

    public string? GetApiKey(string? provider = null)
    {
        var config = GetConfig();
        provider ??= config.Provider;

        var encryptedKey = provider == "openai"
            ? config.EncryptedOpenAiApiKey
            : config.EncryptedClaudeApiKey ?? config.EncryptedApiKey;

        if (!string.IsNullOrEmpty(encryptedKey))
        {
            try
            {
                return DecryptApiKey(encryptedKey);
            }
            catch (Exception ex)
            {
                _logService.Error($"AIDE Lite: Failed to decrypt API key: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    public bool HasApiKey(string? provider = null)
    {
        var config = GetConfig();
        provider ??= config.Provider;
        return provider == "openai"
            ? !string.IsNullOrEmpty(config.EncryptedOpenAiApiKey)
            : !string.IsNullOrEmpty(config.EncryptedClaudeApiKey ?? config.EncryptedApiKey);
    }

    private static readonly HashSet<string> AllowedProviders = new(StringComparer.Ordinal)
    {
        "claude", "openai"
    };

    private static readonly HashSet<string> AllowedModels = new(StringComparer.Ordinal)
    {
        "claude-sonnet-4-5-20250929",
        "claude-sonnet-4-6",
        "claude-opus-4-6",
        "claude-haiku-4-5-20251001",
        "gpt-4o-mini"
    };

    private static readonly HashSet<string> AllowedContextDepths = new(StringComparer.Ordinal)
    {
        "full", "module", "summary", "none"
    };

    private const int MaxTokensCeiling = 64000;

    public void SaveConfig(string? apiKey, string? provider, string? selectedModel, string? contextDepth, int? maxTokens, string? theme = null, int? retryMaxAttempts = null, int? retryDelaySeconds = null, int? maxToolRounds = null, bool? promptCachingEnabled = null, bool? autoRefreshContext = null, bool? autoLoadLastConversation = null)
    {
        if (!string.IsNullOrEmpty(provider) && AllowedProviders.Contains(provider))
            _cachedConfig.Provider = provider;

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var encrypted = EncryptApiKey(apiKey);
            if (_cachedConfig.Provider == "openai")
            {
                _cachedConfig.EncryptedOpenAiApiKey = encrypted;
            }
            else
            {
                _cachedConfig.EncryptedClaudeApiKey = encrypted;
                _cachedConfig.EncryptedApiKey = encrypted;
            }
        }

        if (!string.IsNullOrEmpty(selectedModel) && AllowedModels.Contains(selectedModel))
            _cachedConfig.SelectedModel = selectedModel;

        if (_cachedConfig.Provider == "openai" && _cachedConfig.SelectedModel != "gpt-4o-mini")
            _cachedConfig.SelectedModel = "gpt-4o-mini";

        if (!string.IsNullOrEmpty(contextDepth) && AllowedContextDepths.Contains(contextDepth))
            _cachedConfig.ContextDepth = contextDepth;

        if (maxTokens.HasValue && maxTokens.Value >= 256)
            _cachedConfig.MaxTokens = Math.Min(maxTokens.Value, MaxTokensCeiling);

        if (theme is "light" or "dark")
            _cachedConfig.Theme = theme;

        if (retryMaxAttempts.HasValue && retryMaxAttempts.Value >= 0)
            _cachedConfig.RetryMaxAttempts = Math.Min(retryMaxAttempts.Value, 100);

        if (retryDelaySeconds.HasValue && retryDelaySeconds.Value >= 1)
            _cachedConfig.RetryDelaySeconds = Math.Min(retryDelaySeconds.Value, 600);

        if (maxToolRounds.HasValue && maxToolRounds.Value >= 1)
            _cachedConfig.MaxToolRounds = Math.Min(maxToolRounds.Value, 50);

        if (promptCachingEnabled.HasValue)
            _cachedConfig.PromptCachingEnabled = promptCachingEnabled.Value;

        if (autoRefreshContext.HasValue)
            _cachedConfig.AutoRefreshContext = autoRefreshContext.Value;

        if (autoLoadLastConversation.HasValue)
            _cachedConfig.AutoLoadLastConversation = autoLoadLastConversation.Value;

        SaveToDisk(_cachedConfig);
        _logService.Info("AIDE Lite: Configuration saved");
    }

    public void SaveConsent(bool accepted)
    {
        _cachedConfig.HasAcceptedDataConsent = accepted;
        SaveToDisk(_cachedConfig);
        _logService.Info($"AIDE Lite: Data consent {(accepted ? "accepted" : "revoked")}");
    }

    private AideLiteConfig LoadFromDisk()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                return NormalizeConfig(JsonSerializer.Deserialize<AideLiteConfig>(json) ?? new AideLiteConfig());
            }
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to load config: {ex.Message}");
        }
        return NormalizeConfig(new AideLiteConfig());
    }

    private void SaveToDisk(AideLiteConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            var tempPath = _configFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logService.Error($"AIDE Lite: Failed to save config: {ex.Message}");
        }
    }

    // DPAPI encryption with app-specific entropy for secure local storage
    // Entropy ensures keys encrypted by other DPAPI-using apps cannot be cross-decrypted
    private static readonly byte[] DpapiEntropy = "AideLite-MendixExtension-2026"u8.ToArray();

    private static string EncryptApiKey(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string DecryptApiKey(string encrypted)
    {
        var bytes = Convert.FromBase64String(encrypted);
        // Backward-compatible decryption: try entropy first, then without (pre-entropy versions)
        try
        {
            var decrypted = ProtectedData.Unprotect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            // Key was encrypted without entropy (from older version) - try without
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
    }

    private static AideLiteConfig NormalizeConfig(AideLiteConfig config)
    {
        if (!AllowedProviders.Contains(config.Provider))
            config.Provider = "claude";

        if (!AllowedModels.Contains(config.SelectedModel))
            config.SelectedModel = config.Provider == "openai" ? "gpt-4o-mini" : "claude-sonnet-4-5-20250929";

        if (config.Provider == "openai")
            config.SelectedModel = "gpt-4o-mini";

        return config;
    }
}
