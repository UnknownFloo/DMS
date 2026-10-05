// Dashboard
document.addEventListener('DOMContentLoaded', () => {
    loadStats();
    loadRecentDocs();
});

async function loadStats() {
    try {
        const docs = await api.getDocuments();
        const cols = await api.getCollections();
        
        document.getElementById('docCount').textContent = docs?.length || 0;
        document.getElementById('collCount').textContent = cols?.length || 0;
    } catch (err) {
        console.error('Failed to load stats:', err);
    }
}

async function loadRecentDocs() {
    const container = document.getElementById('recentDocs');
    try {
        const docs = await api.getDocuments();
        
        if (!docs || docs.length === 0) {
            container.innerHTML = '<div class="empty-state" style="grid-column: 1/-1;"><h3>No documents</h3><p>Start by uploading one!</p></div>';
            return;
        }

        container.innerHTML = docs.slice(0, 6).map(doc => `
            <a href="pages/document-detail.html?id=${doc.id}" class="card">
                <div class="card-header">
                    <div>
                        <div class="card-title">📄</div>
                    </div>
                </div>
                <div class="card-body">
                    <div class="card-title">${escape(doc.fileName)}</div>
                    <div class="card-text">${escape(doc.description || 'No description')}</div>
                </div>
                <div class="card-footer">
                    <span class="card-meta">${new Date(doc.uploadedAtUtc).toLocaleDateString()}</span>
                </div>
            </a>
        `).join('');
    } catch (err) {
        container.innerHTML = '<div class="alert alert-error">Failed to load documents</div>';
    }
}
