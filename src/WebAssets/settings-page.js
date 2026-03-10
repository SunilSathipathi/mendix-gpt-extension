// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Settings dialog logic — WebView bridge and form handling
// ============================================================================

(function () {
    'use strict';
    var keyState = { claude: false, openai: false };

    var MODEL_OPTIONS = {
        claude: [
            { value: 'claude-sonnet-4-5-20250929', label: 'Claude Sonnet 4.5 (Recommended)' },
            { value: 'claude-opus-4-6', label: 'Claude Opus 4.6 (Most Intelligent)' },
            { value: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5 (Fastest)' }
        ],
        openai: [
            { value: 'gpt-4o-mini', label: 'GPT-4o mini' }
        ]
    };

    function updateApiKeyUi(provider, hasClaudeKey, hasOpenAiKey) {
        var input = document.getElementById('apiKeyInput');
        var label = document.getElementById('apiKeyLabel');
        var hasKey = provider === 'openai' ? hasOpenAiKey : hasClaudeKey;
        label.textContent = provider === 'openai' ? 'OpenAI API Key' : 'Claude API Key';
        input.placeholder = provider === 'openai' ? 'sk-...' : 'sk-ant-api03-...';
        if (hasKey) input.placeholder += ' (key saved)';
    }

    function updateModelOptions(provider, selectedModel) {
        var select = document.getElementById('modelSelect');
        var options = MODEL_OPTIONS[provider] || MODEL_OPTIONS.claude;
        select.innerHTML = '';
        options.forEach(function (option) {
            var el = document.createElement('option');
            el.value = option.value;
            el.textContent = option.label;
            select.appendChild(el);
        });
        select.value = options.some(function (o) { return o.value === selectedModel; })
            ? selectedModel
            : options[0].value;
    }

    function sendToBackend(type, payload) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({ message: type, data: payload || {} });
        }
    }

    function handleMessage(event) {
        var envelope = event.data;
        if (!envelope || typeof envelope.message !== 'string') return;

        var type = envelope.message;
        var data = envelope.data;

        if (type === 'load_settings' && data) {
            var provider = data.provider || 'claude';
            keyState.claude = !!data.hasClaudeKey;
            keyState.openai = !!data.hasOpenAiKey;
            document.getElementById('providerSelect').value = provider;
            updateModelOptions(provider, data.selectedModel);
            updateApiKeyUi(provider, keyState.claude, keyState.openai);
            if (data.selectedModel) document.getElementById('modelSelect').value = data.selectedModel;
            if (data.contextDepth) document.getElementById('contextDepthSelect').value = data.contextDepth;
            if (data.maxTokens) document.getElementById('maxTokensInput').value = data.maxTokens;
            if (data.retryMaxAttempts != null) document.getElementById('retryMaxAttemptsInput').value = data.retryMaxAttempts;
            if (data.retryDelaySeconds != null) document.getElementById('retryDelaySecondsInput').value = data.retryDelaySeconds;
            if (data.maxToolRounds != null) document.getElementById('maxToolRoundsInput').value = data.maxToolRounds;
            if (data.promptCachingEnabled != null) document.getElementById('promptCachingCheckbox').checked = data.promptCachingEnabled;
            if (data.theme) {
                document.getElementById('themeSelect').value = data.theme;
                document.body.classList.toggle('dark', data.theme === 'dark');
                document.body.classList.toggle('settings-body', true);
            }
        }
        if (type === 'settings_saved') {
            // Dialog is closed by C# backend after saving
        }
    }

    document.getElementById('saveSettingsBtn').addEventListener('click', function () {
        sendToBackend('save_settings', {
            apiKey: document.getElementById('apiKeyInput').value,
            provider: document.getElementById('providerSelect').value,
            selectedModel: document.getElementById('modelSelect').value,
            contextDepth: document.getElementById('contextDepthSelect').value,
            maxTokens: parseInt(document.getElementById('maxTokensInput').value) || 8192,
            retryMaxAttempts: (function(v) { var n = parseInt(v); return isNaN(n) ? 20 : n; })(document.getElementById('retryMaxAttemptsInput').value),
            retryDelaySeconds: parseInt(document.getElementById('retryDelaySecondsInput').value) || 60,
            maxToolRounds: parseInt(document.getElementById('maxToolRoundsInput').value) || 10,
            promptCachingEnabled: document.getElementById('promptCachingCheckbox').checked,
            theme: document.getElementById('themeSelect').value
        });
    });

    document.getElementById('cancelSettingsBtn').addEventListener('click', function () {
        sendToBackend('cancel_settings', {});
    });

    document.getElementById('providerSelect').addEventListener('change', function () {
        updateModelOptions(this.value, null);
        updateApiKeyUi(this.value, keyState.claude, keyState.openai);
        document.getElementById('apiKeyInput').value = '';
    });

    // Per Mendix API docs: register message handler, then post MessageListenerRegistered
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', handleMessage);
        sendToBackend('MessageListenerRegistered');
    }

    // Request current settings
    sendToBackend('get_settings', {});
})();
