using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Historial_Precios")]
    public class HistorialPrecio
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public int VarianteComercialId { get; set; }

        [ForeignKey(nameof(VarianteComercialId))]
        public VarianteComercial? VarianteComercial { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Precio { get; set; }

        [Required]
        public bool EnStock { get; set; }

        [Required]
        public DateTime FechaCaptura { get; set; } = DateTime.UtcNow;
    }
}
