// ############################
// Load the PDF and build one editable page model per rendered page.
// ############################
const documentId = new URLSearchParams(location.search).get('id');
const DISPLAY_MAX = 1.35;
const EXPORT_SCALE = 2;
let sourceBytes;
let pdf;
const pages = [];

const status = message => { document.getElementById('editorStatus').textContent = message; };

async function init() {
    if (!documentId) throw new Error('Dokument-ID fehlt.');
    if (!window.pdfjsLib || !window.PDFLib || !window.Tesseract) throw new Error('PDF- oder OCR-Bibliothek konnte nicht geladen werden. Bitte Internetverbindung prüfen.');
    pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdn.jsdelivr.net/npm/pdfjs-dist@2.16.105/build/pdf.worker.min.js';
    const response = await fetch(`/api/documents/${encodeURIComponent(documentId)}/file`);
    if (!response.ok) throw new Error('PDF konnte nicht geladen werden.');
    sourceBytes = new Uint8Array(await response.arrayBuffer());
    pdf = await pdfjsLib.getDocument({ data: sourceBytes.slice() }).promise;

    const stage = document.getElementById('editorStage');
    for (let number = 1; number <= pdf.numPages; number++) {
        status(`Seite ${number} von ${pdf.numPages} wird geladen…`);
        const page = await pdf.getPage(number);
        const baseViewport = page.getViewport({ scale: 1 });
        const scale = Math.min(DISPLAY_MAX, Math.max(.5, (stage.clientWidth - 48) / baseViewport.width));
        const viewport = page.getViewport({ scale });
        const canvas = document.createElement('canvas');
        canvas.width = Math.ceil(viewport.width);
        canvas.height = Math.ceil(viewport.height);
        await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;

        const wrap = document.createElement('div');
        wrap.className = 'pdf-page';
        wrap.style.width = `${canvas.width}px`;
        wrap.style.height = `${canvas.height}px`;
        const layer = document.createElement('div');
        layer.className = 'text-layer';
        wrap.append(canvas, layer);
        stage.appendChild(wrap);
        const model = { page, baseViewport, viewport, canvas, layer, edits: [], scale };
        pages.push(model);

        const textContent = await page.getTextContent();
        const words = splitPdfText(textContent.items, textContent.styles || {}, viewport);
        if (words.length) {
            words.forEach(word => addEditableRegion(model, word));
        } else {
            await recognizePage(model, number);
        }
    }
    status(`${pdf.numPages} Seite(n) bereit. Klicke auf eine blau markierte Stelle und tippe direkt.`);
    document.getElementById('savePdf').disabled = false;
}

// ############################
// Find existing text or recognize scanned pages, then create editable regions.
// ############################
function splitPdfText(items, styles, viewport) {
    const words = [];
    const measureCanvas = document.createElement('canvas');
    const measureContext = measureCanvas.getContext('2d');
    for (const item of items) {
        if (!item.str?.trim() || !item.transform) continue;
        const transform = pdfjsLib.Util.transform(viewport.transform, item.transform);
        const fontSize = Math.max(8, Math.hypot(transform[2], transform[3]));
        const style = styles[item.fontName] || {};
        measureContext.font = `${fontSize}px ${style.fontFamily || 'sans-serif'}`;
        const tokens = [...item.str.matchAll(/\S+/g)];
        const measuredWhole = measureContext.measureText(item.str).width;
        const actualWidth = Math.max(1, item.width * viewport.scale);
        const widthScale = measuredWhole > 0 ? actualWidth / measuredWhole : 1;
        for (const token of tokens) {
            const before = measureContext.measureText(item.str.slice(0, token.index)).width * widthScale;
            const width = measureContext.measureText(token[0]).width * widthScale;
            words.push({
                x:transform[4] + before,
                y:transform[5] - fontSize,
                width:Math.max(4, width),
                height:fontSize * 1.2,
                fontSize,
                text:token[0]
            });
        }
    }
    return words;
}

async function recognizePage(model, pageNumber) {
    status(`Scan auf Seite ${pageNumber} wird erkannt…`);
    const result = await Tesseract.recognize(model.canvas, 'deu+eng', {
        logger: progress => {
            if (progress.status === 'recognizing text') status(`Scan Seite ${pageNumber}: ${Math.round((progress.progress || 0) * 100)} %`);
        }
    });
    for (const word of result.data.words || []) {
        if (!word.text?.trim()) continue;
        addEditableRegion(model, {
            x:word.bbox.x0, y:word.bbox.y0,
            width:word.bbox.x1-word.bbox.x0, height:word.bbox.y1-word.bbox.y0,
            fontSize:Math.max(8, (word.bbox.y1-word.bbox.y0) * .82), text:word.text.trim()
        });
    }
}

function addEditableRegion(model, value) {
    const record = { ...value, original:value.text, modified:false };
    const region = document.createElement('div');
    region.className = 'edit-region';
    region.title = 'Klicken und Text direkt überschreiben';
    region.style.left = `${record.x}px`;
    region.style.top = `${record.y}px`;
    region.style.width = `${Math.max(4, record.width)}px`;
    region.style.height = `${Math.max(record.height, record.fontSize * 1.15)}px`;
    region.style.font = `${record.fontSize}px Arial, sans-serif`;
    region.addEventListener('click', () => beginInlineEdit(region, record));
    model.layer.appendChild(region);
    record.region = region;
    model.edits.push(record);
}

function beginInlineEdit(region, record) {
    if (region.querySelector('textarea')) return;
    region.classList.add('is-editing');
    region.style.width = `${Math.max(record.width, Math.min(260, record.text.length * record.fontSize * .62 + 12), 52)}px`;
    const input = document.createElement('textarea');
    input.setAttribute('aria-label', `Text bearbeiten: ${record.original}`);
    input.value = record.text;
    input.style.fontSize = `${record.fontSize}px`;
    region.replaceChildren(input);
    input.addEventListener('input', () => {
        record.text = input.value;
        record.modified = record.text !== record.original;
        input.style.height = 'auto';
        input.style.height = `${Math.max(region.clientHeight, input.scrollHeight)}px`;
    });
    input.addEventListener('keydown', event => {
        if (event.key === 'Escape') { record.text = record.original; record.modified = false; finishInlineEdit(region, record); }
        if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); input.blur(); }
    });
    input.addEventListener('blur', () => finishInlineEdit(region, record), { once:true });
    input.focus();
    input.select();
}

// ############################
// Keep the page preview in sync and export changed pages as a new PDF copy.
// ############################
function finishInlineEdit(region, record) {
    const input = region.querySelector('textarea');
    if (input) record.text = input.value;
    record.modified = record.text !== record.original;
    region.classList.toggle('is-editing', false);
    region.replaceChildren(document.createTextNode(record.text));
    region.style.color = record.modified ? '#202820' : 'transparent';
    region.style.background = record.modified ? '#fff' : 'transparent';
    region.style.borderStyle = record.modified ? 'solid' : 'dashed';
    region.style.width = `${record.modified ? Math.max(record.width, measureTextWidth(record.text, record.fontSize)) : record.width}px`;
}

function measureTextWidth(text, fontSize) {
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    context.font = `${fontSize}px Arial, sans-serif`;
    return context.measureText(text).width + 2;
}

function averageBackground(ctx, x, y, width, height) {
    const samples = [];
    const take = (sx, sy, sw, sh) => {
        sx = Math.max(0, Math.min(ctx.canvas.width - 1, sx));
        sy = Math.max(0, Math.min(ctx.canvas.height - 1, sy));
        sw = Math.max(1, Math.min(ctx.canvas.width - sx, sw));
        sh = Math.max(1, Math.min(ctx.canvas.height - sy, sh));
        const pixels = ctx.getImageData(sx, sy, sw, sh).data;
        for (let i = 0; i < pixels.length; i += 4 * Math.max(1, Math.floor(pixels.length / 180 / 4))) samples.push([pixels[i], pixels[i+1], pixels[i+2]]);
    };
    const pad = 3;
    take(x, y-pad, width, 2); take(x, y+height+1, width, 2);
    take(x-pad, y, 2, height); take(x+width+1, y, 2, height);
    if (!samples.length) return '#ffffff';
    const avg = [0,1,2].map(channel => Math.round(samples.reduce((sum,pixel) => sum + pixel[channel], 0) / samples.length));
    return `rgb(${avg[0]},${avg[1]},${avg[2]})`;
}

async function exportEditedPdf() {
    for (const model of pages) {
        for (const record of model.edits) {
            const input = record.region.querySelector('textarea');
            if (input) finishInlineEdit(record.region, record);
        }
    }
    if (!pages.some(model => model.edits.some(edit => edit.modified))) {
        status('Ändere zuerst eine markierte Textstelle.');
        return;
    }
    const button = document.getElementById('savePdf');
    button.disabled = true;
    status('Bearbeitete Kopie wird erstellt…');
    try {
        const output = await PDFLib.PDFDocument.create();
        for (let i=0; i<pages.length; i++) {
            const model = pages[i];
            const viewport = model.page.getViewport({ scale:EXPORT_SCALE });
            const canvas = document.createElement('canvas');
            canvas.width = Math.ceil(viewport.width);
            canvas.height = Math.ceil(viewport.height);
            const ctx = canvas.getContext('2d', { willReadFrequently:true });
            await model.page.render({ canvasContext:ctx, viewport }).promise;
            const ratio = EXPORT_SCALE / model.scale;
            for (const edit of model.edits.filter(item => item.modified)) {
                const x = Math.max(0, Math.round(edit.x * ratio));
                const y = Math.max(0, Math.round(edit.y * ratio));
                const w = Math.max(1, Math.min(canvas.width-x, Math.round(edit.width * ratio)));
                const h = Math.max(1, Math.min(canvas.height-y, Math.round(edit.height * ratio)));
                ctx.fillStyle = averageBackground(ctx, x, y, w, h);
                ctx.fillRect(x, y, w, h);
                ctx.fillStyle = '#202820';
                ctx.font = `${Math.max(8, edit.fontSize * ratio * .82)}px Arial, sans-serif`;
                ctx.textBaseline = 'alphabetic';
                const lineHeight = Math.max(10, edit.fontSize * ratio * 1.05);
                edit.text.split('\n').forEach((line, lineIndex) => {
                    ctx.fillText(line, x, y + Math.min(h, lineHeight) * .86 + lineIndex * lineHeight, Math.max(1, w));
                });
            }
            const image = await output.embedJpg(canvas.toDataURL('image/jpeg', .96));
            const outPage = output.addPage([model.baseViewport.width, model.baseViewport.height]);
            outPage.drawImage(image, { x:0, y:0, width:model.baseViewport.width, height:model.baseViewport.height });
            status(`Seite ${i+1} von ${pages.length} wird gespeichert…`);
        }
        const blob = new Blob([await output.save()], { type:'application/pdf' });
        const result = await api.uploadEditedPdf(documentId, blob, `bearbeitet-${documentId}.pdf`);
        status('Bearbeitete Kopie gespeichert.');
        location.href = `document-detail.html?id=${encodeURIComponent(result)}`;
    } catch (error) {
        status(`Speichern fehlgeschlagen: ${error.message}`);
        button.disabled = false;
    }
}

document.getElementById('savePdf').addEventListener('click', exportEditedPdf);
init().catch(error => status(error.message));
