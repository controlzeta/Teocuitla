using System.Collections.Generic;
using System.Threading.Tasks;
using Teocuitla.Shared.Dtos;

namespace Teocuitla.Web.Services
{
    public record BulkIngestResult(bool Success, int ProcessedCount, string Message);

    public interface IBulkIngestionService
    {
        /// <summary>
        /// Procesa de forma masiva y atómica una lista de capturas de precios usando transacciones y optimización por lote.
        /// </summary>
        Task<BulkIngestResult> ProcessBulkPriceIngestionAsync(List<IngestionItemDto> items);
    }
}
