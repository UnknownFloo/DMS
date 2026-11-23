// ############################
// Provider settings form loading, editing, and persistence.
// ############################
const DEFAULT_PROMPT = 'Create a detailed, useful summary of the document. Write several short paragraphs and use clear bullet points for the main facts. Include key people, dates, organizations, education or employment, qualifications, conclusions, and important numbers. Do not invent information. Use complete sentences and a professional tone.';

// Escape provider values before placing them into generated card markup.
function escapeHtml(value) {
    const element = document.createElement('span');
    element.textContent = value || '';
    return element.innerHTML;
}

// Load provider settings, build custom provider cards, and save edits.
document.addEventListener('DOMContentLoaded', async () => {
    const form = document.getElementById('genAiForm');
    const prompt = document.getElementById('prompt');
    const status = document.getElementById('settingsStatus');
    const providerGrid = document.getElementById('providerGrid');

    function getToggles() {
        return [...document.querySelectorAll('[data-provider-toggle]')];
    }

    function updateToggleExclusivity(changedToggle) {
        if (!changedToggle.checked) return;
        getToggles().forEach(toggle => {
            if (toggle !== changedToggle) toggle.checked = false;
        });
        status.innerHTML = '<div class="alert-success" style="position:static">Provider selected. Click “Save providers” to activate it.</div>';
    }

    getToggles().forEach(toggle => toggle.addEventListener('change', () => updateToggleExclusivity(toggle)));

    document.getElementById('resetPromptBtn').addEventListener('click', () => {
        prompt.value = DEFAULT_PROMPT;
    });

    function addCustomCard(provider = null) {
        const id = provider?.id || `custom-${crypto.randomUUID ? crypto.randomUUID() : Date.now()}`;
        const card = document.createElement('article');
        card.className = 'provider-card custom-provider-card';
        card.dataset.customProvider = id;
        card.innerHTML = `
            <div class="provider-card-header">
                <div class="provider-icon local-icon">＋</div>
                <div><h2>Custom provider</h2><p>OpenAI-compatible server</p></div>
                <label class="switch" title="Enable custom provider">
                    <input type="checkbox" data-provider-toggle="${escapeHtml(id)}">
                    <span class="switch-track"></span>
                </label>
            </div>
            <div class="form-group"><label>Provider name</label><input class="custom-name" value="${escapeHtml(provider?.name || 'My local model')}"></div>
            <div class="form-group"><label>Server URL</label><input class="custom-url" type="url" value="${escapeHtml(provider?.baseUrl || '')}" placeholder="http://host.docker.internal:11434/v1"></div>
            <div class="form-group"><label>Model</label><input class="custom-model" value="${escapeHtml(provider?.model || 'llama3.1')}"></div>
            <div class="form-group"><label>Optional API key</label><input class="custom-key" type="password" placeholder="Leave empty if not required"></div>
            <button class="btn btn-danger btn-small remove-provider-btn" type="button">Remove provider</button>`;

        card.querySelector('[data-provider-toggle]').checked = !!provider?.enabled;
        card.querySelector('[data-provider-toggle]').addEventListener('change', event => updateToggleExclusivity(event.target));
        card.querySelector('.remove-provider-btn').addEventListener('click', () => {
            card.remove();
            status.textContent = 'Provider removed from the form. Click “Save providers” to persist.';
        });
        providerGrid.appendChild(card);
    }

    document.getElementById('addProviderBtn').addEventListener('click', () => addCustomCard());

    try {
        const settings = await api.getGenAiSettings();
        const providers = settings.providers || [];
        const gemini = providers.find(provider => provider.id === 'gemini') || providers.find(provider => provider.provider === 'gemini');
        const local = providers.find(provider => provider.id === 'local') || providers.find(provider => provider.provider === 'local');

        document.getElementById('geminiEnabled').checked = !!gemini?.enabled;
        document.getElementById('geminiApiKey').value = gemini?.apiKey || '';
        document.getElementById('geminiModel').value = gemini?.model || 'gemini-2.5-flash';
        document.getElementById('geminiKeyStatus').textContent = gemini?.apiKey ? `Saved key: ${gemini.apiKey}` : 'No API key saved';

        document.getElementById('localEnabled').checked = !!local?.enabled;
        document.getElementById('localApiKey').value = local?.apiKey || '';
        document.getElementById('localBaseUrl').value = local?.baseUrl || '';
        document.getElementById('localModel').value = local?.model || 'llama3.1';
        document.getElementById('localKeyStatus').textContent = local?.apiKey ? 'Saved API key is configured' : 'Optional — no key configured';

        prompt.value = gemini?.prompt || local?.prompt || DEFAULT_PROMPT;

        providers
            .filter(provider => provider.id !== 'gemini' && provider.id !== 'local')
            .forEach(provider => addCustomCard(provider));

        getToggles().forEach(toggle => toggle.addEventListener('change', () => updateToggleExclusivity(toggle)));
    } catch (error) {
        prompt.value = DEFAULT_PROMPT;
        status.innerHTML = '<div class="alert-error" style="position:static">Could not load saved provider settings.</div>';
    }

    form.addEventListener('submit', async event => {
        event.preventDefault();
        status.textContent = 'Saving providers...';

        const geminiKey = document.getElementById('geminiApiKey').value.trim();
        const localKey = document.getElementById('localApiKey').value.trim();
        const promptText = prompt.value.trim() || DEFAULT_PROMPT;

        const providers = [
            {
                id: 'gemini', name: 'Google Gemini', provider: 'gemini',
                apiKey: geminiKey || '••••••••', model: document.getElementById('geminiModel').value.trim() || 'gemini-2.5-flash',
                baseUrl: document.getElementById('geminiBaseUrl').value.trim() || 'https://generativelanguage.googleapis.com', prompt: promptText, enabled: document.getElementById('geminiEnabled').checked
            },
            {
                id: 'local', name: 'Local / OpenAI-compatible', provider: 'local',
                apiKey: localKey || '••••••••', model: document.getElementById('localModel').value.trim() || 'llama3.1',
                baseUrl: document.getElementById('localBaseUrl').value.trim() || null, prompt: promptText,
                enabled: document.getElementById('localEnabled').checked
            }
        ];

        providerGrid.querySelectorAll('[data-custom-provider]').forEach(card => {
            const id = card.dataset.customProvider;
            providers.push({
                id,
                name: card.querySelector('.custom-name').value.trim() || 'Custom provider',
                provider: 'local',
                apiKey: card.querySelector('.custom-key').value.trim() || '••••••••',
                model: card.querySelector('.custom-model').value.trim() || 'llama3.1',
                baseUrl: card.querySelector('.custom-url').value.trim() || null,
                prompt: promptText,
                enabled: card.querySelector('[data-provider-toggle]').checked
            });
        });

        try {
            const saved = await api.updateGenAiSettings({ providers });
            status.innerHTML = '<div class="alert-success" style="position:static">Provider settings saved.</div>';
            if (geminiKey) document.getElementById('geminiKeyStatus').textContent = 'Saved API key is configured';
            if (localKey) document.getElementById('localKeyStatus').textContent = 'Saved API key is configured';
            document.getElementById('geminiApiKey').value = '';
            document.getElementById('localApiKey').value = '';
            providerGrid.querySelectorAll('.custom-key').forEach(input => input.value = '');
        } catch (error) {
            status.innerHTML = `<div class="alert-error" style="position:static">Could not save provider settings: ${escapeHtml(error.message)}</div>`;
        }
    });
});
