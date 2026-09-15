using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Teocuitla.Shared.Data;
using Teocuitla.Shared.Dtos;

namespace Teocuitla.Web.Services
{
    public class BulkIngestionService : IBulkIngestionService
    {
        private readonly TeocuitlaDbContext _context;
        private readonly ILogger<BulkIngestionService> _logger;

        public BulkIngestionService(TeocuitlaDbContext context, ILogger<BulkIngestionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<BulkIngestResult> ProcessBulkPriceIngestionAsync(List<IngestionItemDto> items)
        {
            if (items == null || items.Count == 0)
            {
                return new BulkIngestResult(false, 0, "El lote de datos está vacío.");
            }

            _logger.LogInformation("Iniciando ingesta masiva de {Count} registros de precios en BulkIngestionService.", items.Count);

            var dbConn = _context.Database.GetDbConnection();
            if (dbConn is not SqlConnection sqlConnection)
            {
                throw new InvalidOperationException("El proveedor de base de datos actual no es Microsoft SQL Server o la conexión no es SqlConnection.");
            }

            if (sqlConnection.State != ConnectionState.Open)
            {
                await sqlConnection.OpenAsync();
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            var sqlTransaction = (SqlTransaction)transaction.GetDbTransaction();

            try
            {
                // 1. Crear tabla temporal en la sesión
                using (var createTempTableCmd = sqlConnection.CreateCommand())
                {
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
                }

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

                // 3. Ejecutar SqlBulkCopy
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

                // 4. Lógica de inserción/actualización en lote
                using (var mergeCmd = sqlConnection.CreateCommand())
                {
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

                    await mergeCmd.ExecuteNonQueryAsync();
                }

                // 5. Eliminar la tabla temporal
                using (var dropTempTableCmd = sqlConnection.CreateCommand())
                {
                    dropTempTableCmd.Transaction = sqlTransaction;
                    dropTempTableCmd.CommandText = "DROP TABLE #TempIngest;";
                    await dropTempTableCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                _logger.LogInformation("Lote de {Count} registros de precios persistido con éxito en SQL Server.", items.Count);

                return new BulkIngestResult(true, items.Count, "Lote procesado exitosamente.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción durante la ejecución de ProcessBulkPriceIngestionAsync.");
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
