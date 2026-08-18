// Service Worker de Fondo para Extensión de Teocuitla

// Función para descargar los sitios y selectores configurados desde el servidor
function fetchConfiguredSites() {
  chrome.storage.local.get(['apiUrl', 'apiKey'], (config) => {
    const apiUrl = config.apiUrl || 'https://localhost:7192';
    const apiKey = config.apiKey || 'TeocuitlaDefaultApiKeySecret';

    if (!apiUrl || !apiKey) {
      console.warn('[Teocuitla] La extensión no está configurada correctamente para descargar la lista de sitios.');
      return;
    }

    const endpoint = `${apiUrl.replace(/\/$/, '')}/api/ingestion/sites`;

    fetch(endpoint, {
      method: 'GET',
      headers: {
        'X-Api-Key': apiKey
      }
    })
    .then(response => {
      if (!response.ok) {
        throw new Error(`Error en servidor: ${response.status} ${response.statusText}`);
      }
      return response.json();
    })
    .then(sites => {
      console.log('[Teocuitla] Sitios configurados cargados exitosamente:', sites);
      chrome.storage.local.set({ configuredSites: sites });
    })
    .catch(error => {
      console.error('[Teocuitla] Error al descargar los sitios desde la API:', error);
    });
  });
}

// Escuchar cuando cambie la configuración en el popup (para refrescar los sitios en caliente)
chrome.storage.onChanged.addListener((changes, namespace) => {
  if (namespace === 'local' && (changes.apiUrl || changes.apiKey)) {
    fetchConfiguredSites();
  }
});

// Descargar al iniciar o actualizar
chrome.runtime.onInstalled.addListener(() => fetchConfiguredSites());
chrome.runtime.onStartup.addListener(() => fetchConfiguredSites());

// Intentar cargar inmediatamente al inicializar el Service Worker
fetchConfiguredSites();

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'ingestProduct') {
    const productData = message.data;

    chrome.storage.local.get(['apiUrl', 'apiKey'], (config) => {
      const apiUrl = config.apiUrl || 'https://localhost:7192';
      const apiKey = config.apiKey || 'TeocuitlaDefaultApiKeySecret';

      if (!apiUrl || !apiKey) {
        console.warn('[Teocuitla] La extensión no está configurada correctamente. Revisa la URL y API Key en el Popup.');
        return;
      }

      const endpoint = `${apiUrl.replace(/\/$/, '')}/api/ingestion/extension`;

      fetch(endpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Api-Key': apiKey
        },
        body: JSON.stringify(productData)
      })
      .then(response => {
        if (!response.ok) {
          throw new Error(`Error en servidor: ${response.status} ${response.statusText}`);
        }
        return response.json();
      })
      .then(data => {
        console.log('[Teocuitla] Ingesta exitosa del producto:', data);
        const statusMsg = `Ingestado: SKU ${productData.sku}`;
        chrome.runtime.sendMessage({ action: 'ingestStatus', success: true, message: statusMsg }).catch(() => {});

        const notificationPayload = {
          action: 'showIngestNotification',
          success: true,
          sku: productData.sku,
          nombre: productData.nombre,
          precio: productData.precio,
          message: statusMsg
        };

        if (sender && sender.tab && sender.tab.id) {
          chrome.tabs.sendMessage(sender.tab.id, notificationPayload).catch(() => {});
        } else {
          chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs && tabs[0]) {
              chrome.tabs.sendMessage(tabs[0].id, notificationPayload).catch(() => {});
            }
          });
        }
      })
      .catch(error => {
        console.error('[Teocuitla] Fallo al enviar producto a la API:', error);
        chrome.runtime.sendMessage({ action: 'ingestStatus', success: false, message: error.message }).catch(() => {});

        const notificationPayload = {
          action: 'showIngestNotification',
          success: false,
          sku: productData.sku,
          message: error.message
        };

        if (sender && sender.tab && sender.tab.id) {
          chrome.tabs.sendMessage(sender.tab.id, notificationPayload).catch(() => {});
        }
      });
    });
  }

  // Nuevo mensaje para proveer los selectores del sitio actual al content script
  if (message.action === 'getSiteSelectors') {
    chrome.storage.local.get(['configuredSites'], (config) => {
      const sites = config.configuredSites || [];
      const url = message.url;
      
      try {
        const currentDomain = new URL(url).hostname.replace('www.', '').toLowerCase();
        
        // Buscar si hay un sitio que coincida con el dominio actual
        const matchedSite = sites.find(s => {
          try {
            const siteDomain = new URL(s.urlBase).hostname.replace('www.', '').toLowerCase();
            return currentDomain === siteDomain || currentDomain.endsWith('.' + siteDomain) || siteDomain.endsWith('.' + currentDomain);
          } catch (e) {
            return false;
          }
        });
        
        sendResponse({ site: matchedSite });
      } catch (err) {
        sendResponse({ site: null });
      }
    });
    return true; // Mantiene el canal abierto para respuesta asíncrona
  }
});
