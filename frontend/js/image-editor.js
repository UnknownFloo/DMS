// ############################
// Load the source image and keep recognized text regions as editable guides.
// ############################
const documentId = new URLSearchParams(location.search).get('id');
let overlays = [];
let selected = null;
let recognized = false;

async function init() {
    if (!documentId) throw new Error('Dokument-ID fehlt.');
    const image = document.getElementById('imageCanvas');
    image.src = `/api/documents/${encodeURIComponent(documentId)}/file`;
    await new Promise((resolve, reject) => { image.onload = resolve; image.onerror = () => reject(new Error('Bild konnte nicht geladen werden.')); });
    document.getElementById('imageStatus').textContent = `${image.naturalWidth} × ${image.naturalHeight} Pixel · Bereit für Texterkennung`;
}

// ############################
// Create, select, and position OCR boxes over the image preview.
// ############################
function addBox(box) {
    const wrap = document.getElementById('imageWrap');
    const image = wrap.querySelector('img');
    const scaleX = wrap.clientWidth / image.naturalWidth;
    const scaleY = wrap.clientHeight / image.naturalHeight;
    const element = document.createElement('div');
    element.className = 'image-box';
    element.title = `Erkannter Text: ${box.text}`;
    element.style.left = `${box.x0 * scaleX}px`;
    element.style.top = `${box.y0 * scaleY}px`;
    element.style.width = `${Math.max(20, (box.x1 - box.x0) * scaleX)}px`;
    element.style.height = `${Math.max(16, (box.y1 - box.y0) * scaleY)}px`;
    const record = { ...box, element, color: '#1f2d1f' };
    element.addEventListener('pointerdown', event => {
        event.preventDefault();
        selectBox(record);
        element.setPointerCapture(event.pointerId);
        element.dataset.dragX = event.clientX;
        element.dataset.dragY = event.clientY;
        element.dataset.dragLeft = parseFloat(element.style.left);
        element.dataset.dragTop = parseFloat(element.style.top);
    });
    element.addEventListener('pointermove', event => {
        if (!element.hasPointerCapture(event.pointerId)) return;
        const left = Math.max(0, Number(element.dataset.dragLeft) + event.clientX - Number(element.dataset.dragX));
        const top = Math.max(0, Number(element.dataset.dragTop) + event.clientY - Number(element.dataset.dragY));
        element.style.left = `${left}px`;
        element.style.top = `${top}px`;
    });
    element.addEventListener('pointerup', () => syncOverlay(record));
    element.addEventListener('pointercancel', () => syncOverlay(record));
    wrap.appendChild(element);
    overlays.push(record);
}

function selectBox(record) {
    selected = record;
    document.querySelectorAll('.image-box.selected').forEach(node => node.classList.remove('selected'));
    record.element.classList.add('selected');
    document.getElementById('imageText').value = record.text;
    document.getElementById('imageColor').value = record.color;
    document.getElementById('imageText').focus();
}

function syncOverlay(record) {
    const wrap = document.getElementById('imageWrap');
    const image = document.getElementById('imageCanvas');
    const element = record.element;
    record.x = parseFloat(element.style.left) / wrap.clientWidth;
    record.y = parseFloat(element.style.top) / wrap.clientHeight;
    record.width = parseFloat(element.style.width) / wrap.clientWidth;
    record.height = parseFloat(element.style.height) / wrap.clientHeight;
    record.fontScale = image.naturalHeight / wrap.clientHeight;
}

// ############################
// Apply text changes, run OCR on demand, and save a new image copy.
// ############################
document.getElementById('applyImageText').addEventListener('click', () => {
    if (!selected) {
        document.getElementById('imageStatus').textContent = 'Wähle zuerst eine blau markierte Textstelle aus.';
        return;
    }
    selected.text = document.getElementById('imageText').value;
    selected.color = document.getElementById('imageColor').value;
    selected.applied = true;
    selected.element.title = `Neuer Text: ${selected.text}`;
    syncOverlay(selected);
    document.getElementById('imageStatus').textContent = 'Änderung übernommen. Im Bild wird der neue Text beim Speichern eingearbeitet.';
});

document.getElementById('ocrImage').addEventListener('click', async () => {
    const status = document.getElementById('imageStatus');
    if (recognized) {
        status.textContent = 'Text wurde bereits erkannt. Bearbeite die markierten Stellen.';
        return;
    }
    status.textContent = 'Text wird erkannt…';
    document.getElementById('ocrImage').disabled = true;
    try {
        const result = await Tesseract.recognize(document.getElementById('imageCanvas'), 'deu+eng', {
            logger: progress => { if (progress.status === 'recognizing text') status.textContent = `Texterkennung: ${Math.round((progress.progress || 0) * 100)} %`; }
        });
        const lines = result.data.lines || [];
        for (const line of lines) {
            if (!line.text?.trim()) continue;
            addBox({ x0:line.bbox.x0, y0:line.bbox.y0, x1:line.bbox.x1, y1:line.bbox.y1, text:line.text.trim() });
        }
        recognized = true;
        status.textContent = overlays.length ? `${overlays.length} Textstellen erkannt. Klicke eine blaue Markierung an, um sie zu bearbeiten.` : 'Kein Text erkannt. Prüfe Bildqualität und Sprache.';
    } catch (error) {
        status.textContent = `Texterkennung fehlgeschlagen: ${error.message}`;
    } finally {
        document.getElementById('ocrImage').disabled = false;
    }
});

document.getElementById('saveImage').addEventListener('click', async () => {
    const status = document.getElementById('imageStatus');
    // Only export fields the user explicitly applied; unchanged OCR boxes are guides.
    const payload = overlays.filter(record => record.applied).map(({ x, y, width, height, text, color }) => ({ x, y, width, height, text, color }));
    if (!payload.length) {
        status.textContent = 'Es gibt noch keine übernommenen Textänderungen.';
        return;
    }
    const button = document.getElementById('saveImage');
    button.disabled = true;
    status.textContent = 'Scan wird rekonstruiert und gespeichert…';
    try {
        const result = await api.editImage(documentId, payload, `bearbeitet-${documentId}.png`);
        status.textContent = 'Bearbeitete Kopie wurde gespeichert.';
        location.href = `document-detail.html?id=${encodeURIComponent(result)}`;
    } catch (error) {
        status.textContent = `Speichern fehlgeschlagen: ${error.message}`;
        button.disabled = false;
    }
});

init().catch(error => document.getElementById('imageStatus').textContent = error.message);
