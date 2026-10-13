window.sierraPrint = (id) => {
    const source = document.getElementById(id);
    if (!source) throw new Error('Printable document not found');
    document.getElementById('sierra-print-frame')?.remove();
    const frame = document.createElement('iframe');
    frame.id = 'sierra-print-frame';
    frame.title = 'Print preview';
    frame.style.cssText = 'position:fixed;width:1px;height:1px;left:-10000px;border:0';
    document.body.appendChild(frame);
    const doc = frame.contentDocument;
    const style = doc.createElement('style');
    const narrow = source.classList.contains('receipt-80') || !!source.querySelector('.receipt-80');
    const chequeWidth = Number(source.dataset.paperWidth), chequeHeight = Number(source.dataset.paperHeight);
    const cheque = source.classList.contains('cheque-print') && chequeWidth >= 100 && chequeWidth <= 300 && chequeHeight >= 50 && chequeHeight <= 150;
    const page = cheque ? `@page{size:${chequeWidth}mm ${chequeHeight}mm;margin:0}body{margin:0;font-family:Segoe UI,Arial,sans-serif}.cheque-page:last-child{page-break-after:auto!important}` : narrow ? '@page{size:80mm auto;margin:3mm}body{font:9pt Segoe UI,Arial,sans-serif;color:#111;width:74mm}h1{font-size:12pt!important}h2{font-size:10pt!important}th,td{padding:3px!important}' : '@page{size:A4;margin:16mm}body{font:11pt Segoe UI,Arial,sans-serif;color:#111}';
    const letterhead = '.letterhead{display:flex;gap:14px;align-items:flex-start;border-bottom:3px solid var(--accent,#1f4e79);padding-bottom:8px;margin-bottom:12px}.letterhead p{margin:2px 0}.letterhead-logo{max-height:64px;max-width:150px;object-fit:contain}.letterhead h1{margin:0;color:var(--accent,#1f4e79)}.lh-doc{margin-left:auto;text-align:right}.lh-doc h2{margin:0}.lh-ids,.lh-tagline{font-size:9pt}.doc-footer{margin-top:16px;border-top:1px solid #bbb;padding-top:6px;font-size:9pt}.doc-footer .words{font-style:italic}.terms{white-space:pre-line}.fiscal{font-weight:600}.sign{display:flex;gap:40px;margin-top:30px}.sign span{flex:1;border-top:1px solid #333;padding-top:4px}.lh-footer{text-align:center;color:#555}.receipt-80 .letterhead,.receipt-80 .sign{display:block;text-align:center}.receipt-80 .lh-doc{text-align:center}.receipt-80 .sign span{display:block;margin-top:24px}';
    style.textContent = page + letterhead + 'h1{font-size:20pt}h2{font-size:15pt}table{width:100%;border-collapse:collapse;margin:16px 0}th,td{border-bottom:1px solid #bbb;padding:8px;text-align:left}thead{display:table-header-group}tr{break-inside:avoid}.numeric{text-align:right}button,input,select,summary,.no-print{display:none!important}p{overflow-wrap:anywhere}.code{font-size:9pt;overflow-wrap:anywhere}';
    doc.head.appendChild(style);
    const copy = source.cloneNode(true);
    copy.querySelectorAll('button,script,iframe,.no-print').forEach(x => x.remove());
    doc.body.appendChild(copy);
    frame.contentWindow.focus();
    frame.contentWindow.print();
    // Keep the frame alive until the next print so asynchronous native dialogs can finish.
};

// Downloads a generated workbook (templates, error reports) in the browser host.
window.sierraDownload = (name, base64, type) => {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([bytes], { type: type || 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }));
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(link.href), 10000);
};

// Non-financial display preferences only: never store session tokens or document contents here.
window.sierraPreferenceRead = (key) => {
    try { const value = localStorage.getItem('rokela.preferences.' + key); return value && value.length <= 8192 ? value : null; }
    catch { return null; }
};
window.sierraPreferenceWrite = (key, json) => {
    if (typeof json !== 'string' || json.length > 8192) throw new Error('Invalid display preferences');
    localStorage.setItem('rokela.preferences.' + key, json);
};
window.sierraPreferenceRemove = (key) => localStorage.removeItem('rokela.preferences.' + key);
window.sierraConfigureShortcuts = (keys) => { window.rokelaShortcutKeys = new Set(keys); };
document.addEventListener('keydown', event => {
    if (!event.target?.closest?.('.workspace')) return;
    if (!event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey && window.rokelaShortcutKeys?.has(event.key)) event.preventDefault();
    if (event.ctrlKey && !event.altKey && !event.metaKey && event.key.toLowerCase() === 's') {
        const button = document.querySelector('.workspace [data-post-document]');
        if (!button) return;
        event.preventDefault();
        event.target.blur?.(); // commit the focused Blazor field before clicking the same visible posting action
        setTimeout(() => { if (!button.disabled) button.click(); }, 0);
    }
}, true);

// Shows a stored original (PDF, image or text) inside the page without saving it to disk.
window.sierraPreview = (id, base64, type) => {
    const frame = document.getElementById(id);
    if (!frame) return;
    if (frame.dataset.url) URL.revokeObjectURL(frame.dataset.url);
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: type === 'text/csv' ? 'text/plain' : type }));
    frame.dataset.url = url;
    frame.src = url;
};

// ---------------- document viewer: offline PDF (pdf.js), TIFF (UTIF.js), images and text ----------------
(() => {
    let pdfLib = null, tiffLib = null;
    const lib = path => new URL(path, document.baseURI).href;
    async function pdfjs() {
        if (!pdfLib) { pdfLib = await import(lib('lib/pdfjs/pdf.min.js')); pdfLib.GlobalWorkerOptions.workerSrc = lib('lib/pdfjs/pdf.worker.min.js'); }
        return pdfLib;
    }
    const script = src => new Promise((ok, fail) => { const s = document.createElement('script'); s.src = lib(src); s.onload = ok; s.onerror = () => fail(new Error('Could not load ' + src)); document.head.appendChild(s); });
    async function utif() { if (!tiffLib) tiffLib = script('lib/utif/pako_inflate.min.js').then(() => script('lib/utif/UTIF.js')); await tiffLib; return window.UTIF; }
    const bytesOf = b64 => Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    async function pdfPages(data, max, width) {
        const pdf = await (await pdfjs()).getDocument({ data, isEvalSupported: false }).promise;
        const pages = [];
        for (let i = 1; i <= Math.min(pdf.numPages, max); i++) {
            const page = await pdf.getPage(i);
            const viewport = page.getViewport({ scale: width / page.getViewport({ scale: 1 }).width });
            const canvas = document.createElement('canvas');
            canvas.width = Math.floor(viewport.width); canvas.height = Math.floor(viewport.height);
            await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
            pages.push(canvas);
        }
        return { pages, total: pdf.numPages };
    }
    async function tiffPages(data, max) {
        const UTIF = await utif();
        const ifds = UTIF.decode(data.buffer).filter(x => x.t256 && x.t257);
        const pages = [];
        for (const ifd of ifds.slice(0, max)) {
            UTIF.decodeImage(data.buffer, ifd);
            const rgba = UTIF.toRGBA8(ifd);
            const canvas = document.createElement('canvas'); canvas.width = ifd.width; canvas.height = ifd.height;
            canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(rgba.buffer, rgba.byteOffset, rgba.length), ifd.width, ifd.height), 0, 0);
            pages.push(canvas);
        }
        return { pages, total: ifds.length };
    }
    const image = (data, type) => new Promise((ok, fail) => {
        const url = URL.createObjectURL(new Blob([data], { type }));
        const img = new Image();
        img.onload = () => ok({ img, url });
        img.onerror = () => { URL.revokeObjectURL(url); fail(new Error('Image could not be read')); };
        img.src = url;
    });
    function scaled(source, w, h, width) {
        const ratio = Math.min(1, width / w), canvas = document.createElement('canvas');
        canvas.width = Math.max(1, Math.round(w * ratio)); canvas.height = Math.max(1, Math.round(h * ratio));
        const g = canvas.getContext('2d'); g.fillStyle = '#fff'; g.fillRect(0, 0, canvas.width, canvas.height); g.drawImage(source, 0, 0, canvas.width, canvas.height);
        return canvas;
    }

    // First-page picture for library cards: base64 JPEG, or null when the type has no picture.
    window.sierraThumbnail = async (base64, type) => {
        try {
            const data = bytesOf(base64);
            let canvas = null;
            if (type === 'application/pdf') canvas = (await pdfPages(data, 1, 360)).pages[0];
            else if (type === 'image/tiff') { const first = (await tiffPages(data, 1)).pages[0]; if (first) canvas = scaled(first, first.width, first.height, 360); }
            else if (type.startsWith('image/')) { const { img, url } = await image(data, type); canvas = scaled(img, img.naturalWidth, img.naturalHeight, 360); URL.revokeObjectURL(url); }
            if (!canvas) return null;
            if (canvas.width > canvas.height * 4 / 3) canvas = (() => { const c = document.createElement('canvas'); c.width = canvas.width; c.height = Math.round(canvas.width * 3 / 4); const g = c.getContext('2d'); g.fillStyle = '#eef2ec'; g.fillRect(0, 0, c.width, c.height); g.drawImage(canvas, 0, Math.round((c.height - canvas.height) / 2)); return c; })(); // landscape photos (cheques) fit inside the card
            if (canvas.height > canvas.width * 1.6) canvas = (() => { const c = document.createElement('canvas'); c.width = canvas.width; c.height = Math.round(canvas.width * 1.6); c.getContext('2d').drawImage(canvas, 0, 0); return c; })();
            return canvas.toDataURL('image/jpeg', 0.8).split(',')[1];
        } catch (e) { console.warn('Thumbnail failed', e); return null; }
    };

    // Renders a stored original as page-style sheets inside the element. Returns 'ok' or 'failed'.
    window.sierraRender = async (id, base64, type) => {
        const host = document.getElementById(id);
        if (!host) return 'failed';
        if (host.dataset.url) { URL.revokeObjectURL(host.dataset.url); delete host.dataset.url; }
        host.replaceChildren();
        const status = document.createElement('p'); status.className = 'viewer-status'; status.textContent = 'Opening…'; host.appendChild(status);
        const sheet = node => { const page = document.createElement('div'); page.className = 'viewer-page'; page.appendChild(node); host.appendChild(page); };
        const width = Math.min(1400, Math.max(360, host.clientWidth - 40) * Math.min(2, window.devicePixelRatio || 1));
        try {
            const data = bytesOf(base64);
            let shown = 1, total = 1;
            if (type === 'application/pdf') { const r = await pdfPages(data, 40, width); r.pages.forEach(sheet); shown = r.pages.length; total = r.total; }
            else if (type === 'image/tiff') { const r = await tiffPages(data, 40); r.pages.forEach(sheet); shown = r.pages.length; total = r.total; }
            else if (type.startsWith('image/')) { const { img, url } = await image(data, type); img.alt = 'Document picture'; host.dataset.url = url; sheet(img); }
            else { const pre = document.createElement('pre'); pre.className = 'viewer-text'; pre.textContent = new TextDecoder().decode(data); sheet(pre); }
            status.remove();
            if (total > shown) { const more = document.createElement('p'); more.className = 'viewer-status'; more.textContent = `Showing the first ${shown} of ${total} pages. Download the original to see the rest.`; host.appendChild(more); }
            if (total === 0) { status.textContent = 'This file has no pages to show.'; host.appendChild(status); }
            return 'ok';
        } catch (e) {
            console.warn('Preview failed', e);
            host.replaceChildren(status);
            status.textContent = 'This file could not be shown here. Download the original to open it.';
            return 'failed';
        }
    };
})();
