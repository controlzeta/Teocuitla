window.teocuitlaRegisterMessageListener = (dotNetHelper) => {
    const handler = (event) => {
        if (event.data && event.data.type === 'teocuitla-selector-selected') {
            dotNetHelper.invokeMethodAsync('OnSelectorSelected', event.data.xpath, event.data.css, event.data.text, event.data.tagName || '');
        }
    };
    window.teocuitlaMessageListener = handler;
    window.addEventListener('message', handler);
};

window.teocuitlaUnregisterMessageListener = () => {
    if (window.teocuitlaMessageListener) {
        window.removeEventListener('message', window.teocuitlaMessageListener);
        window.teocuitlaMessageListener = null;
    }
};

window.teocuitlaDownloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName ?? 'log.json';
    anchorElement.click();
    anchorElement.remove();
    URL.revokeObjectURL(url);
};

window.teocuitlaSubmitManualHtml = (html, url) => {
    let form = document.getElementById('teocuitlaManualForm');
    if (!form) {
        form = document.createElement('form');
        form.id = 'teocuitlaManualForm';
        form.method = 'POST';
        form.action = '/api/proxy/manual';
        form.target = 'selector-iframe';
        form.style.display = 'none';

        const htmlInput = document.createElement('input');
        htmlInput.type = 'hidden';
        htmlInput.name = 'html';
        htmlInput.id = 'teocuitlaManualHtmlInput';
        form.appendChild(htmlInput);

        const urlInput = document.createElement('input');
        urlInput.type = 'hidden';
        urlInput.name = 'url';
        urlInput.id = 'teocuitlaManualUrlInput';
        form.appendChild(urlInput);

        document.body.appendChild(form);
    }

    document.getElementById('teocuitlaManualHtmlInput').value = html;
    document.getElementById('teocuitlaManualUrlInput').value = url || '';
    form.submit();
};

window.teocuitlaSubmitManualHtmlFromElement = (textareaId, url) => {
    const textarea = document.getElementById(textareaId);
    if (!textarea) {
        console.error('Textarea no encontrado:', textareaId);
        return false;
    }
    const html = textarea.value;
    if (!html || html.trim() === '') {
        return false;
    }
    window.teocuitlaSubmitManualHtml(html, url);
    return true;
};


