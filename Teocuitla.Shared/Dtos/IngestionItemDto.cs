using System;

namespace Teocuitla.Shared.Dtos
{
    public class IngestionItemDto
    {
        public int VarianteComercialId { get; set; }
        public decimal Precio { get; set; }
        public bool EnStock { get; set; }
        public DateTime FechaCaptura { get; set; }
    }
}
