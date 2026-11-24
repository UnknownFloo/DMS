// ############################
// Shared HTTP client for document, collection, editor, and GenAI endpoints.
// ############################
class PaperlessApi {
    // Use relative API path so the frontend works both on-host and when served
    // behind the containerized nginx proxy. Override by passing a full URL.
    constructor(baseUrl = '/api') {
        this.baseUrl = baseUrl;
    }

    // Send JSON requests and normalize API errors for the page-level handlers.
    async request(method, endpoint, data = null) {
        const url = `${this.baseUrl}${endpoint}`;
        const options = {
            method,
            headers: { 'Content-Type': 'application/json' }
        };

        if (data) options.body = JSON.stringify(data);

        const response = await fetch(url, options);
        if (!response.ok) {
            let detail = '';
            try {
                const payload = await response.text();
                detail = payload ? `: ${payload.replace(/[{}"]+/g, '').slice(0, 200)}` : '';
            } catch {}
            throw new Error(`HTTP ${response.status}${detail}`);
        }
        if (response.status === 204) return null;
        return response.json();
    }

    // Upload multipart file data, which cannot use the JSON request helper.
    async uploadFile(file, description = '') {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('description', description);

        const response = await fetch(`${this.baseUrl}/documents/upload`, {
            method: 'POST',
            body: formData
        });

        if (!response.ok) throw new Error(`Upload failed: ${response.status}`);
        return response.json();
   }

    // Document and collection endpoints.
    getDocuments() {
        return this.request('GET', '/documents');
    }

    searchDocuments(query) {
        return this.request('GET', `/documents/search?q=${encodeURIComponent(query)}`);
    }

    getDocument(id) {
        return this.request('GET', `/documents/${id}`);
    }

    getDocumentFileUrl(id) {
        return `${this.baseUrl}/documents/${id}/file`;
    }

    deleteDocument(id) {
        return this.request('DELETE', `/documents/${id}`);
    }

    getCollections() {
        return this.request('GET', '/collections');
    }

    addDocumentToCollection(collectionId, documentId) {
        return this.request('POST', `/collections/${collectionId}/documents`, {
            documentId
        });
    }

    // Provider settings, diagnostics, and summary generation.
    getGenAISettings() {
        return this.request('GET', '/genai/settings');
    }

    updateGenAISettings(settings) {
        return this.request('PUT', '/genai/settings', settings);
    }

    resetGenAISettings() {
        return this.request('DELETE', '/genai/settings');
    }

    getGenAiSettings() {
        return this.getGenAISettings();
    }

    updateGenAiSettings(settings) {
        return this.updateGenAISettings(settings);
    }

    getGenAiDiagnostics() {
        return this.request('GET', '/genai/diagnostics');
    }

    regenerateSummary(id, prompt) {
        return this.request('POST', `/documents/${id}/summary/regenerate`, { prompt });
    }

    // Document metadata and OCR updates.
    updateDocument(id, fileName, description) {
        return this.request('PUT', `/documents/${id}`, { fileName, description });
    }

    updateOcrText(id, text) {
        return this.request('PUT', `/documents/${id}/ocr-text`, { text });
    }

    // File-editing endpoints; PDF upload uses multipart data.
    editPdf(id, text, fileName) {
        return this.request('POST', `/documents/${id}/edit/pdf`, { text, fileName });
    }

    editImage(id, overlays, fileName) {
        return this.request('POST', `/documents/${id}/edit/image`, { overlays, fileName });
    }

    uploadEditedPdf(id, pdfBlob, fileName) {
        const formData = new FormData();
        formData.append('file', pdfBlob, fileName);
        formData.append('fileName', fileName);
        return fetch(`${this.baseUrl}/documents/${id}/edit/pdf/upload`, { method: 'POST', body: formData })
            .then(async response => {
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                return response.json();
            });
    }
}

// Shared client instance used by the page scripts.
const api = new PaperlessApi();

// Escape dynamic text before inserting it into page-generated HTML.
function escape(value) {
    const element = document.createElement('span');
    element.textContent = value == null ? '' : String(value);
    return element.innerHTML;
}
