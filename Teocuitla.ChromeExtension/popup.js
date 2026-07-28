// Script de Control para el Popup de Teocuitla

document.addEventListener('DOMContentLoaded', () => {
  const apiUrlInput = document.getElementById('apiUrl');
  const apiKeyInput = document.getElementById('apiKey');
  const btnSave = document.getElementById('btnSave');
  const btnExtract = document.getElementById('btnExtract');
  const btnRefresh = document.getElementById('btnRefresh');
  const btnOpenAll = document.getElementById('btnOpenAll');
  const statusMsg = document.getElementById('statusMsg');
  const variantList = document.getElementById('variantList');

  // Control de pestañas
  const tabConfig = document.getElementById('tabConfig');
  const tabPending = document.getElementById('tabPending');
  const configContent = document.getElementById('configContent');
  const pendingContent = document.getElementById('pendingContent');

  let loadedVariants = []; // Lista local para guardar los pendientes cargados

  tabConfig.addEventListener('click', () => {
    tabConfig.classList.add('active');
    tabPending.classList.remove('active');
    configContent.classList.add('active');
    pendingContent.classList.remove('active');
  });

  tabPending.addEventListener('click', () => {
    tabPending.classList.add('active');
    tabConfig.classList.remove('active');
    pendingContent.classList.add('active');
    configContent.classList.remove('active');
    loadPendingVariants();
  });

  // 1. Cargar configuración previa
  chrome.storage.local.get(['apiUrl', 'apiKey'], (config) => {
    if (config.apiUrl) {
      apiUrlInput.value = config.apiUrl;
    }
    if (config.apiKey) {
      apiKeyInput.value = config.apiKey;
    }
  });

  // 2. Guardar configuración
  btnSave.addEventListener('click', () => {
    const apiUrl = apiUrlInput.value.trim();
    const apiKey = apiKeyInput.value.trim();

    if (!apiUrl) {
      updateStatus('Por favor ingresa una URL válida.', 'error');
      return;
    }

    chrome.storage.local.set({ apiUrl, apiKey }, () => {
      updateStatus('Configuración guardada exitosamente.', 'success');
    });
  });

  // 3. Forzar Extracción Manual en pestaña activa
  btnExtract.addEventListener('click', async () => {
    updateStatus('Extrayendo datos de la página...', '');
    
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab) {
      updateStatus('No se encontró una pestaña activa.', 'error');
      return;
    }

    chrome.tabs.sendMessage(tab.id, { action: 'manualExtract' }, (response) => {
      if (chrome.runtime.lastError) {
        updateStatus('La extensión no puede ejecutarse en esta página o requiere recargarse.', 'error');
      } else if (response && response.success) {
        updateStatus('Extracción manual solicitada.', 'success');
      }
    });
  });

  // 4. Refrescar lista de pendientes
  btnRefresh.addEventListener('click', () => {
    loadPendingVariants();
  });

  // 5. Abrir todos los enlaces cargados de una vez (en segundo plano)
  btnOpenAll.addEventListener('click', () => {
    if (!loadedVariants || loadedVariants.length === 0) {
      updateStatus('No hay productos pendientes para abrir.', 'error');
      return;
    }

    let openedCount = 0;
    loadedVariants.forEach(variant => {
      if (variant.urlProducto) {
        // active: false abre la pestaña en segundo plano para no interrumpir al usuario
        chrome.tabs.create({ url: variant.urlProducto, active: false });
        openedCount++;
      }
    });

    updateStatus(`Abiertas ${openedCount} pestañas en segundo plano con éxito.`, 'success');
  });

  // 6. Escuchar estatus enviado por background.js
  chrome.runtime.onMessage.addListener((message) => {
    if (message.action === 'ingestStatus') {
      if (message.success) {
        updateStatus(message.message, 'success');
        // Si está en la pestaña de pendientes, recargar la lista
        if (pendingContent.classList.contains('active')) {
          setTimeout(loadPendingVariants, 1500); // Dar un margen para que se complete el registro
        }
      } else {
        updateStatus(`Fallo de ingesta: ${message.message}`, 'error');
      }
    }
  });

  function updateStatus(text, type) {
    statusMsg.innerText = text;
    statusMsg.className = 'status-msg';
    if (type === 'success') {
      statusMsg.classList.add('status-success');
    } else if (type === 'error') {
      statusMsg.classList.add('status-error');
    }
  }

  // Carga las variantes pendientes desde el servidor
  function loadPendingVariants() {
    variantList.innerHTML = '<div class="no-variants">Cargando productos pendientes...</div>';
    loadedVariants = []; // Resetear

    chrome.storage.local.get(['apiUrl', 'apiKey'], (config) => {
      const apiUrl = config.apiUrl || 'https://localhost:7192';
      const apiKey = config.apiKey || 'TeocuitlaDefaultApiKeySecret';

      if (!apiUrl || !apiKey) {
        variantList.innerHTML = '<div class="no-variants">Configura el Servidor y API Key primero.</div>';
        return;
      }

      const endpoint = `${apiUrl.replace(/\/$/, '')}/api/ingestion/variants`;

      fetch(endpoint, {
        method: 'GET',
        headers: {
          'X-Api-Key': apiKey
        }
      })
      .then(response => {
        if (!response.ok) {
          throw new Error(`Servidor retornó código: ${response.status}`);
        }
        return response.json();
      })
      .then(variants => {
        loadedVariants = variants; // Guardar referencia global
        renderVariants(variants);
      })
      .catch(error => {
        console.error('[Teocuitla] Error al cargar pendientes:', error);
        variantList.innerHTML = `<div class="no-variants" style="color: #f44336;">No se pudo conectar al servidor: ${error.message}</div>`;
      });
    });
  }

  function renderVariants(variants) {
    if (!variants || variants.length === 0) {
      variantList.innerHTML = '<div class="no-variants">No hay productos en catálogo o todos están al día.</div>';
      return;
    }

    variantList.innerHTML = '';

    variants.forEach(variant => {
      const item = document.createElement('div');
      item.className = 'variant-item';

      const info = document.createElement('div');
      info.className = 'variant-info';

      const name = document.createElement('p');
      name.className = 'variant-name';
      name.innerText = variant.nombre || `SKU ${variant.sku}`;
      name.title = variant.nombre;

      const meta = document.createElement('div');
      meta.className = 'variant-meta';
      
      const badge = document.createElement('span');
      badge.className = 'badge-site';
      badge.innerText = variant.sitioNombre || 'Tienda';

      const price = document.createElement('span');
      price.innerText = variant.precioActual > 0 ? `$${variant.precioActual.toFixed(2)}` : '$0.00';

      const lastUpdated = document.createElement('span');
      if (variant.ultimaActualizacion) {
        const date = new Date(variant.ultimaActualizacion);
        lastUpdated.innerText = date.toLocaleDateString();
      } else {
        lastUpdated.innerText = 'Nunca';
      }

      meta.appendChild(badge);
      meta.appendChild(price);
      meta.appendChild(lastUpdated);

      info.appendChild(name);
      info.appendChild(meta);

      const actionBtn = document.createElement('button');
      actionBtn.className = 'variant-action-btn';
      actionBtn.innerText = 'Visitar';
      actionBtn.addEventListener('click', () => {
        if (variant.urlProducto) {
          chrome.tabs.create({ url: variant.urlProducto });
        }
      });

      item.appendChild(info);
      item.appendChild(actionBtn);

      item.addEventListener('mouseenter', () => {
        item.style.backgroundColor = '#161616';
      });
      item.addEventListener('mouseleave', () => {
        item.style.backgroundColor = 'transparent';
      });

      variantList.appendChild(item);
    });
  }
});
