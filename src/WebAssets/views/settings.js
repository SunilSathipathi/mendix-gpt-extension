// ============================================================================
// AIDE Lite - Settings, Theme & Consent
// Settings form, theme toggling, consent/privacy modals.
// ============================================================================
(function (AIDE) {
    'use strict';

    var state = AIDE.state;
    var keyState = { claude: false, openai: false };
    var MODEL_OPTIONS = {
        claude: [
            { value: 'claude-sonnet-4-5-20250929', label: 'Claude Sonnet 4.5' },
            { value: 'claude-sonnet-4-6', label: 'Claude Sonnet 4.6 (Recommended)' },
            { value: 'claude-opus-4-6', label: 'Claude Opus 4.6 (Most Intelligent)' },
            { value: 'claude-haiku-4-5-20251001', label: 'Claude Haiku 4.5 (Fastest)' }
        ],
        openai: [
            { value: 'gpt-4o-mini', label: 'GPT-4o mini' }
        ]
    };

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

    function updateApiKeyUi(provider, hasClaudeKey, hasOpenAiKey) {
        var input = document.getElementById('apiKeyInput');
        var label = document.getElementById('apiKeyLabel');
        var hasKey = provider === 'openai' ? hasOpenAiKey : hasClaudeKey;
        label.textContent = provider === 'openai' ? 'OpenAI API Key' : 'Claude API Key';
        input.placeholder = provider === 'openai' ? 'sk-...' : 'sk-ant-api03-...';
        if (hasKey) input.placeholder += ' (key saved)';
    }

    // --- Settings ---
    AIDE.openSettings = function () {
        AIDE.dom.settingsModal.classList.remove('hidden');
        AIDE.sendToBackend('get_settings');
    };

    AIDE.closeSettings = function () {
        AIDE.dom.settingsModal.classList.add('hidden');
    };

    AIDE.handleLoadSettings = function (data) {
        if (!data) return;
        var provider = data.provider || 'claude';
        keyState.claude = !!data.hasClaudeKey;
        keyState.openai = !!data.hasOpenAiKey;
        document.getElementById('providerSelect').value = provider;
        updateModelOptions(provider, data.selectedModel);
        updateApiKeyUi(provider, keyState.claude, keyState.openai);
        AIDE.updateModelBadge(document.getElementById('modelSelect').value);
        if (data.contextDepth) document.getElementById('contextDepthSelect').value = data.contextDepth;
        if (data.maxTokens) document.getElementById('maxTokensInput').value = data.maxTokens;
        if (data.retryMaxAttempts != null) document.getElementById('retryMaxAttemptsInput').value = data.retryMaxAttempts;
        if (data.retryDelaySeconds != null) document.getElementById('retryDelaySecondsInput').value = data.retryDelaySeconds;
        if (data.maxToolRounds != null) document.getElementById('maxToolRoundsInput').value = data.maxToolRounds;
        if (data.promptCachingEnabled != null) document.getElementById('promptCachingCheckbox').checked = data.promptCachingEnabled;
        if (data.autoRefreshContext != null) document.getElementById('autoRefreshContextCheckbox').checked = data.autoRefreshContext;
        if (data.autoLoadLastConversation != null) document.getElementById('autoLoadLastConversationCheckbox').checked = data.autoLoadLastConversation;
        if (data.theme) AIDE.applyTheme(data.theme);

        if (!state.get('initialSettingsLoaded')) {
            state.set('initialSettingsLoaded', true);
            if (data.autoRefreshContext !== false) {
                AIDE.dom.contextDot.className = 'status-dot loading';
                AIDE.dom.contextText.textContent = 'Loading context...';
                AIDE.sendToBackend('get_context');
            }
            if (data.autoLoadLastConversation !== false && !state.get('skipAutoLoadConversation')) {
                state.set('autoLoadPending', true);
                AIDE.sendToBackend('get_history');
            }
        }
    };

    AIDE.handleSettingsSaved = function () {
        document.getElementById('apiKeyInput').value = '';
        AIDE.closeSettings();
    };

    AIDE.saveSettings = function () {
        var apiKey = document.getElementById('apiKeyInput').value;
        var provider = document.getElementById('providerSelect').value;
        var model = document.getElementById('modelSelect').value;
        var depth = document.getElementById('contextDepthSelect').value;
        var tokens = parseInt(document.getElementById('maxTokensInput').value) || 8192;
        var theme = AIDE.dom.themeSelect ? AIDE.dom.themeSelect.value : state.get('currentTheme');

        var retryMaxAttemptsVal = parseInt(document.getElementById('retryMaxAttemptsInput').value);
        var retryMaxAttempts = isNaN(retryMaxAttemptsVal) ? 20 : retryMaxAttemptsVal;
        var retryDelaySeconds = parseInt(document.getElementById('retryDelaySecondsInput').value) || 60;
        var maxToolRounds = parseInt(document.getElementById('maxToolRoundsInput').value) || 10;
        var promptCachingEnabled = document.getElementById('promptCachingCheckbox').checked;
        var autoRefreshContext = document.getElementById('autoRefreshContextCheckbox').checked;
        var autoLoadLastConversation = document.getElementById('autoLoadLastConversationCheckbox').checked;

        AIDE.sendToBackend('save_settings', {
            apiKey: apiKey,
            provider: provider,
            selectedModel: model,
            contextDepth: depth,
            maxTokens: tokens,
            retryMaxAttempts: retryMaxAttempts,
            retryDelaySeconds: retryDelaySeconds,
            maxToolRounds: maxToolRounds,
            promptCachingEnabled: promptCachingEnabled,
            autoRefreshContext: autoRefreshContext,
            autoLoadLastConversation: autoLoadLastConversation,
            theme: theme
        });

        AIDE.updateModelBadge(model);
        AIDE.applyTheme(theme);
    };

    AIDE.updateModelBadge = function (model) {
        var labels = {
            'claude-sonnet-4-5-20250929': 'Sonnet 4.5',
            'claude-sonnet-4-6': 'Sonnet 4.6',
            'claude-opus-4-6': 'Opus 4.6',
            'claude-haiku-4-5-20251001': 'Haiku 4.5',
            'gpt-4o-mini': 'GPT-4o mini'
        };
        AIDE.dom.modelBadge.textContent = labels[model] || model;
    };

    document.getElementById('providerSelect').addEventListener('change', function () {
        updateModelOptions(this.value, null);
        updateApiKeyUi(this.value, keyState.claude, keyState.openai);
        document.getElementById('apiKeyInput').value = '';
        AIDE.updateModelBadge(document.getElementById('modelSelect').value);
    });

    // --- Theme ---
    AIDE.applyTheme = function (theme) {
        var current = theme === 'dark' ? 'dark' : 'light';
        state.set('currentTheme', current);
        document.body.classList.toggle('dark', current === 'dark');
        if (AIDE.dom.themeToggleBtn) {
            AIDE.dom.themeToggleBtn.innerHTML = current === 'dark' ? '&#x2600;' : '&#x1F319;';
            AIDE.dom.themeToggleBtn.title = current === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode';
        }
        if (AIDE.dom.themeSelect) {
            AIDE.dom.themeSelect.value = current;
        }
    };

    // --- Consent & Privacy ---
    AIDE.handleConsentRequired = function () {
        state.set('isStreaming', false);
        AIDE.dom.sendBtn.disabled = false;
        AIDE.dom.sendBtn.classList.remove('hidden');
        AIDE.dom.stopBtn.classList.add('hidden');
        AIDE.hideProcessingBar();
        if (AIDE.dom.consentModal) AIDE.dom.consentModal.classList.remove('hidden');
    };

    AIDE.handleConsentSaved = function () {
        if (AIDE.dom.consentModal) AIDE.dom.consentModal.classList.add('hidden');
    };

    AIDE.openPrivacy = function () {
        if (AIDE.dom.privacyModal) AIDE.dom.privacyModal.classList.remove('hidden');
    };

    AIDE.closePrivacy = function () {
        if (AIDE.dom.privacyModal) AIDE.dom.privacyModal.classList.add('hidden');
    };

})(window.AIDE = window.AIDE || {});
