// API Client
class PaperlessApi {
    // Use relative API path so the frontend works both on-host and when served
    // behind the containerized nginx proxy. Override by passing a full URL.
    constructor(baseUrl = '/api') {
        this.baseUrl = baseUrl;
    }

    async request(method, endpoint, data = null) {
        const url = `${this.baseUrl}${endpoint}`;
        const options = {
            method,
            headers: { 'Content-Type': 'application/json' }
        };

        if (data) options.body = JSON.stringify(data);

        const response = await fetch(url, options);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        if (response.status === 204) return null;
        return response.json();
    }

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

    getDocuments() {
        return this.request('GET', '/documents');
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
}

const api = new PaperlessApi();
