using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Registro_Fallas_Scraping")]
    public class RegistroFallaScraping
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int VarianteComercialId { get; set; }

        [ForeignKey(nameof(VarianteComercialId))]
        public VarianteComercial? VarianteComercial { get; set; }

        [Required]
        public DateTime FechaFalla { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(1000)]
        public string UrlProducto { get; set; } = string.Empty;

        [Required]
        public string ErrorMensaje { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ProxyUtilizado { get; set; }

        [MaxLength(50)]
        public string? EstrategiaEvasion { get; set; }

        [Required]
        public string HtmlContenido { get; set; } = string.Empty;
    }
}
