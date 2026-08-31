using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
using Teocuitla.Shared.Dtos;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Helpers;

namespace Teocuitla.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IngestionController : ControllerBase
    {
        private readonly TeocuitlaDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngestionController> _logger;
        private readonly Teocuitla.Web.Services.IngestionNotificationService _notificationService;

        public IngestionController(
            TeocuitlaDbContext context,
            IConfiguration configuration,
            ILogger<IngestionController> logger,
            Teocuitla.Web.Services.IngestionNotificationService notificationService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _notificationService = notificationService;
        }

        [HttpPost("bulk")]
        public async Task<IActionResult> BulkIngest([FromBody] List<IngestionItemDto> items)
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            if (items == null || items.Count == 0)
            {
                return BadRequest("El lote de datos está vacío.");
            }

            _logger.LogInformation("Iniciando ingesta masiva de {Count} registros de precios.", items.Count);

            try
            {
                // Obtener la conexión subyacente de SQL Server
                var connection = _context.Database.GetDbConnection() as SqlConnection;
                if (connection == null)
                {
                    throw new InvalidOperationException("No se pudo obtener la conexión SqlConnection subyacente.");
                }

                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                // Utilizar una transacción para garantizar consistencia y máximo rendimiento
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    var sqlConnection = (SqlConnection)_context.Database.GetDbConnection();
                    var sqlTransaction = (SqlTransaction)transaction.GetDbTransaction();

                    // 1. Crear la tabla temporal en la base de datos
                    var createTempTableCmd = sqlConnection.CreateCommand();
                    createTempTableCmd.Transaction = sqlTransaction;
                    createTempTableCmd.CommandText = @"
                        CREATE TABLE #TempIngest (
                            VarianteId INT,
                            Precio DECIMAL(18,2),
                            EnStock BIT,
                            Fecha DATETIME2,
                            ImagenUrl NVARCHAR(1000) NULL
                        );";
                    await createTempTableCmd.ExecuteNonQueryAsync();

                    // 2. Preparar el DataTable para SqlBulkCopy
                    var dataTable = new DataTable();
                    dataTable.Columns.Add("VarianteId", typeof(int));
                    dataTable.Columns.Add("Precio", typeof(decimal));
                    dataTable.Columns.Add("EnStock", typeof(bool));
                    dataTable.Columns.Add("Fecha", typeof(DateTime));
                    dataTable.Columns.Add("ImagenUrl", typeof(string));

                    foreach (var item in items)
                    {
                        dataTable.Rows.Add(item.VarianteComercialId, item.Precio, item.EnStock, item.FechaCaptura, item.ImagenUrl);
                    }

                    // 3. Insertar masivamente en la tabla temporal
                    using (var bulkCopy = new SqlBulkCopy(sqlConnection, SqlBulkCopyOptions.Default, sqlTransaction))
                    {
                        bulkCopy.DestinationTableName = "#TempIngest";
                        bulkCopy.ColumnMappings.Add("VarianteId", "VarianteId");
                        bulkCopy.ColumnMappings.Add("Precio", "Precio");
                        bulkCopy.ColumnMappings.Add("EnStock", "EnStock");
                        bulkCopy.ColumnMappings.Add("Fecha", "Fecha");
                        bulkCopy.ColumnMappings.Add("ImagenUrl", "ImagenUrl");

                        await bulkCopy.WriteToServerAsync(dataTable);
                    }

                    // 4. Ejecutar la lógica de inserción y actualización en un único lote de SQL
                    var mergeCmd = sqlConnection.CreateCommand();
                    mergeCmd.Transaction = sqlTransaction;
                    mergeCmd.CommandText = @"
                        -- Insertar registros en el historial histórico de precios solo si el precio o stock cambió
                        INSERT INTO Historial_Precios (VarianteComercialId, Precio, EnStock, FechaCaptura)
                        SELECT T.VarianteId, T.Precio, T.EnStock, T.Fecha 
                        FROM #TempIngest T
                        INNER JOIN Variantes_Comerciales V ON T.VarianteId = V.Id
                        WHERE V.PrecioActual IS NULL 
                           OR V.PrecioActual <> T.Precio 
                           OR V.EnStock <> T.EnStock;

                        -- Actualizar el estado actual en la tabla de variantes comerciales
                        UPDATE V
                        SET 
                            V.PrecioAnterior = V.PrecioActual,
                            V.PrecioActual = T.Precio,
                            V.EnStock = T.EnStock,
                            V.UltimaActualizacion = T.Fecha,
                            V.ImagenUrl = CASE WHEN V.ImagenUrl IS NOT NULL AND V.ImagenUrl <> '' THEN V.ImagenUrl ELSE T.ImagenUrl END
                        FROM Variantes_Comerciales V
                        INNER JOIN #TempIngest T ON V.Id = T.VarianteId;

                        -- Actualizar el último rastreo en la tabla de catálogo de sitios
                        UPDATE C
                        SET 
                            C.UltimoRastreo = S.MaxFecha
                        FROM Catalogo_Sitios C
                        INNER JOIN (
                            SELECT V2.CatalogoSitioId, MAX(T2.Fecha) AS MaxFecha
                            FROM Variantes_Comerciales V2
                            INNER JOIN #TempIngest T2 ON V2.Id = T2.VarianteId
                            GROUP BY V2.CatalogoSitioId
                        ) S ON C.Id = S.CatalogoSitioId;
                    ";
                    
                    int rowsAffected = await mergeCmd.ExecuteNonQueryAsync();

                    // 5. Eliminar la tabla temporal
                    var dropTempTableCmd = sqlConnection.CreateCommand();
                    dropTempTableCmd.Transaction = sqlTransaction;
                    dropTempTableCmd.CommandText = "DROP TABLE #TempIngest;";
                    await dropTempTableCmd.ExecuteNonQueryAsync();

                    // Confirmar transacción
                    await transaction.CommitAsync();
                }

                _logger.LogInformation("Ingesta masiva completada con éxito.");
                return Ok(new { Message = "Lote procesado exitosamente.", ProcessedCount = items.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la ingesta masiva de precios.");
                return StatusCode(500, $"Error interno al procesar el lote: {ex.Message}");
            }
        }

        [HttpPost("extension")]
        public async Task<IActionResult> IngestFromExtension([FromBody] ExtensionIngestionDto dto)
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.Sku) || string.IsNullOrWhiteSpace(dto.UrlProducto))
            {
                return BadRequest("Datos de producto inválidos.");
            }

            dto.Sku = DataNormalizer.NormalizeSku(dto.Sku);
            dto.Marca = DataNormalizer.NormalizeBrand(dto.Marca);
            dto.UrlProducto = DataNormalizer.CleanProductUrl(dto.UrlProducto);
            dto.ImagenUrl = DataNormalizer.NormalizeImageUrl(DataNormalizer.MakeAbsoluteUrl(dto.ImagenUrl, dto.UrlProducto));

            _logger.LogInformation("Recibiendo ingesta de producto desde extensión: SKU={Sku}, Dominio={Dominio}", dto.Sku, dto.Dominio);

            try
            {
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    // 1. Resolver o crear el sitio de catálogo
                    var baseDomain = dto.Dominio.ToLower().Trim();
                    var site = await _context.CatalogoSitios
                        .Where(s => s.UrlBase != null && s.UrlBase.Contains(baseDomain))
                        .OrderBy(s => s.Id)
                        .FirstOrDefaultAsync();

                    if (site == null)
                    {
                        _logger.LogWarning("La extensión intentó enviar datos para un sitio no registrado en la base de datos: {Dominio}", baseDomain);
                        return BadRequest($"El sitio con dominio '{baseDomain}' no existe en la base de datos.");
                    }

                    // Auto-aprendizaje/corrección de selectores a partir de la extensión
                    bool siteUpdated = false;
                    if (!string.IsNullOrEmpty(dto.SelectorNombreXPath) && site.SelectorNombreXPath != dto.SelectorNombreXPath)
                    {
                        site.SelectorNombreXPath = dto.SelectorNombreXPath;
                        siteUpdated = true;
                    }
                    if (!string.IsNullOrEmpty(dto.SelectorPrecioXPath) && site.SelectorPrecioXPath != dto.SelectorPrecioXPath)
                    {
                        site.SelectorPrecioXPath = dto.SelectorPrecioXPath;
                        siteUpdated = true;
                    }
                    if (!string.IsNullOrEmpty(dto.SelectorImagenXPath) && site.SelectorImagenXPath != dto.SelectorImagenXPath)
                    {
                        site.SelectorImagenXPath = dto.SelectorImagenXPath;
                        siteUpdated = true;
                    }

                    if (siteUpdated)
                    {
                        _context.CatalogoSitios.Update(site);
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Selectores corregidos/actualizados automáticamente para el sitio {Nombre} desde la extensión.", site.Nombre);
                    }

                    // 2. Buscar variante comercial existente por URL limpia, SKU o coincidencia de URL
                    var cleanDtoUrl = DataNormalizer.CleanProductUrl(dto.UrlProducto);
                    var cleanDtoSku = DataNormalizer.NormalizeSku(dto.Sku);

                    var allVariantsInSite = await _context.VariantesComerciales
                        .Where(v => v.CatalogoSitioId == site.Id || v.CatalogoSitioId == null)
                        .ToListAsync();

                    var matchingVariants = allVariantsInSite.Where(v => 
                        (!string.IsNullOrEmpty(v.UrlProducto) && DataNormalizer.CleanProductUrl(v.UrlProducto).Equals(cleanDtoUrl, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(v.Sku) && (
                            v.Sku.Equals(cleanDtoSku, StringComparison.OrdinalIgnoreCase) ||
                            (cleanDtoSku.Length >= 4 && v.Sku.Contains(cleanDtoSku, StringComparison.OrdinalIgnoreCase)) ||
                            (v.Sku.Length >= 4 && cleanDtoSku.Contains(v.Sku, StringComparison.OrdinalIgnoreCase))
                        )) ||
                        (!string.IsNullOrEmpty(v.UrlProducto) && (
                            (!string.IsNullOrEmpty(cleanDtoSku) && cleanDtoSku.Length >= 4 && v.UrlProducto.Contains(cleanDtoSku, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(v.Sku) && v.Sku.Length >= 4 && cleanDtoUrl.Contains(v.Sku, StringComparison.OrdinalIgnoreCase))
                        ))
                    ).ToList();

                    var variant = matchingVariants
                        .OrderByDescending(v => v.UltimaActualizacion.HasValue)
                        .ThenByDescending(v => v.UltimaActualizacion)
                        .ThenBy(v => v.Id)
                        .FirstOrDefault();

                    bool priceChanged = false;

                    if (variant != null)
                    {
                        priceChanged = variant.PrecioActual != dto.Precio || variant.EnStock != (dto.Precio > 0);

                        // Actualizar fecha y datos en todas las variantes coincidentes registradas para quitar de pendientes
                        foreach (var mVar in matchingVariants)
                        {
                            mVar.PrecioAnterior = mVar.PrecioActual;
                            mVar.PrecioActual = dto.Precio;
                            mVar.EnStock = dto.Precio > 0;
                            mVar.UltimaActualizacion = DateTime.Now;
                            mVar.UrlProducto = cleanDtoUrl;
                            if (!string.IsNullOrEmpty(dto.ImagenUrl) && string.IsNullOrEmpty(mVar.ImagenUrl))
                            {
                                mVar.ImagenUrl = dto.ImagenUrl;
                            }
                            if (!mVar.Activo)
                            {
                                mVar.Activo = true;
                            }
                            _context.VariantesComerciales.Update(mVar);
                        }

                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        priceChanged = true;

                        // Buscar si existe un producto maestro por nombre y marca
                        var masterProduct = await _context.ProductosMaestros
                            .Where(p => p.Nombre == dto.Nombre && p.Marca == dto.Marca)
                            .OrderBy(p => p.Id)
                            .FirstOrDefaultAsync();

                        if (masterProduct == null)
                        {
                            masterProduct = new ProductoMaestro
                            {
                                Nombre = dto.Nombre,
                                Marca = dto.Marca,
                                Categoria = "Extensión de Navegador",
                                Descripcion = "Ingresado mediante extensión de Chrome",
                                FechaCreacion = DateTime.Now
                            };
                            _context.ProductosMaestros.Add(masterProduct);
                            await _context.SaveChangesAsync();
                        }

                        // Crear variante
                        variant = new VarianteComercial
                        {
                            ProductoMaestroId = masterProduct.Id,
                            CatalogoSitioId = site.Id,
                            Sku = dto.Sku,
                            Nombre = dto.Nombre,
                            UrlProducto = cleanDtoUrl,
                            PrecioActual = dto.Precio,
                            EnStock = dto.Precio > 0,
                            UltimaActualizacion = DateTime.Now,
                            ImagenUrl = dto.ImagenUrl
                        };
                        _context.VariantesComerciales.Add(variant);
                        await _context.SaveChangesAsync();
                    }

                    // 3. Registrar en historial de precios solo si cambió
                    if (priceChanged)
                    {
                        var history = new HistorialPrecio
                        {
                            VarianteComercialId = variant.Id,
                            Precio = dto.Precio,
                            EnStock = dto.Precio > 0,
                            FechaCaptura = DateTime.Now
                        };
                        _context.HistorialPrecios.Add(history);
                    }
                    
                    // Actualizar fecha de último rastreo del sitio
                    site.UltimoRastreo = DateTime.Now;
                    _context.CatalogoSitios.Update(site);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                _notificationService.NotifyIngestion(dto.Sku, dto.Nombre, dto.Precio, dto.Dominio);

                return Ok(new { Message = "Producto ingerido con éxito desde la extensión.", Sku = dto.Sku });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al procesar ingesta desde extensión.");
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpGet("sites")]
        public async Task<IActionResult> GetConfiguredSites()
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            try
            {
                var sites = await _context.CatalogoSitios
                    .Where(s => s.Activo)
                    .OrderBy(s => s.Id)
                    .Select(s => new {
                        s.Id,
                        s.Nombre,
                        s.UrlBase,
                        s.SelectorProductoXPath,
                        s.SelectorPrecioXPath,
                        s.SelectorStockXPath,
                        s.SelectorNombreXPath,
                        s.SelectorImagenXPath
                    })
                    .ToListAsync();
                return Ok(sites);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la lista de sitios para la extensión.");
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpGet("variants")]
        public async Task<IActionResult> GetVariants()
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            try
            {
                var today = DateTime.Today;
                var now = DateTime.Now;

                var totalPending = await _context.VariantesComerciales
                    .Include(v => v.CatalogoSitio)
                    .Where(v => v.Activo && (v.UltimaActualizacion == null || 
                                (v.UltimaActualizacion < today && 
                                 v.CatalogoSitio != null && 
                                 v.UltimaActualizacion < now.AddMinutes(-v.CatalogoSitio.IntervaloMinutos))))
                    .CountAsync();

                var rawVariants = await _context.VariantesComerciales
                    .Include(v => v.CatalogoSitio)
                    .Where(v => v.Activo && (v.UltimaActualizacion == null || 
                                (v.UltimaActualizacion < today && 
                                 v.CatalogoSitio != null && 
                                 v.UltimaActualizacion < now.AddMinutes(-v.CatalogoSitio.IntervaloMinutos))))
                    .Select(v => new {
                        v.Id,
                        v.Nombre,
                        v.Sku,
                        v.UrlProducto,
                        v.PrecioActual,
                        v.UltimaActualizacion,
                        SitioNombre = v.CatalogoSitio != null ? v.CatalogoSitio.Nombre : "Tienda"
                    })
                    .OrderBy(v => v.UltimaActualizacion)
                    .Take(150)
                    .ToListAsync();

                // Filtrar variantes con URLs duplicadas usando CleanProductUrl para retornar únicamente URLs únicas
                var variants = rawVariants
                    .Where(v => !string.IsNullOrWhiteSpace(v.UrlProducto))
                    .GroupBy(v => DataNormalizer.CleanProductUrl(v.UrlProducto).ToLower())
                    .Select(g => g.First())
                    .Take(50)
                    .ToList();

                return Ok(new { Total = totalPending, Variants = variants });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la lista de variantes para la extensión.");
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpPost("clean-duplicates")]
        [HttpDelete("duplicates")]
        public async Task<IActionResult> CleanDuplicates()
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            try
            {
                var allVariants = await _context.VariantesComerciales
                    .Where(v => v.UrlProducto != null && v.UrlProducto != "")
                    .ToListAsync();

                var duplicatesGrouped = allVariants
                    .GroupBy(v => DataNormalizer.CleanProductUrl(v.UrlProducto).ToLower())
                    .Where(g => g.Count() > 1)
                    .ToList();

                int removedCount = 0;

                foreach (var group in duplicatesGrouped)
                {
                    // Seleccionar la variante principal (la que tenga fecha más reciente o menor Id)
                    var primaryVariant = group
                        .OrderByDescending(v => v.UltimaActualizacion.HasValue)
                        .ThenByDescending(v => v.UltimaActualizacion)
                        .ThenBy(v => v.Id)
                        .First();

                    var duplicates = group.Where(v => v.Id != primaryVariant.Id).ToList();

                    foreach (var dup in duplicates)
                    {
                        // Reasignar el historial de precios al registro principal
                        var histories = await _context.HistorialPrecios
                            .Where(h => h.VarianteComercialId == dup.Id)
                            .ToListAsync();
                        foreach (var h in histories)
                        {
                            h.VarianteComercialId = primaryVariant.Id;
                        }

                        _context.VariantesComerciales.Remove(dup);
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    await _context.SaveChangesAsync();
                }

                _logger.LogInformation("Limpieza de duplicados completada. Se eliminaron {RemovedCount} registros repetidos.", removedCount);
                return Ok(new { Message = $"Se eliminaron {removedCount} variantes duplicadas.", RemovedCount = removedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al limpiar variantes duplicadas.");
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }

        [HttpDelete("variants/{id}")]
        [HttpPost("variants/{id}/delete")]
        [HttpPost("variants/delete/{id}")]
        public async Task<IActionResult> DeactivateVariant(int id)
        {
            // 1. Validar la API Key
            if (!Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey))
            {
                return Unauthorized("API Key no proporcionada.");
            }

            var configuredApiKey = _configuration["Scraping:ApiKey"] ?? "TeocuitlaDefaultApiKeySecret";
            if (extractedApiKey != configuredApiKey)
            {
                return Unauthorized("API Key inválida.");
            }

            try
            {
                var variant = await _context.VariantesComerciales.FindAsync(id);
                if (variant == null)
                {
                    return NotFound($"No se encontró la variante comercial con ID {id}.");
                }

                var cleanUrl = DataNormalizer.CleanProductUrl(variant.UrlProducto);

                var matchingVariants = await _context.VariantesComerciales
                    .Where(v => v.UrlProducto != null && v.UrlProducto != "")
                    .ToListAsync();

                var duplicates = matchingVariants
                    .Where(v => DataNormalizer.CleanProductUrl(v.UrlProducto).Equals(cleanUrl, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var v in duplicates)
                {
                    v.Activo = false;
                    _context.VariantesComerciales.Update(v);
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Variante '{Nombre}' (ID={Id}) y {Count} duplicados desactivados lógicamente.", variant.Nombre, id, duplicates.Count - 1);
                return Ok(new { Message = "Producto eliminado (desactivado lógicamente) del catálogo.", Id = id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al desactivar lógicamente la variante con ID {Id}.", id);
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }
    }
}
