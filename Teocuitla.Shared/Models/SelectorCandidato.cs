using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Selectores_Candidatos")]
    public class SelectorCandidato
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CatalogoSitioId { get; set; }

        [Required]
        public int VarianteComercialId { get; set; }

        [Required]
        [MaxLength(500)]
        public string XPath { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal PrecioVerificado { get; set; }

        [Required]
        [MaxLength(1000)]
        public string UrlProducto { get; set; } = string.Empty;

        public DateTime FechaValidacion { get; set; } = DateTime.UtcNow;
    }
}
