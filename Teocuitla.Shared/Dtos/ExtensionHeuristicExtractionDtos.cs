namespace Teocuitla.Shared.Dtos
{
    public class ExtensionHeuristicExtractionRequestDto
    {
        public string UrlProducto { get; set; } = string.Empty;
        public string Html { get; set; } = string.Empty;
    }

    public class ExtensionHeuristicExtractionResultDto
    {
        public string? Sku { get; set; }
        public string? Nombre { get; set; }
        public decimal? Precio { get; set; }
        public bool EnStock { get; set; }
        public string? ImagenUrl { get; set; }
        public string? Marca { get; set; }
        public string? SelectorPrecioXPath { get; set; }
        public string? FuentePrecio { get; set; }
        public string? Moneda { get; set; }
        public string MetodoDeteccion { get; set; } = "Ninguno";
        public int ConfianzaPrecio { get; set; }
    }
}
