using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Variantes_Comerciales")]
    public class VarianteComercial
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ProductoMaestroId { get; set; }

        [ForeignKey(nameof(ProductoMaestroId))]
        public ProductoMaestro? ProductoMaestro { get; set; }

        [Required]
        public int CatalogoSitioId { get; set; }

        [ForeignKey(nameof(CatalogoSitioId))]
        public CatalogoSitio? CatalogoSitio { get; set; }

        [Required]
        [MaxLength(100)]
        public string Sku { get; set; } = string.Empty;

        [Required]
        [MaxLength(300)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(1000)]
        public string UrlProducto { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? PrecioActual { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? PrecioAnterior { get; set; }

        public bool EnStock { get; set; }

        public DateTime? UltimaActualizacion { get; set; }

        public int IntentosDiaActual { get; set; }

        public DateTime? FechaUltimoIntento { get; set; }

        public ICollection<HistorialPrecio> HistorialPrecios { get; set; } = new List<HistorialPrecio>();
    }
}
