// ############################
// Load document details, file preview, collection links, and saved summary.
// ############################
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

        currentDocument = doc;

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

                <div class="description-panel">
                    <div class="summary-heading-row"><h3>Description</h3><button class="btn btn-secondary btn-small" type="button" onclick="openDescriptionModal()">Edit description</button></div>
                    <p id="documentDescription">${doc.description ? escape(doc.description) : '<span class="muted">No description added.</span>'}</p>
                </div>

                <div style="margin-top: 1.5rem;">
                    <h3>Collections</h3>
                    <div style="display:flex; flex-wrap:wrap; gap:0.75rem; margin-top:0.75rem;">
                        ${(collections || []).length === 0 ? '<span class="tag">No collections yet</span>' : (collections || []).map(col => `
                            <button class="collection-chip" data-collection-id="${col.id}" type="button" onclick="addToCollection('${col.id}', '${id}')">+ ${escape(col.name)}</button>
                        `).join('')}
                    </div>
                </div>

                <div class="document-carousel" id="documentCarousel">
                    <button class="carousel-arrow carousel-arrow-left" type="button" onclick="moveDocumentCarousel(-1)" aria-label="Previous view" hidden>
                        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m15 18-6-6 6-6" /></svg>
                    </button>
                    <div class="carousel-track">
                        <section class="carousel-slide preview-slide">
                            ${renderPreview(doc, fileUrl)}
                        </section>
                        <section class="carousel-slide summary-slide">
                            <div class="ai-summary">
                                <div class="summary-heading-row">
                                    <h3>AI Generated Summary</h3>
                                    <button class="btn btn-secondary btn-small" type="button" onclick="openSummaryModal()">${doc.summary ? 'Generate new summary' : 'Choose prompt'}</button>
                                </div>
                                <div class="summary-content" id="summaryContent">
                                    ${doc.summary
                                        ? formatSummary(doc.summary)
                                        : '<span class="summary-loader"><span class="summary-spinner"></span> Summary is being generated in the background…</span>'}
                                </div>
                            </div>
                        </section>
                    </div>
                    <button class="carousel-arrow carousel-arrow-right" type="button" onclick="moveDocumentCarousel(1)" aria-label="Next view">
                        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m9 18 6-6-6-6" /></svg>
                    </button>
                </div>

                <div style="margin-top: 2rem; display: flex; gap: 1rem; flex-wrap: wrap;">
                    <button onclick="deleteDoc('${id}')" class="btn btn-danger">Delete</button>
                    <a href="upload.html" class="btn btn-primary">Upload Another</a>
                    <a href="${fileUrl}" class="btn btn-secondary" target="_blank" rel="noopener noreferrer">Open preview</a>
                    <button onclick="openFileEditModal()" class="btn btn-primary">Edit as new file</button>
                </div>
            </div>
        `;
        initializeDocumentCarousel();

        if (typeof api.getGenAiDiagnostics === 'function') {
            api.getGenAiDiagnostics().then(entries => {
                const relevant = (entries || []).filter(entry => entry.documentId === id);
                if (relevant.length) {
                    console.group('[Paperless] AI summary diagnostics');
                    relevant.forEach(entry => console.log(entry));
                    console.groupEnd();
                }
            }).catch(() => {});
        }

        if (!doc.summary) {
            let attempts = 0;
            const pollSummary = window.setInterval(async () => {
                attempts += 1;
                try {
                    if (typeof api.getGenAiDiagnostics === 'function') {
                        const entries = await api.getGenAiDiagnostics();
                        const relevant = (entries || []).filter(entry => entry.documentId === id);
                        if (relevant.length) console.log('[Paperless] AI request/response:', relevant[0]);
                    }

                    const refreshed = await api.getDocument(id);
                    if (refreshed?.summary) {
                        const summaryContent = document.getElementById('summaryContent');
                        if (summaryContent) summaryContent.innerHTML = formatSummary(refreshed.summary);
                        window.clearInterval(pollSummary);
                    } else if (attempts >= 40) {
                        const summaryContent = document.getElementById('summaryContent');
                        if (summaryContent) summaryContent.textContent = 'Summary is temporarily unavailable. Check AI Settings and provider logs.';
                        window.clearInterval(pollSummary);
                    }
                } catch (_) {
                    // Keep polling; transient API or worker outages may recover.
                }
            }, 3000);
        }
    } catch (err) {
        container.innerHTML = '<div class="alert alert-error">Failed to load document</div>';
    }
}

// ############################
// Collection membership and document deletion.
// ############################
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

// ############################
// Preview/summary navigation and summary generation/formatting.
// ############################
let documentCarouselIndex = 0;

function moveDocumentCarousel(direction) {
    const carousel = document.getElementById('documentCarousel');
    if (!carousel) return;

    documentCarouselIndex = Math.max(0, Math.min(1, documentCarouselIndex + direction));
    carousel.dataset.slide = String(documentCarouselIndex);
    carousel.querySelector('.carousel-arrow-left').hidden = documentCarouselIndex === 0;
    carousel.querySelector('.carousel-arrow-right').hidden = documentCarouselIndex === 1;
}

function initializeDocumentCarousel() {
    const carousel = document.getElementById('documentCarousel');
    if (!carousel) return;
    documentCarouselIndex = 0;
    carousel.dataset.slide = '0';
    carousel.querySelector('.carousel-arrow-left').hidden = true;
    carousel.querySelector('.carousel-arrow-right').hidden = false;

    let touchStartX = 0;
    carousel.addEventListener('touchstart', event => { touchStartX = event.changedTouches[0].clientX; }, { passive: true });
    carousel.addEventListener('touchend', event => {
        const delta = event.changedTouches[0].clientX - touchStartX;
        if (Math.abs(delta) > 50) moveDocumentCarousel(delta < 0 ? 1 : -1);
    }, { passive: true });
}

function openSummaryModal() {
    const modal = document.getElementById('summaryModal');
    const prompt = document.getElementById('summaryPrompt');
    const submitButton = modal.querySelector('.btn-primary');
    prompt.value = '';
    submitButton.disabled = false;
    submitButton.textContent = 'Generate summary';
    modal.classList.add('visible');
    modal.setAttribute('aria-hidden', 'false');
}

function closeSummaryModal() {
    const modal = document.getElementById('summaryModal');
    modal.classList.remove('visible');
    modal.setAttribute('aria-hidden', 'true');
}

async function submitSummaryRegeneration() {
    const modal = document.getElementById('summaryModal');
    const customInstructions = document.getElementById('summaryPrompt').value.trim();
    const format = document.getElementById('summaryFormat').value;
    const formatInstructions = {
        structured: 'Organize the summary with short Markdown headings, concise paragraphs, and bullet lists where useful. Put the most important facts first.',
        concise: 'Write a concise summary in one short paragraph, followed by at most five key points.',
        bullets: 'Write a short opening sentence followed by clear, concise bullet points. Group related points under short Markdown headings when useful.',
        timeline: 'Organize relevant events chronologically. Use Markdown headings for dates or periods and concise bullet points for events. If the document has no timeline, summarize it in clear sections.'
    }[format];
    const id = new URLSearchParams(window.location.search).get('id');
    if (!id) return;
    const prompt = [
        'Create a useful, accurate summary of the document. Do not invent information. Use a professional tone.',
        formatInstructions,
        customInstructions ? `Additional instructions from the user: ${customInstructions}` : ''
    ].filter(Boolean).join('\n\n');

    const button = modal.querySelector('.btn-primary');
    button.disabled = true;
    button.textContent = 'Queueing...';
    try {
        await api.regenerateSummary(id, prompt);
        closeSummaryModal();
        const summaryContent = document.getElementById('summaryContent');
        if (summaryContent) summaryContent.innerHTML = '<span class="summary-loader"><span class="summary-spinner"></span> Generating a new summary with your prompt…</span>';
        moveDocumentCarousel(1);
        pollForRegeneratedSummary(id);
    } catch (error) {
        alert(error.message || 'Could not start summary regeneration');
        button.disabled = false;
        button.textContent = 'Generate summary';
    }
}

function pollForRegeneratedSummary(id) {
    let attempts = 0;
    const poll = window.setInterval(async () => {
        attempts += 1;
        try {
            const refreshed = await api.getDocument(id);
            if (refreshed?.summary) {
                const summaryContent = document.getElementById('summaryContent');
                if (summaryContent) summaryContent.innerHTML = formatSummary(refreshed.summary);
                window.clearInterval(poll);
            } else if (attempts >= 40) {
                const summaryContent = document.getElementById('summaryContent');
                if (summaryContent) summaryContent.textContent = 'Summary is temporarily unavailable. Check AI Settings and provider logs.';
                window.clearInterval(poll);
            }
        } catch (_) {
            if (attempts >= 40) window.clearInterval(poll);
        }
    }, 3000);
}

function formatInlineMarkdown(text) {
    const escaped = escape(text);
    return escaped.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
        .replace(/\*(.+?)\*/g, '<em>$1</em>')
        .replace(/`([^`]+)`/g, '<code>$1</code>');
}

function formatSummary(text) {
    if (!text) return '';
    const lines = String(text).replace(/\r/g, '').split('\n');
    const blocks = [];
    let paragraph = [];
    let list = [];
    let listType = '';
    const flushParagraph = () => {
        if (paragraph.length) blocks.push(`<p>${paragraph.map(formatInlineMarkdown).join(' ')}</p>`);
        paragraph = [];
    };
    const flushList = () => {
        if (list.length) blocks.push(`<${listType}>${list.map(item => `<li>${formatInlineMarkdown(item)}</li>`).join('')}</${listType}>`);
        list = [];
        listType = '';
    };

    for (const rawLine of lines) {
        const line = rawLine.trim();
        if (!line) { flushParagraph(); flushList(); continue; }
        const heading = line.match(/^#{1,3}\s+(.+)$/);
        if (heading) {
            flushParagraph(); flushList();
            blocks.push(`<h4>${formatInlineMarkdown(heading[1])}</h4>`);
            continue;
        }
        const item = line.match(/^[-*•]\s+(.+)$/) || line.match(/^\d+[.)]\s+(.+)$/);
        if (item) {
            flushParagraph();
            const nextType = /^\d/.test(line) ? 'ol' : 'ul';
            if (listType && listType !== nextType) flushList();
            listType = nextType;
            list.push(item[1]);
            continue;
        }
        flushList();
        paragraph.push(line);
    }
    flushParagraph();
    flushList();
    return blocks.join('');
}

// ############################
// Document description and edited-copy actions.
// ############################
let currentDocument = null;

function openDescriptionModal() {
    if (!currentDocument) return;
    const modal = document.getElementById('descriptionModal');
    document.getElementById('descriptionInput').value = currentDocument.description || '';
    modal.classList.add('visible');
    modal.setAttribute('aria-hidden', 'false');
}

function closeDescriptionModal() {
    const modal = document.getElementById('descriptionModal');
    modal.classList.remove('visible');
    modal.setAttribute('aria-hidden', 'true');
}

async function saveDescription() {
    if (!currentDocument) return;
    const description = document.getElementById('descriptionInput').value.trim();
    const button = document.querySelector('#descriptionModal .btn-primary');
    button.disabled = true;
    try {
        await api.updateDocument(currentDocument.id, currentDocument.fileName, description);
        currentDocument.description = description;
        document.getElementById('documentDescription').innerHTML = description ? escape(description) : '<span class="muted">No description added.</span>';
        closeDescriptionModal();
    } catch (error) {
        alert(error.message || 'Could not save description');
    } finally {
        button.disabled = false;
    }
}

// Canvas-based overlay helpers from the earlier in-page image editor flow.
let imageEditOverlays = [];

function openFileEditModal() {
    if (!currentDocument) return;
    const modal = document.getElementById('fileEditModal');
    const isPdf = currentDocument.contentType === 'application/pdf' || currentDocument.fileName?.toLowerCase().endsWith('.pdf');
    const isImage = currentDocument.contentType?.startsWith('image/');
    document.getElementById('pdfEditControls').style.display = isPdf ? 'block' : 'none';
    document.getElementById('imageEditControls').style.display = isImage ? 'block' : 'none';

    if (isPdf) {
        window.location.href = `pdf-editor.html?id=${encodeURIComponent(currentDocument.id)}`;
        return;
    } else if (isImage) {
        closeFileEditModal();
        window.location.href = `image-editor.html?id=${encodeURIComponent(currentDocument.id)}`;
        return;
    } else {
        alert('This file type is not supported by the visual editor.');
        return;
    }
    modal.classList.add('visible');
    modal.setAttribute('aria-hidden', 'false');
}

function closeFileEditModal() {
    const modal = document.getElementById('fileEditModal');
    modal.classList.remove('visible');
    modal.setAttribute('aria-hidden', 'true');
}

async function saveEditedFile() {
    if (!currentDocument) return;
    const button = document.querySelector('#fileEditModal .btn-primary');
    button.disabled = true;
    try {
        if (currentDocument.contentType === 'application/pdf' || currentDocument.fileName?.toLowerCase().endsWith('.pdf')) {
            await api.editPdf(currentDocument.id, document.getElementById('editedPdfText').value, document.getElementById('editedPdfName').value);
        } else {
            await api.editImage(currentDocument.id, imageEditOverlays, document.getElementById('editedImageName').value);
        }
        closeFileEditModal();
        alert('Edited copy created successfully. It is available in Documents.');
    } catch (error) {
        alert(error.message || 'Could not create edited copy');
    } finally {
        button.disabled = false;
    }
}

function loadImageEditCanvas() {
    const canvas = document.getElementById('imageEditCanvas');
    const image = new Image();
    image.onload = () => {
        const maxWidth = 680;
        const scale = Math.min(1, maxWidth / image.naturalWidth);
        canvas.width = Math.round(image.naturalWidth * scale);
        canvas.height = Math.round(image.naturalHeight * scale);
        canvas.dataset.naturalWidth = image.naturalWidth;
        canvas.dataset.naturalHeight = image.naturalHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
        setupImageCanvasDragging(canvas);
        redrawImageOverlayBoxes();
    };
    image.src = api.getDocumentFileUrl(currentDocument.id);
}

function addImageTextOverlay() {
    const index = imageEditOverlays.length;
    imageEditOverlays.push({ x: 0.1, y: 0.1 + index * 0.12, width: 0.8, height: 0.08, text: 'New text', color: '#1f2d1f' });
    renderImageOverlayInputs();
    redrawImageOverlayBoxes();
}

function renderImageOverlayInputs() {
    const container = document.getElementById('imageOverlayInputs');
    container.innerHTML = imageEditOverlays.map((overlay, index) => `
        <div class="image-overlay-row">
            <input aria-label="Overlay text" value="${escape(overlay.text)}" oninput="updateImageOverlay(${index}, 'text', this.value)">
            <input aria-label="Horizontal position" type="number" min="0" max="100" value="${Math.round(overlay.x * 100)}" oninput="updateImageOverlay(${index}, 'x', this.value / 100)">
            <input aria-label="Vertical position" type="number" min="0" max="100" value="${Math.round(overlay.y * 100)}" oninput="updateImageOverlay(${index}, 'y', this.value / 100)">
            <input aria-label="Text color" type="color" value="${overlay.color}" oninput="updateImageOverlay(${index}, 'color', this.value)">
            <button type="button" class="btn btn-danger btn-small" onclick="removeImageOverlay(${index})">Remove</button>
        </div>`).join('');
}

function updateImageOverlay(index, property, value) {
    imageEditOverlays[index][property] = value;
    redrawImageOverlayBoxes();
}

function removeImageOverlay(index) {
    imageEditOverlays.splice(index, 1);
    renderImageOverlayInputs();
    redrawImageOverlayBoxes();
}

function redrawImageOverlayBoxes() {
    const canvas = document.getElementById('imageEditCanvas');
    if (!canvas || !canvas.width) return;
    const image = new Image();
    image.onload = () => {
        const ctx = canvas.getContext('2d');
        ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
        ctx.lineWidth = 2;
        ctx.strokeStyle = '#3d7347';
        ctx.fillStyle = 'rgba(255,255,255,.72)';
        imageEditOverlays.forEach(overlay => {
            const x = overlay.x * canvas.width;
            const y = overlay.y * canvas.height;
            const width = overlay.width * canvas.width;
            const height = overlay.height * canvas.height;
            ctx.fillRect(x, y, width, height);
            ctx.strokeRect(x, y, width, height);
        });
    };
    image.src = api.getDocumentFileUrl(currentDocument.id);
}

let dragOverlayIndex = null;

function setupImageCanvasDragging(canvas) {
    canvas.onpointerdown = event => {
        const rect = canvas.getBoundingClientRect();
        const x = (event.clientX - rect.left) / rect.width;
        const y = (event.clientY - rect.top) / rect.height;
        dragOverlayIndex = imageEditOverlays.findLastIndex(overlay =>
            x >= overlay.x && x <= overlay.x + overlay.width && y >= overlay.y && y <= overlay.y + overlay.height);
        if (dragOverlayIndex >= 0) canvas.setPointerCapture(event.pointerId);
    };
    canvas.onpointermove = event => {
        if (dragOverlayIndex === null) return;
        const rect = canvas.getBoundingClientRect();
        const x = Math.min(1 - imageEditOverlays[dragOverlayIndex].width, Math.max(0, (event.clientX - rect.left) / rect.width));
        const y = Math.min(1 - imageEditOverlays[dragOverlayIndex].height, Math.max(0, (event.clientY - rect.top) / rect.height));
        imageEditOverlays[dragOverlayIndex].x = x;
        imageEditOverlays[dragOverlayIndex].y = y;
        redrawImageOverlayBoxes();
    };
    canvas.onpointerup = () => {
        dragOverlayIndex = null;
        renderImageOverlayInputs();
    };
}

setupImageCanvasDragging(document.getElementById('imageEditCanvas'));
