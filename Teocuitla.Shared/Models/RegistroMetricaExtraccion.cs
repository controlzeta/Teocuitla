using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Registro_Metricas_Extraccion")]
    public class RegistroMetricaExtraccion
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int CatalogoSitioId { get; set; }

        [Required]
        public int VarianteComercialId { get; set; }

        public bool Exitoso { get; set; }

        [MaxLength(100)]
        public string? MetodoDeteccion { get; set; }

        [MaxLength(100)]
        public string? FuentePrecio { get; set; }

        public int ConfianzaPrecio { get; set; }
        public int LatenciaMs { get; set; }
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    }
}
