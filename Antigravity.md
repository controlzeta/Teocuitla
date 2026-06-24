# Manifiesto de Desarrollo - Proyecto Teocuitla

Este documento define la arquitectura, el stack tecnológico, las reglas de diseño, las directrices del motor de scraping y los flujos de trabajo establecidos para el desarrollo y mantenimiento del sistema **Teocuitla**.

---

## 1. Contexto, Objetivos y Stack Tecnológico

**Misión**: Desarrollar y mantener un sistema inteligente, distribuido y de alto rendimiento para el web scraping y la monitorización de precios de comercio electrónico.

### Arquitectura del Sistema
Teocuitla está estructurado bajo un esquema limpio y distribuido en los siguientes proyectos:
* **[Teocuitla.Shared](file:///d:/Github/Teocuitla/Teocuitla.Shared)**: Biblioteca de clases común que contiene los modelos de dominio, entidades y el contexto de base de datos (`TeocuitlaDbContext`).
* **[Teocuitla.Web](file:///d:/Github/Teocuitla/Teocuitla.Web)**: Panel web interactivo para administración, monitoreo e ingesta de datos. Construido con **ASP.NET Core Blazor Server** y expone controladores REST de API (como el controlador de proxy interactivo para la captura de selectores).
* **[Teocuitla.Worker](file:///d:/Github/Teocuitla/Teocuitla.Worker)**: Servicio local en segundo plano (*Background Service* / `IHostedService`) encargado de la ejecución concurrente del rastreador de precios.
* **[Teocuitla.Tests](file:///d:/Github/Teocuitla/Teocuitla.Tests)**: Proyecto de pruebas automatizadas que utiliza **xUnit**, **Moq** y **EF Core In-Memory** para asegurar la calidad de la lógica del sistema.

### Stack Tecnológico Oficial
* **Lenguaje**: C#
* **Plataforma**: .NET 9.0 (ASP.NET Core / Worker Service)
* **ORM**: Entity Framework Core 9.0
* **Base de Datos**: SQL Server (con alojamiento remoto)
* **Interfaz de Usuario**: Blazor Server & MudBlazor (v9.5.0)
* **Scraping**: HtmlAgilityPack (fase ligera) y Selenium WebDriver con ChromeDriver (fase pesada)
* **Suite de Pruebas**: xUnit, Moq y Microsoft.EntityFrameworkCore.InMemory (v9.0.17)

---

## 2. Reglas de la Base de Datos (SQL Server)

* **Esquema Relacional**: Implementado de manera estricta mediante las siguientes tablas físicas:
  * `Catalogo_Sitios`: Configuraciones de selectores, intervalos de scraping y evasión por portal.
  * `Productos_Maestros`: Catálogo principal de productos a monitorear.
  * `Variantes_Comerciales`: Enlace físico entre un producto maestro y una tienda específica (contiene SKUs y precios).
  * `Historial_Precios`: Registro temporal de variaciones de precios y stock.
  * `Registro_Proxies`: Almacén de proxies con latencia, fallas acumuladas y estado de baneo.

* **Optimización del Historial**: Dado que `Historial_Precios` crece exponencialmente, se implementa la siguiente estrategia física:
  * Clave primaria `Id` configurada como **no agrupada** (*Non-Clustered*).
  * Creación de un **índice agrupado compuesto** (*Clustered Index*) sobre las columnas `(VarianteComercialId, FechaCaptura)` denominado `IX_HistorialPrecios_VarianteId_FechaCaptura_Clustered` para acelerar las consultas de series temporales y gráficos históricos.
  * Índice no agrupado `IX_HistorialPrecios_FechaCaptura` sobre la columna `FechaCaptura` para estadísticas globales rápidas.
  * Índice no agrupado `IX_Variantes_Comerciales_Sku` sobre la columna `Sku` en variantes comerciales para agilizar búsquedas.

* **Reglas de Integridad Referencial**:
  * Eliminación en cascada (*Cascade*) configurada en la relación `ProductoMaestro -> VarianteComercial` y `VarianteComercial -> HistorialPrecio`.
  * Eliminación restringida (*Restrict*) en la relación `CatalogoSitio -> VarianteComercial` para evitar huérfanos.

---

## 3. Reglas de Desarrollo UI/UX (MudBlazor)

* **Estética**: Implementar un diseño plano (*Flat Design*). Se debe configurar la clase `MudTheme` para remover elevaciones y sombras en tarjetas, paneles y botones.
* **Responsividad**: El diseño del panel web debe ser estrictamente **Mobile-First**, optimizado para resoluciones móviles de alta densidad como la pantalla del Samsung Galaxy S24 Ultra. Utilizar los puntos de interrupción (`xs`, `sm`, `md`) de `MudGrid` para apilar componentes correctamente.
* **Tema Oscuro**: Habilitar soporte nativo para transiciones de Modo Claro/Oscuro dinámicas vinculando la propiedad `IsDarkMode` de `MudThemeProvider`. El tema oscuro debe emplear **negro verdadero (#000000)** en su fondo para optimizar pantallas AMOLED.
* **Captura de Selectores**: Mantener el funcionamiento de [SelectorVisualDialog.razor](file:///d:/Github/Teocuitla/Teocuitla.Web/Components/Pages/SelectorVisualDialog.razor), el cual utiliza un iframe canalizado a través del controlador proxy `/api/proxy`. Este inyecta scripts interactivos para que el usuario pueda tocar elementos de una página y capturar de forma automatizada su XPath o selector CSS.

---

## 4. Reglas del Motor de Scraping (Teocuitla.Worker)

* **Estrategia Híbrida**:
  * **Fase Ligera (HttpClient + HtmlAgilityPack)**: Ejecutada de forma predeterminada para sitios con estrategia *"Standard"*. Utiliza peticiones HTTP directas con compresión automática (Brotli/Gzip) y suplantación de cabeceras (`User-Agent`, `Accept`).
  * **Fase Pesada (Selenium WebDriver)**: Activada como fallback o para portales con protecciones avanzadas. Utiliza Chrome de forma oculta configurando las ChromeOptions (`--headless=new`, `--disable-gpu`, `--no-sandbox`, `--disable-dev-shm-usage` y desactivación de imágenes para optimización de ancho de banda).

* **Parseo de Precios Adaptativo**:
  La función `ParsePrice` debe limpiar y procesar dinámicamente cadenas de texto de precios provenientes del HTML, manejando de forma automatizada formatos regionales:
  * **Formato US/UK** (ej. `"1,250.75"`): Donde la coma es separador de miles y el punto es decimal.
  * **Formato Español/Europeo** (ej. `"1.250,75"`): Donde el punto es separador de miles y la coma es decimal.
  El motor debe evaluar cuál separador aparece al final de la cadena limpia para determinar el decimal.

* **Rotación y Salud de Proxies**:
  * **Rotación Round-Robin**: Los proxies activos y no baneados se rotan secuencialmente, registrando el timestamp de su último uso.
  * **Auto-Baneo**: Si un proxy acumula 5 fallos consecutivos durante el scraping, se marca automáticamente como `Baneado = true`.
  * **Recuperación**: Los usos exitosos restablecen el contador de fallos a 0 y actualizan la latencia de red (`LatenciaMs`).

---

## 5. Instrucciones de Flujo de Trabajo para el Agente Antigravity

* **Planificación Asíncrona**: Antes de realizar cualquier cambio significativo o añadir características, genera un Plan de Implementación (`implementation_plan.md`) y una lista de tareas (`task.md`) en el directorio de artefactos para coordinar el backend (Web/Shared) del motor local (Worker).
* **Uso Autónomo de la Terminal**: Utilizar la terminal para instalar paquetes NuGet compatibles con la versión de la plataforma, ejecutar compilaciones y aplicar migraciones de base de datos.
* **Navegador Embebido**: Emplear capacidades browser-in-the-loop para probar y verificar interactivamente la responsividad de las páginas web en resoluciones móviles de alta densidad.
* **Política de Revisión (Always Ask)**: Solicitar confirmación explícita del usuario antes de alterar esquemas de la base de datos o modificar la lógica de evasión de proxies de producción.
* **Reportes Finales**: Al concluir un hito de desarrollo, generar un informe de cambios (`walkthrough.md`) detallando lo implementado, probado y verificado.

---

## 6. Reglas de Pruebas y Calidad de Código

* **Cobertura de Pruebas**: Al desarrollar nuevas funcionalidades, es obligatorio generar e implementar pruebas unitarias en el proyecto **Teocuitla.Tests**. Se debe garantizar una cobertura mínima del **70% de los casos de uso esenciales** del nuevo código introducido para asegurar la estabilidad del sistema y evitar regresiones en la lógica de negocio y persistencia.
* **Aislamiento en Pruebas**: Las pruebas que involucren base de datos deben utilizar `DbContext` respaldados por base de datos en memoria (`UseInMemoryDatabase`), declarando el nombre de la base de datos en una variable local externa a la lambda de opciones para garantizar un estado compartido consistente en el mismo caso de prueba. Limpiar el change tracker (`ChangeTracker.Clear()`) antes de las afirmaciones para validar la base de datos física.