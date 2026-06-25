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
                        -- Insertar registros en el historial histórico de precios
                        INSERT INTO Historial_Precios (VarianteComercialId, Precio, EnStock, FechaCaptura)
                        SELECT VarianteId, Precio, EnStock, Fecha 
                        FROM #TempIngest;

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
    }
}
