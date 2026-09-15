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
using Serilog.Context;
using Teocuitla.Shared.Dtos;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Models;
using Teocuitla.Shared.Helpers;
using Teocuitla.Web.Filters;
using Teocuitla.Web.Services;

namespace Teocuitla.Web.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiKeyAuth]
    public class IngestionController : ControllerBase
    {
        private readonly TeocuitlaDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IngestionController> _logger;
        private readonly IngestionNotificationService _notificationService;
        private readonly IBulkIngestionService _bulkIngestionService;

        public IngestionController(
            TeocuitlaDbContext context,
            IConfiguration configuration,
            ILogger<IngestionController> logger,
            IngestionNotificationService notificationService,
            IBulkIngestionService bulkIngestionService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _notificationService = notificationService;
            _bulkIngestionService = bulkIngestionService;
        }

        [HttpPost("bulk")]
        public async Task<IActionResult> BulkIngest([FromBody] List<IngestionItemDto> items)
        {
            if (items == null || items.Count == 0)
            {
                return BadRequest(new ProblemDetails
                {
                    Status = 400,
                    Title = "Bad Request",
                    Detail = "El lote de datos está vacío."
                });
            }

            try
            {
                var result = await _bulkIngestionService.ProcessBulkPriceIngestionAsync(items);
                return Ok(new { Message = result.Message, ProcessedCount = result.ProcessedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error durante la ingesta masiva de precios.");
                return StatusCode(500, new ProblemDetails
                {
                    Status = 500,
                    Title = "Internal Server Error",
                    Detail = $"Error interno al procesar el lote: {ex.Message}"
                });
            }
        }

        [HttpPost("extension")]
        public async Task<IActionResult> IngestFromExtension([FromBody] ExtensionIngestionDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Sku) || string.IsNullOrWhiteSpace(dto.UrlProducto))
            {
                return BadRequest("Datos de producto inválidos.");
            }

            dto.Sku = DataNormalizer.NormalizeSku(dto.Sku);
            dto.Marca = DataNormalizer.NormalizeBrand(dto.Marca);
            dto.UrlProducto = DataNormalizer.CleanProductUrl(dto.UrlProducto);
            dto.ImagenUrl = DataNormalizer.NormalizeImageUrl(DataNormalizer.MakeAbsoluteUrl(dto.ImagenUrl, dto.UrlProducto));

            // Enriquecer el contexto de Serilog para que cualquier log o excepción contenga estas propiedades estructuradas
            using (LogContext.PushProperty("ProductSKU", dto.Sku))
            using (LogContext.PushProperty("UrlProducto", dto.UrlProducto))
            using (LogContext.PushProperty("Dominio", dto.Dominio))
            {
                _logger.LogInformation("Recibiendo ingesta de producto desde extensión: {@Dto}", new
                {
                    dto.Sku,
                    dto.Nombre,
                    dto.UrlProducto,
                    dto.Precio,
                    dto.Dominio,
                    dto.Marca,
                    dto.ImagenUrl,
                    dto.SelectorNombreXPath,
                    dto.SelectorPrecioXPath,
                    dto.SelectorImagenXPath
                });

                try
                {
                    // Truncar preventivamente para evitar SqlException por longitud máxima de columnas de base de datos
                    var safeNombre = dto.Nombre ?? string.Empty;
                    if (safeNombre.Length > 300) safeNombre = safeNombre.Substring(0, 300);

                    var safeSku = dto.Sku ?? string.Empty;
                    if (safeSku.Length > 100) safeSku = safeSku.Substring(0, 100);

                    var safeMarca = dto.Marca ?? "Genérica";
                    if (safeMarca.Length > 100) safeMarca = safeMarca.Substring(0, 100);

                    var safeUrl = dto.UrlProducto ?? string.Empty;
                    if (safeUrl.Length > 1000) safeUrl = safeUrl.Substring(0, 1000);

                    var safeImagen = dto.ImagenUrl;
                    if (safeImagen != null && safeImagen.Length > 1000) safeImagen = safeImagen.Substring(0, 1000);

                    using (var transaction = await _context.Database.BeginTransactionAsync())
                    {
                        // 1. Resolver el sitio de catálogo
                        var baseDomain = dto.Dominio.ToLower().Trim();
                        var site = await _context.CatalogoSitios
                            .Where(s => s.UrlBase != null && s.UrlBase.Contains(baseDomain))
                            .OrderBy(s => s.Id)
                            .FirstOrDefaultAsync();

                        if (site == null)
                        {
                            _logger.LogWarning("La extensión intentó enviar datos para un sitio no registrado en la base de datos: Dominio={Dominio}, Sku={Sku}, Url={Url}", baseDomain, dto.Sku, dto.UrlProducto);
                            return BadRequest($"El sitio con dominio '{baseDomain}' no existe en la base de datos.");
                        }

                        // Auto-aprendizaje/corrección de selectores a partir de la extensión
                        bool siteUpdated = false;
                        if (!string.IsNullOrEmpty(dto.SelectorNombreXPath) && site.SelectorNombreXPath != dto.SelectorNombreXPath)
                        {
                            site.SelectorNombreXPath = dto.SelectorNombreXPath.Length > 500 ? dto.SelectorNombreXPath.Substring(0, 500) : dto.SelectorNombreXPath;
                            siteUpdated = true;
                        }
                        if (!string.IsNullOrEmpty(dto.SelectorPrecioXPath) && site.SelectorPrecioXPath != dto.SelectorPrecioXPath)
                        {
                            site.SelectorPrecioXPath = dto.SelectorPrecioXPath.Length > 500 ? dto.SelectorPrecioXPath.Substring(0, 500) : dto.SelectorPrecioXPath;
                            siteUpdated = true;
                        }
                        if (!string.IsNullOrEmpty(dto.SelectorImagenXPath) && site.SelectorImagenXPath != dto.SelectorImagenXPath)
                        {
                            site.SelectorImagenXPath = dto.SelectorImagenXPath.Length > 500 ? dto.SelectorImagenXPath.Substring(0, 500) : dto.SelectorImagenXPath;
                            siteUpdated = true;
                        }

                        if (siteUpdated)
                        {
                            _context.CatalogoSitios.Update(site);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Selectores corregidos/actualizados automáticamente para el sitio {Nombre} desde la extensión.", site.Nombre);
                        }

                        // 2. Buscar variante comercial existente por URL limpia, SKU o coincidencia de URL
                        var cleanDtoUrl = safeUrl;
                        var cleanDtoSku = safeSku;

                        var allVariantsInSite = await _context.VariantesComerciales
                            .Where(v => v.CatalogoSitioId == site.Id)
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
                                if (!string.IsNullOrEmpty(safeImagen) && string.IsNullOrEmpty(mVar.ImagenUrl))
                                {
                                    mVar.ImagenUrl = safeImagen;
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
                                .Where(p => p.Nombre == safeNombre && p.Marca == safeMarca)
                                .OrderBy(p => p.Id)
                                .FirstOrDefaultAsync();

                            if (masterProduct == null)
                            {
                                masterProduct = new ProductoMaestro
                                {
                                    Nombre = safeNombre.Length > 200 ? safeNombre.Substring(0, 200) : safeNombre,
                                    Marca = safeMarca,
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
                                Sku = safeSku,
                                Nombre = safeNombre,
                                UrlProducto = cleanDtoUrl,
                                PrecioActual = dto.Precio,
                                EnStock = dto.Precio > 0,
                                UltimaActualizacion = DateTime.Now,
                                ImagenUrl = safeImagen
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
                    _logger.LogError(ex, "Error al procesar ingesta desde extensión. Datos recibidos: SKU={Sku}, Nombre={Nombre}, Url={Url}, Precio={Precio}, Dominio={Dominio}, Detalle={Error}", 
                        dto.Sku, dto.Nombre, dto.UrlProducto, dto.Precio, dto.Dominio, ex.Message);
                    return StatusCode(500, $"Error interno: {ex.Message}");
                }
            }
        }


        [HttpGet("sites")]
        public async Task<IActionResult> GetConfiguredSites()
        {
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
