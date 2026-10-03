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
    style.textContent = '@page{size:A4;margin:16mm}body{font:11pt Segoe UI,Arial,sans-serif;color:#111}h1{font-size:20pt}h2{font-size:15pt}table{width:100%;border-collapse:collapse;margin:16px 0}th,td{border-bottom:1px solid #bbb;padding:8px;text-align:left}thead{display:table-header-group}tr{break-inside:avoid}.numeric{text-align:right}button,input,select,summary,.no-print{display:none!important}p{overflow-wrap:anywhere}.code{font-size:9pt;overflow-wrap:anywhere}';
    doc.head.appendChild(style);
    const copy = source.cloneNode(true);
    copy.querySelectorAll('button,script,iframe,.no-print').forEach(x => x.remove());
    doc.body.appendChild(copy);
    frame.contentWindow.focus();
    frame.contentWindow.print();
    // Keep the frame alive until the next print so asynchronous native dialogs can finish.
};
