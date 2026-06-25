using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Catalogo_Sitios")]
    public class CatalogoSitio
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string UrlBase { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        public int IntervaloMinutos { get; set; } = 360; // 6 horas por defecto

        [MaxLength(500)]
        public string SelectorProductoXPath { get; set; } = string.Empty;

        [MaxLength(500)]
        public string SelectorPrecioXPath { get; set; } = string.Empty;

        [MaxLength(500)]
        public string SelectorStockXPath { get; set; } = string.Empty;

        [MaxLength(500)]
        public string SelectorNombreXPath { get; set; } = string.Empty;

        [MaxLength(500)]
        public string SelectorImagenXPath { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string EstrategiaEvasion { get; set; } = "Standard"; // Standard, Cloudflare, Heavy-JS, etc.

        public DateTime? UltimoRastreo { get; set; }
    }
}
