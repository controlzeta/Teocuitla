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
