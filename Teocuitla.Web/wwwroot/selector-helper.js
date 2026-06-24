window.teocuitlaRegisterMessageListener = (dotNetHelper) => {
    const handler = (event) => {
        if (event.data && event.data.type === 'teocuitla-selector-selected') {
            dotNetHelper.invokeMethodAsync('OnSelectorSelected', event.data.xpath, event.data.css, event.data.text);
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
