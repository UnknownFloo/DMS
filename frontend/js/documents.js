// Documents list page
document.addEventListener('DOMContentLoaded', () => {
    loadDocuments();
    setupSearch();
});

let allDocs = [];

async function loadDocuments() {
    const container = document.getElementById('documentsList');
    try {
        allDocs = await api.getDocuments();
        renderDocuments(allDocs);
    } catch (err) {
        container.innerHTML = '<div class="alert alert-error">Failed to load documents</div>';
    }
}

function renderDocuments(docs) {
    const container = document.getElementById('documentsList');
    
    if (!docs || docs.length === 0) {
        container.innerHTML = '<div class="empty-state" style="grid-column: 1/-1;"><h3>No documents</h3></div>';
        return;
    }

    container.innerHTML = docs.map(doc => {
        const uploadedAt = doc.uploadedAtUtc ? new Date(doc.uploadedAtUtc).toLocaleDateString() : '';
        return `
        <a href="document-detail.html?id=${doc.id}" class="card">
            <div class="card-header">
                <div style="width: 100%;">
                    <div class="card-title">${escape(doc.fileName)}</div>
                </div>
            </div>
            <div class="card-body">
                <div class="card-text">${escape(doc.description || 'No description')}</div>
            </div>
            <div class="card-footer">
                ${uploadedAt ? `<span class="card-meta">${uploadedAt}</span>` : ''}
            </div>
        </a>
    `;
    }).join('');
}

function setupSearch() {
    const input = document.getElementById('searchInput');
    let debounceTimer;
    let searchSequence = 0;
    input.addEventListener('input', () => {
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(async () => {
            const query = input.value.trim();
            const sequence = ++searchSequence;
            if (!query) {
                renderDocuments(allDocs);
                return;
            }
            try {
                const results = await api.searchDocuments(query);
                if (sequence === searchSequence) renderDocuments(results);
            } catch (error) {
                if (sequence === searchSequence) {
                    document.getElementById('documentsList').innerHTML = '<div class="alert alert-error">Search is temporarily unavailable</div>';
                }
            }
        }, 250);
    });
}
