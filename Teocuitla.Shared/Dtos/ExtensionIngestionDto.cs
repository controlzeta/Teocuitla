using System;

namespace Teocuitla.Shared.Dtos
{
    public class ExtensionIngestionDto
    {
        public string Sku { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string UrlProducto { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public string? ImagenUrl { get; set; }
        public string Dominio { get; set; } = string.Empty;
        public string Marca { get; set; } = "Genérica";
        
        // Campos de auto-aprendizaje de selectores
        public string? SelectorNombreXPath { get; set; }
        public string? SelectorPrecioXPath { get; set; }
        public string? SelectorImagenXPath { get; set; }
    }
}
