# Teocuitla 🪙

**Teocuitla** (del náhuatl *teocuitlatl*, "oro" o "metal precioso") es un sistema inteligente y robusto de monitoreo, rastreo y análisis histórico de precios (*price scraping*) de productos en múltiples sitios web. El sistema está diseñado bajo una arquitectura limpia y modular en .NET 9.0, dividida en un panel de administración interactivo y un motor de rastreo concurrente con soporte para rotación de proxies y evasión de medidas anti-bot.

---

## 🏗️ Arquitectura del Proyecto

El proyecto está estructurado en tres componentes principales:

### 1. [Teocuitla.Shared](file:///d:/Github/Teocuitla/Teocuitla.Shared)
La capa de dominio y persistencia común para toda la solución. Contiene:
* **Modelos de Datos**: Definición de entidades clave como `ProductoMaestro`, `VarianteComercial` (versión del producto en una tienda específica), `HistorialPrecio` (registro temporal de variaciones de precio), `CatalogoSitio` (configuraciones de scraping por portal) y `RegistroProxy`.
* **Contexto de Datos (`TeocuitlaDbContext`)**: Configuración de Entity Framework Core optimizada para alto rendimiento. Incluye índices agrupados (*clustered indexes*) compuestos sobre el historial de precios para agilizar las consultas de series temporales y optimizaciones en la indexación de SKUs.

### 2. [Teocuitla.Web](file:///d:/Github/Teocuitla/Teocuitla.Web)
El portal administrativo e interfaz de usuario construida con **ASP.NET Core Blazor Server** y estilizada con **MudBlazor**. Sus funciones principales son:
* **Dashboard Interactivo**: Métricas rápidas del sistema (sitios activos, proxies operativos, total de productos y variantes).
* **Gestión de Proxies**: Monitoreo en tiempo real del estado de la red de proxies, latencias y tasas de fallos.
* **Catálogo y Configuración**: Definición de selectores XPath por sitio web (`SelectorPrecioXPath`, `SelectorStockXPath`, `SelectorNombreXPath`) y estrategias de evasión de bloqueos.
* **Ingestion API**: Controladores REST listos para recibir o procesar datos de forma externa.

### 3. [Teocuitla.Worker](file:///d:/Github/Teocuitla/Teocuitla.Worker)
Un servicio de Windows / servicio en segundo plano (*Background Service* / `IHostedService`) encargado de la ejecución del rastreo:
* **Scraping Híbrido**: Utiliza **HtmlAgilityPack** para peticiones estáticas ultrarrápidas y **Selenium WebDriver** para portales complejos que requieren renderizado de JavaScript o evasión de capas de seguridad avanzadas (como Cloudflare).
* **Rotación y Salud de Proxies**: Evalúa y rota proxies de forma dinámica basándose en su latencia, fallos acumulados y estado de baneo.
* **Rastreo Inteligente**: Ejecuta las tareas de forma asíncrona y paralela respetando los intervalos de tiempo y las estrategias de evasión configuradas para cada sitio.

---

## ⚡ Características Principales

* **Monitoreo Multitienda**: Rastrea precios y disponibilidad de stock en paralelo.
* **Evasión de Bloqueos Configurable**: Soporte para diferentes niveles de evasión de bots (*Standard*, *Cloudflare*, *Heavy-JS*, etc.).
* **Rotación de Proxies Inteligente**: Filtra y selecciona automáticamente el mejor proxy disponible en función de su latencia y confiabilidad, marcando y aislando temporalmente los proxies caídos o baneados.
* **Optimización de Base de Datos**: Estructura de índices optimizada en SQL Server para soportar millones de registros de historial de precios sin degradación en los tiempos de respuesta del dashboard.
* **UI/UX Moderna**: Interfaz de usuario responsiva, intuitiva y fluida utilizando componentes MudBlazor.

---

## 🚀 Comenzando

### Requisitos Previos

* **.NET 9.0 SDK** (instalado en tu máquina de desarrollo).
* **Visual Studio 2022** (versión 17.12 o posterior) o **VS Code**.
* **SQL Server** (o una base de datos compatible configurada en la cadena de conexión).

### Configuración Inicial

1. **Clonar el repositorio**:
   ```bash
   git clone https://github.com/controlzeta/Teocuitla.git
   cd Teocuitla
   ```

2. **Configurar las Cadenas de Conexión**:
   Asegúrate de que la cadena de conexión `DefaultConnection` en los archivos `appsettings.json` de **Teocuitla.Web** y **Teocuitla.Worker** apunte a tu servidor de base de datos SQL Server:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Data Source=TU_SERVIDOR;Initial Catalog=TeocuitlaDb;User Id=TU_USUARIO;Password=TU_CONTRASEÑA;Encrypt=True;TrustServerCertificate=True;"
   }
   ```

3. **Aplicar las Migraciones**:
   Genera las tablas e índices en tu base de datos ejecutando el siguiente comando desde la raíz del proyecto:
   ```bash
   dotnet ef database update --project Teocuitla.Web
   ```

### Ejecutar la Aplicación

Para iniciar tanto el panel de administración web como el servicio de scraping en segundo plano, puedes ejecutar los proyectos usando dotnet CLI o configurando múltiples proyectos de inicio en Visual Studio.

* **Ejecutar el Panel Web (Blazor)**:
  ```bash
  dotnet run --project Teocuitla.Web
  ```

* **Ejecutar el Servicio de Rastreo (Worker)**:
  ```bash
  dotnet run --project Teocuitla.Worker
  ```

---

## 🛠️ Tecnologías Utilizadas

* **Framework Principal**: .NET 9.0 (C#)
* **ORM**: Entity Framework Core 9.0
* **Base de Datos**: SQL Server
* **Interfaz Web**: Blazor Server & MudBlazor
* **Librerías de Scraping**: HtmlAgilityPack & Selenium WebDriver
* **Compresión**: Brotli & Gzip habilitados para peticiones y respuestas optimizadas
