// Document detail page
document.addEventListener('DOMContentLoaded', () => {
    const params = new URLSearchParams(window.location.search);
    const id = params.get('id');
    if (!id) {
        document.getElementById('documentDetail').innerHTML = '<div class="alert alert-error">No document ID</div>';
        return;
    }
    loadDocument(id);
});

function renderPreview(doc, fileUrl) {
    const lowerName = (doc.fileName || '').toLowerCase();
    const isPdf = doc.contentType === 'application/pdf'
        || lowerName.endsWith('.pdf')
        || (doc.contentType === 'application/octet-stream' && lowerName.endsWith('.pdf'));
    const isImage = !!(doc.contentType && doc.contentType.startsWith('image/'));

    if (isImage) {
        return `
            <div class="preview-frame image-preview">
                <img src="${fileUrl}" alt="${escape(doc.fileName)}" />
            </div>
        `;
    }

    if (isPdf) {
        // Use embedded PDF.js viewer for consistent in-app previews
        const viewerUrl = `/vendor/pdfjs/viewer.html?file=${encodeURIComponent(fileUrl)}`;
        return `
            <div class="preview-frame pdf-preview">
                <iframe
                    class="pdf-viewer"
                    src="${viewerUrl}"
                    title="${escape(doc.fileName)}"
                    loading="lazy"
                    referrerpolicy="no-referrer"
                ></iframe>
            </div>
        `;
    }

    return `
        <div class="empty-state">
            <h3>Preview unavailable</h3>
            <p>This file type cannot be previewed inside the browser.</p>
            <a class="file-picker-button" href="${fileUrl}" target="_blank" rel="noopener noreferrer">Open file</a>
        </div>
    `;
}

async function loadDocument(id) {
    const container = document.getElementById('documentDetail');
    try {
        const doc = await api.getDocument(id);
        if (!doc) {
            container.innerHTML = '<div class="alert alert-error">Document not found</div>';
            return;
        }

        const fileUrl = api.getDocumentFileUrl(doc.id);
        const collections = await api.getCollections();

        container.innerHTML = `
            <a href="documents.html" class="btn btn-secondary" style="margin-bottom: 1.5rem;">← Back</a>
            
            <div class="detail">
                <div class="detail-header">
                    <h1>${escape(doc.fileName)}</h1>
                    <div class="detail-meta">
                        <div class="detail-meta-item">
                            <div class="detail-meta-label">Uploaded</div>
                            <div class="detail-meta-value">${new Date(doc.uploadedAtUtc).toLocaleDateString()}</div>
                        </div>
                        <div class="detail-meta-item">
                            <div class="detail-meta-label">Type</div>
                            <div class="detail-meta-value">${escape(doc.contentType)}</div>
                        </div>
                    </div>
                </div>

                ${doc.description ? `
                    <h3>Description</h3>
                    <p>${escape(doc.description)}</p>
                ` : ''}

                <div style="margin-top: 1.5rem;">
                    <h3>Collections</h3>
                    <div style="display:flex; flex-wrap:wrap; gap:0.75rem; margin-top:0.75rem;">
                        ${(collections || []).length === 0 ? '<span class="tag">No collections yet</span>' : (collections || []).map(col => `
                            <button class="collection-chip" data-collection-id="${col.id}" type="button" onclick="addToCollection('${col.id}', '${id}')">+ ${escape(col.name)}</button>
                        `).join('')}
                    </div>
                </div>

                <div class="pdf-section" style="margin-top: 2rem;">
                    ${renderPreview(doc, fileUrl)}
                </div>

                <div style="margin-top: 2rem; display: flex; gap: 1rem; flex-wrap: wrap;">
                    <button onclick="deleteDoc('${id}')" class="btn btn-danger">Delete</button>
                    <a href="upload.html" class="btn btn-primary">Upload Another</a>
                    <a href="${fileUrl}" class="btn btn-secondary" target="_blank" rel="noopener noreferrer">Open preview</a>
                </div>
            </div>
        `;
    } catch (err) {
        container.innerHTML = '<div class="alert alert-error">Failed to load document</div>';
    }
}

async function addToCollection(collectionId, documentId) {
    const button = document.querySelector(`button[data-collection-id="${collectionId}"]`);
    if (!button) return;

    button.disabled = true;
    const previousText = button.dataset.originalText || button.textContent;
    button.dataset.originalText = previousText;
    button.textContent = 'Adding...';

    try {
        await api.addDocumentToCollection(collectionId, documentId);
        button.textContent = 'Added ✓';
        button.classList.add('success-state');
        button.setAttribute('aria-pressed', 'true');
        setTimeout(() => {
            button.textContent = previousText;
            button.disabled = false;
        }, 1800);
    } catch (err) {
        button.textContent = 'Try again';
        button.disabled = false;
        alert('Could not add document to collection');
    }
}

async function deleteDoc(id) {
    // Open modal-driven delete flow for accessibility and nicer UI
    openDeleteDocModal(id);
}

// Modal-driven delete flow for documents
function openDeleteDocModal(id) {
    const modal = document.getElementById('deleteDocModal');
    const nameEl = document.getElementById('deleteDocName');
    // try to read current document name from DOM if present
    const titleEl = document.querySelector('.detail-header h1');
    const displayName = titleEl ? titleEl.textContent : '';
    nameEl.textContent = displayName;
    modal.dataset.id = id;
    modal.classList.add('visible');
    modal.setAttribute('aria-hidden', 'false');
}

function closeDeleteDocModal() {
    const modal = document.getElementById('deleteDocModal');
    modal.classList.remove('visible');
    modal.setAttribute('aria-hidden', 'true');
    delete modal.dataset.id;
}

async function confirmDeleteDoc() {
    const modal = document.getElementById('deleteDocModal');
    const id = modal.dataset.id;
    if (!id) return closeDeleteDocModal();

    const btn = document.getElementById('confirmDeleteDocBtn');
    btn.disabled = true;
    const originalText = btn.textContent;
    btn.textContent = 'Deleting...';

    try {
        await api.deleteDocument(id);
        closeDeleteDocModal();
        window.location = 'documents.html';
    } catch (err) {
        console.error('Delete failed:', err);
        alert('Delete failed: ' + (err.message || 'unknown error'));
        btn.disabled = false;
        btn.textContent = originalText;
    }
}
