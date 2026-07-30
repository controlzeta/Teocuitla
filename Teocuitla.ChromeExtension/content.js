function getStableHash(str) {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    const char = str.charCodeAt(i);
    hash = (hash << 5) - hash + char;
    hash |= 0;
  }
  return 'GEN-' + Math.abs(hash).toString(36).toUpperCase();
}

function extractSku(url, jsonLdProduct) {
  let sku = '';

  // 1. Intentar desde JSON-LD proporcionado
  if (jsonLdProduct && jsonLdProduct.sku) {
    sku = jsonLdProduct.sku;
  } else if (jsonLdProduct && (jsonLdProduct.mpn || jsonLdProduct.productID)) {
    sku = jsonLdProduct.mpn || jsonLdProduct.productID;
  }

  // 2. Si no hay JSON-LD, intentar extraerlo en caliente del DOM
  if (!sku) {
    const jsonLd = tryExtractJsonLd();
    if (jsonLd) {
      sku = jsonLd.sku || jsonLd.mpn || jsonLd.productID || '';
    }
  }

  // 3. Intentar meta tags
  if (!sku) {
    const metaSku = document.querySelector('meta[property="product:retailer_item_id"]') || 
                    document.querySelector('meta[itemprop="sku"]') ||
                    document.querySelector('meta[name="sku"]') ||
                    document.querySelector('meta[property="og:sku"]');
    if (metaSku) {
      sku = metaSku.content || metaSku.innerText;
    }
  }

  // 4. Intentar selectores DOM comunes de SKU
  if (!sku) {
    const domSku = document.querySelector('.product-meta__sku-number') || 
                   document.querySelector('[class*="sku-number"]') || 
                   document.querySelector('[class*="sku"]') || 
                   document.querySelector('[id*="sku"]') ||
                   document.querySelector('[data-sku]');
    if (domSku) {
      sku = domSku.innerText || domSku.getAttribute('data-sku') || domSku.value || '';
    }
  }

  // Limpiar SKU si tiene prefijos de texto
  if (sku) {
    sku = String(sku).replace(/sku\s*:\s*/i, '').trim();
  }

  // 5. Intentar regex en URL
  if (!sku) {
    const skuMatch = url.match(/\/p\/(\d+)/) || 
                     url.match(/\/dp\/([A-Z0-9]{10})/i) || 
                     url.match(/(MLM-?\d+)/i) || 
                     url.match(/\/(\d{5,12})(?:\/|\.|$)/);
    if (skuMatch) {
      sku = skuMatch[1].replace('-', '');
    }
  }

  // 6. Generar hash estable como fallback
  if (!sku) {
    sku = getStableHash(url);
  }

  return String(sku).trim();
}

function extractProductData() {
  let url = window.location.href;
  let domain = window.location.hostname.replace('www.', '');

  if (window.location.protocol === 'file:') {
    // Resolver URL real desde canonical o og:url
    const canonical = document.querySelector('link[rel="canonical"]') || document.querySelector('meta[property="og:url"]');
    const resolvedUrl = canonical ? (canonical.href || canonical.content) : '';
    if (resolvedUrl && resolvedUrl.startsWith('http')) {
      url = resolvedUrl;
      try {
        domain = new URL(url).hostname.replace('www.', '');
      } catch (e) {}
    }

    // Fallback: extraer dominio del nombre de archivo (ej: supernaturista.com_2026...)
    if (!domain) {
      const fileName = window.location.pathname.split('/').pop();
      const match = fileName.match(/^([a-zA-Z0-9.-]+)_[0-9-]{10}_/);
      if (match) {
        domain = match[1];
      }
    }
  }

  // Consultar si el servidor tiene selectores específicos configurados para este sitio
  chrome.runtime.sendMessage({ action: 'getSiteSelectors', url: url }, (response) => {
    const site = response?.site;

    if (site && (site.selectorNombreXPath || site.selectorPrecioXPath)) {
      console.log('[Teocuitla] Usando selectores configurados desde la base de datos de Teocuitla:', site);
      
      try {
        const nombreNode = evaluateSelector(site.selectorNombreXPath);
        const nombre = nombreNode ? nombreNode.innerText.trim() : '';
        
        let precio = 0;
        const priceNode = evaluateSelector(site.selectorPrecioXPath);
        if (priceNode) {
          precio = parsePrice(priceNode.textContent || priceNode.innerText);
        }

        let imagenUrl = '';
        if (site.selectorImagenXPath) {
          const imgNode = evaluateSelector(site.selectorImagenXPath);
          imagenUrl = imgNode ? (imgNode.src || imgNode.getAttribute('src') || '') : '';
        }

        const sku = extractSku(url, null);
        const color = '';
        const marca = extractBrandFromName(nombre);

        // Si la extracción fue exitosa con los selectores de la base de datos, los mantenemos y reportamos
        if (sku && nombre && precio > 0) {
          sendPayload(sku, nombre, url, precio, imagenUrl, domain, marca, site.selectorNombreXPath, site.selectorPrecioXPath, site.selectorImagenXPath);
          return;
        }
      } catch (err) {
        console.error('[Teocuitla] Error al extraer usando selectores de la base de datos, intentando auto-aprendizaje:', err);
      }
    }

    // Si no hay selectores en la base de datos, o fallaron/están desactualizados,
    // activamos la extracción con heurísticas locales de auto-aprendizaje
    executeHardcodedOrGenericExtraction(url, domain);
  });
}

function executeHardcodedOrGenericExtraction(url, domain) {
  let sku = '';
  let nombre = '';
  let precio = 0;
  let imagenUrl = '';
  let marca = 'Genérica';

  // Nodos físicos de los que se extrajeron los datos
  let nameNode = null;
  let priceNode = null;
  let imageNode = null;

  // Cadenas de XPath aprendidas
  let learnedNombreXPath = '';
  let learnedPrecioXPath = '';
  let learnedImagenXPath = '';

  try {
    if (domain.includes('costco.com.mx')) {
      // Costco México PDP
      nameNode = document.querySelector('h1') || document.querySelector('.product-name');
      nombre = nameNode ? nameNode.innerText.trim() : '';

      const skuMatch = url.match(/\/p\/(\d+)/);
      if (skuMatch) {
        sku = skuMatch[1];
      } else {
        const skuElem = document.querySelector('.product-details-code') || document.querySelector('[itemprop="sku"]');
        sku = skuElem ? skuElem.innerText.replace(/\D/g, '') : '';
      }

      priceNode = document.querySelector('.price-after-discount') || document.querySelector('.product-price-amount') || document.querySelector('.price') || document.querySelector('[itemprop="price"]');
      if (priceNode) {
        precio = parsePrice(priceNode.textContent || priceNode.innerText);
      }

      imageNode = document.querySelector('.product-image img') || document.querySelector('#product-image') || document.querySelector('img.main-image');
      imagenUrl = imageNode ? imageNode.src : '';

      marca = extractBrandFromName(nombre);
    } 
    else if (domain.includes('mercadolibre.com.mx')) {
      // Mercado Libre PDP
      nameNode = document.querySelector('.ui-pdp-title');
      nombre = nameNode ? nameNode.innerText.trim() : '';

      const skuMatch = url.match(/(MLM-?\d+)/i);
      sku = skuMatch ? skuMatch[1].replace('-', '') : '';

      const priceFraction = document.querySelector('.ui-pdp-price__part .andes-money-amount__fraction');
      const priceCents = document.querySelector('.ui-pdp-price__part .andes-money-amount__cents');
      if (priceFraction) {
        priceNode = priceFraction;
        let priceStr = priceFraction.innerText.replace(/[^\d]/g, '');
        if (priceCents) {
          priceStr += '.' + priceCents.innerText.replace(/[^\d]/g, '');
        }
        precio = parseFloat(priceStr) || 0;
      }

      imageNode = document.querySelector('.ui-pdp-gallery__figure__image') || document.querySelector('.ui-pdp-image');
      imagenUrl = imageNode ? imageNode.src : '';

      const brandElem = document.querySelector('.ui-pdp-features .ui-pdp-attribute-value');
      marca = brandElem ? brandElem.innerText.trim() : 'Genérica';
    } 
    else if (domain.includes('amazon.com.mx')) {
      // Amazon PDP
      nameNode = document.querySelector('#productTitle');
      nombre = nameNode ? nameNode.innerText.trim() : '';

      const skuMatch = url.match(/\/dp\/([A-Z0-9]{10})/i);
      sku = skuMatch ? skuMatch[1] : '';

      const priceWhole = document.querySelector('.a-price-whole');
      const priceFraction = document.querySelector('.a-price-fraction');
      if (priceWhole) {
        priceNode = priceWhole;
        let priceStr = priceWhole.innerText.replace(/[^\d]/g, '');
        if (priceFraction) {
          priceStr += '.' + priceFraction.innerText.replace(/[^\d]/g, '');
        }
        precio = parseFloat(priceStr) || 0;
      }

      imageNode = document.querySelector('#landingImage') || document.querySelector('#imgBlkFront');
      imagenUrl = imageNode ? imageNode.src : '';

      const brandElem = document.querySelector('#bylineInfo') || document.querySelector('.brand-link');
      if (brandElem) {
        marca = brandElem.innerText.replace(/Visita la tienda de|Marca:/i, '').trim();
      }
    }
    else {
      // --- ESTRATEGIA GENÉRICA (Con Detección de Nodos para Auto-aprendizaje) ---
      console.log('[Teocuitla] Ejecutando estrategia de extracción genérica con auto-aprendizaje...');
      
      // 1. Intentar JSON-LD
      const jsonLdProduct = tryExtractJsonLd();
      if (jsonLdProduct) {
        nombre = jsonLdProduct.name || '';
        sku = jsonLdProduct.sku || jsonLdProduct.mpn || jsonLdProduct.productID || '';
        
        if (jsonLdProduct.offers) {
          const offers = jsonLdProduct.offers;
          if (Array.isArray(offers) && offers.length > 0) {
            precio = parseFloat(offers[0].price) || parseFloat(offers[0].lowPrice) || 0;
          } else {
            precio = parseFloat(offers.price) || parseFloat(offers.lowPrice) || 0;
          }
        }
        
        if (jsonLdProduct.image) {
          if (Array.isArray(jsonLdProduct.image) && jsonLdProduct.image.length > 0) {
            imagenUrl = jsonLdProduct.image[0];
          } else if (typeof jsonLdProduct.image === 'object') {
            imagenUrl = jsonLdProduct.image.url || '';
          } else {
            imagenUrl = jsonLdProduct.image;
          }
        }

        if (jsonLdProduct.brand) {
          marca = typeof jsonLdProduct.brand === 'object' ? jsonLdProduct.brand.name : jsonLdProduct.brand;
        }

        // Si JSON-LD funcionó, intentamos hacer match visual en el DOM para aprender los selectores
        if (nombre) {
          nameNode = Array.from(document.querySelectorAll('h1, h2, h3, span, div'))
            .find(el => el.innerText.trim() === nombre);
        }
        if (precio > 0) {
          priceNode = Array.from(document.querySelectorAll('span, div, p, b, strong'))
            .find(el => parsePrice(el.innerText) === precio);
        }
        if (imagenUrl) {
          imageNode = Array.from(document.querySelectorAll('img'))
            .find(el => el.src === imagenUrl);
        }
      }

      // 2. Intentar Meta tags (si JSON-LD no dio nombre o precio)
      if (!nombre) {
        const metaTitle = document.querySelector('meta[property="og:title"]') || document.querySelector('meta[name="twitter:title"]');
        if (metaTitle) {
          nombre = metaTitle.content.trim();
          learnedNombreXPath = metaTitle.property ? `//meta[@property='${metaTitle.property}']` : `//meta[@name='${metaTitle.name}']`;
        } else {
          nombre = document.title.split('-')[0].split('|')[0].trim();
        }
      }

      if (precio === 0) {
        const metaPrice = document.querySelector('meta[property="product:price:amount"]') || 
                          document.querySelector('meta[property="og:price:amount"]') ||
                          document.querySelector('meta[name="twitter:data1"]');
        if (metaPrice) {
          precio = parsePrice(metaPrice.content);
          learnedPrecioXPath = metaPrice.property ? `//meta[@property='${metaPrice.property}']` : `//meta[@name='${metaPrice.name}']`;
        }
      }

      if (!imagenUrl) {
        const metaImg = document.querySelector('meta[property="og:image"]') || document.querySelector('meta[name="twitter:image"]');
        if (metaImg) {
          imagenUrl = metaImg.content;
          learnedImagenXPath = metaImg.property ? `//meta[@property='${metaImg.property}']` : `//meta[@name='${metaImg.name}']`;
        }
      }

      if (!sku) {
        sku = extractSku(url, jsonLdProduct);
      }

      // 3. Fallback DOM Heuristics (si aún faltan datos cruciales)
      if (!nombre) {
        nameNode = document.querySelector('h1');
        nombre = nameNode ? nameNode.innerText.trim() : '';
      }

      if (precio === 0) {
        priceNode = document.querySelector('.price') || document.querySelector('[class*="price"]') || document.querySelector('[id*="price"]');
        if (priceNode) {
          precio = parsePrice(priceNode.textContent || priceNode.innerText);
        }
      }

      if (!imagenUrl) {
        imageNode = document.querySelector('main img') || document.querySelector('article img') || document.querySelector('img[src*="product"]');
        imagenUrl = imageNode ? imageNode.src : '';
      }

      if (marca === 'Genérica' && nombre) {
        marca = extractBrandFromName(nombre);
      }
    }

    // Generar selectores XPath inteligentes para auto-aprendizaje si detectamos los nodos
    if (!learnedNombreXPath && nameNode) {
      learnedNombreXPath = getSmartXPath(nameNode);
    }
    if (!learnedPrecioXPath && priceNode) {
      learnedPrecioXPath = getSmartXPath(priceNode);
    }
    if (!learnedImagenXPath && imageNode) {
      learnedImagenXPath = getSmartXPath(imageNode);
    }

    if (sku && nombre && precio > 0) {
      sendPayload(sku, nombre, url, precio, imagenUrl, domain, marca, learnedNombreXPath, learnedPrecioXPath, learnedImagenXPath);
    } else {
      console.warn('[Teocuitla] Datos insuficientes para la extracción local/genérica. Nombre:', nombre, 'Precio:', precio, 'SKU:', sku);
      chrome.runtime.sendMessage({
        action: 'ingestStatus',
        success: false,
        message: `Extracción local falló: Nombre='${nombre || 'No detectado'}', Precio=${precio}, SKU='${sku || 'No detectado'}'`
      });
    }
  } catch (err) {
    console.error('[Teocuitla] Error al extraer detalles del producto (local/genérico):', err);
    chrome.runtime.sendMessage({
      action: 'ingestStatus',
      success: false,
      message: `Error interno de extracción: ${err.message}`
    });
  }
}

function sendPayload(sku, nombre, url, precio, imagenUrl, domain, marca, nombreXPath, precioXPath, imagenXPath) {
  if (imagenUrl && imagenUrl.startsWith('/')) {
    imagenUrl = window.location.origin + imagenUrl;
  }

  const payload = {
    sku: String(sku || '').trim(),
    nombre: String(nombre || '').trim(),
    urlProducto: url,
    precio: precio,
    imagenUrl: imagenUrl,
    dominio: domain,
    marca: String(marca || 'Genérica').trim(),
    // Proporcionar los selectores aprendidos/corregidos en caliente
    selectorNombreXPath: nombreXPath || '',
    selectorPrecioXPath: precioXPath || '',
    selectorImagenXPath: imagenXPath || ''
  };

  console.log('[Teocuitla] Enviando producto y selectores corregidos a la base de datos:', payload);
  chrome.runtime.sendMessage({ action: 'ingestProduct', data: payload });
}

// Evalúa un selector (tanto si es XPath como CSS Selector)
function evaluateSelector(selector) {
  if (!selector) return null;
  const trimmed = selector.trim();

  // Determinar si es un XPath
  const isXPath = trimmed.startsWith('/') || 
                  trimmed.startsWith('./') || 
                  trimmed.startsWith('../') || 
                  trimmed.startsWith('(') || 
                  trimmed.includes('[@') || 
                  trimmed.includes('text()') || 
                  trimmed.includes('contains(');

  if (isXPath) {
    try {
      const result = document.evaluate(trimmed, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null);
      return result.singleNodeValue;
    } catch (e) {
      console.error('[Teocuitla] Error al evaluar XPath:', trimmed, e);
      return null;
    }
  } else {
    try {
      return document.querySelector(trimmed);
    } catch (e) {
      console.error('[Teocuitla] Error al evaluar CSS Selector:', trimmed, e);
      return null;
    }
  }
}

// Genera un XPath inteligente buscando el ID estable más cercano
function getSmartXPath(element) {
  if (!element) return '';
  
  const elementId = element.getAttribute('id');
  if (elementId && !isDynamicId(elementId)) {
    return `//*[@id='${elementId}']`;
  }
  
  let current = element;
  const path = [];
  
  while (current && current.nodeType === Node.ELEMENT_NODE) {
    const currentId = current.getAttribute('id');
    if (currentId && !isDynamicId(currentId)) {
      path.unshift(`*[@id='${currentId}']`);
      break;
    }
    
    // Obtener el índice del elemento entre hermanos del mismo tipo
    let index = 1;
    let sibling = current.previousElementSibling;
    while (sibling) {
      if (sibling.tagName === current.tagName) {
        index++;
      }
      sibling = sibling.previousElementSibling;
    }
    
    const tagName = current.tagName.toLowerCase();
    path.unshift(`${tagName}[${index}]`);
    current = current.parentElement;
  }
  
  return '//' + path.join('/');
}

function isDynamicId(id) {
  if (!id || typeof id !== 'string') return true;
  // Descartar IDs dinámicos y autogenerados
  if (id.includes('ng-') || id.includes('mat-') || id.includes('ember') || id.includes('react-')) return true;
  if (/\d{4,}/.test(id)) return true; // Contiene 4 o más números consecutivos
  return false;
}

// Auxiliares
function parsePrice(priceText) {
  if (!priceText) return 0;
  const cleanPrice = priceText.replace(/[^\d.]/g, '');
  return parseFloat(cleanPrice) || 0;
}

function extractBrandFromName(nameText) {
  if (!nameText) return 'Genérica';
  const nameLower = nameText.toLowerCase();
  if (nameLower.includes('isopure')) return 'Isopure';
  if (nameLower.includes('orgain')) return 'Orgain';
  if (nameLower.includes('optimum nutrition') || nameLower.includes(' ON ')) return 'Optimum Nutrition';
  if (nameLower.includes('premier protein')) return 'Premier Protein';
  
  const firstWord = nameText.trim().split(' ')[0];
  return firstWord && firstWord.length > 2 ? firstWord : 'Genérica';
}

function tryExtractJsonLd() {
  const scripts = document.querySelectorAll('script[type="application/ld+json"]');
  for (const script of scripts) {
    try {
      const data = JSON.parse(script.textContent || script.innerText);
      const product = findProductInJson(data);
      if (product) return product;
    } catch (e) {}
  }
  return null;
}

function findProductInJson(obj) {
  if (!obj) return null;
  if (Array.isArray(obj)) {
    for (const item of obj) {
      const res = findProductInJson(item);
      if (res) return res;
    }
  } else if (typeof obj === 'object') {
    if (obj['@type'] === 'Product') {
      return obj;
    }
    for (const key in obj) {
      if (typeof obj[key] === 'object') {
        const res = findProductInJson(obj[key]);
        if (res) return res;
      }
    }
  }
  return null;
}

// Ejecutar al cargar la página completamente
if (document.readyState === 'complete') {
  extractProductData();
} else {
  window.addEventListener('load', extractProductData);
}

// Escuchar solicitudes de extracción manual desde el popup
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === 'manualExtract') {
    extractProductData();
    sendResponse({ success: true, message: 'Extracción manual gatillada.' });
  }
});
