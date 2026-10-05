// Upload page
document.addEventListener('DOMContentLoaded', () => {
    setupUploadArea();
});

let selectedFile = null;

function setupUploadArea() {
    const area = document.getElementById('uploadArea');
    const input = document.getElementById('fileInput');
    const form = document.getElementById('uploadForm');

    area.addEventListener('click', () => input.click());

    area.addEventListener('dragover', (e) => {
        e.preventDefault();
        area.classList.add('dragover');
    });

    area.addEventListener('dragleave', () => {
        area.classList.remove('dragover');
    });

    area.addEventListener('drop', (e) => {
        e.preventDefault();
        area.classList.remove('dragover');
        if (e.dataTransfer.files[0]) {
            handleFileSelect(e.dataTransfer.files[0]);
        }
    });

    input.addEventListener('change', (e) => {
        if (e.target.files[0]) {
            handleFileSelect(e.target.files[0]);
        }
    });

    form.addEventListener('submit', handleUpload);
}

function handleFileSelect(file) {
    selectedFile = file;
    const list = document.getElementById('fileList');
    list.innerHTML = `
        <li class="file-list-item">
            <span class="file-list-item-name">📎 ${escape(file.name)}</span>
            <span class="file-list-item-size">${(file.size / 1024 / 1024).toFixed(2)} MB</span>
        </li>
    `;
}

async function handleUpload(e) {
    e.preventDefault();

    if (!selectedFile) {
        window.showAlert('Select a file first', 'error');
        return;
    }

    const description = document.getElementById('docDescription').value;
    const formBtn = document.querySelector('button[type="submit"]');
    formBtn.disabled = true;
    formBtn.textContent = 'Uploading...';

    try {
        await api.uploadFile(selectedFile, description);
        window.showAlert('Document uploaded successfully!', 'success');
        setTimeout(() => window.location = '../index.html', 1500);
    } catch (err) {
        window.showAlert('Upload failed: ' + err.message, 'error');
    } finally {
        formBtn.disabled = false;
        formBtn.textContent = 'Upload';
    }
}

function showAlert(msg, type) {
    const form = document.getElementById('uploadForm');
    const alert = document.createElement('div');
    alert.className = `alert alert-${type}`;
    alert.textContent = msg;
    form.insertBefore(alert, form.firstChild);
    setTimeout(() => alert.remove(), 4000);
}

window.showAlert = showAlert;
