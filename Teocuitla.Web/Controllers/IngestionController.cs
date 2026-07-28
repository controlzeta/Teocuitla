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

        public IngestionController(
            TeocuitlaDbContext context,
            IConfiguration configuration,
            ILogger<IngestionController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
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
                            V.ImagenUrl = COALESCE(T.ImagenUrl, V.ImagenUrl)
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
            dto.ImagenUrl = DataNormalizer.NormalizeImageUrl(DataNormalizer.MakeAbsoluteUrl(dto.ImagenUrl, dto.UrlProducto));

            _logger.LogInformation("Recibiendo ingesta de producto desde extensión: SKU={Sku}, Dominio={Dominio}", dto.Sku, dto.Dominio);

            try
            {
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    // 1. Resolver o crear el sitio de catálogo
                    var baseDomain = dto.Dominio.ToLower().Trim();
                    var site = await _context.CatalogoSitios
                        .FirstOrDefaultAsync(s => s.UrlBase != null && s.UrlBase.Contains(baseDomain));

                    if (site == null)
                    {
                        site = new CatalogoSitio
                        {
                            Nombre = baseDomain,
                            UrlBase = dto.UrlProducto.StartsWith("https") ? $"https://{baseDomain}" : $"http://{baseDomain}",
                            EstrategiaEvasion = "Standard"
                        };
                        _context.CatalogoSitios.Add(site);
                        await _context.SaveChangesAsync();
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

                    // 2. Buscar variante comercial existente por SKU y Sitio
                    var variant = await _context.VariantesComerciales
                        .FirstOrDefaultAsync(v => v.Sku == dto.Sku && v.CatalogoSitioId == site.Id);

                    bool priceChanged = false;

                    if (variant != null)
                    {
                        priceChanged = variant.PrecioActual != dto.Precio || variant.EnStock != (dto.Precio > 0);

                        // Actualizar variante
                        variant.PrecioAnterior = variant.PrecioActual;
                        variant.PrecioActual = dto.Precio;
                        variant.EnStock = dto.Precio > 0;
                        variant.UltimaActualizacion = DateTime.Now;
                        if (!string.IsNullOrEmpty(dto.ImagenUrl))
                        {
                            variant.ImagenUrl = dto.ImagenUrl;
                        }
                        
                        _context.VariantesComerciales.Update(variant);
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        priceChanged = true;

                        // Buscar si existe un producto maestro por nombre y marca
                        var masterProduct = await _context.ProductosMaestros
                            .FirstOrDefaultAsync(p => p.Nombre == dto.Nombre && p.Marca == dto.Marca);

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
                            UrlProducto = dto.UrlProducto,
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
                var variants = await _context.VariantesComerciales
                    .Include(v => v.CatalogoSitio)
                    .Where(v => v.UltimaActualizacion == null || 
                                (v.UltimaActualizacion < today && 
                                 v.CatalogoSitio != null && 
                                 v.UltimaActualizacion < now.AddMinutes(-v.CatalogoSitio.IntervaloMinutos)))
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
                    .Take(50)
                    .ToListAsync();
                return Ok(variants);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener la lista de variantes para la extensión.");
                return StatusCode(500, $"Error interno: {ex.Message}");
            }
        }
    }
}
