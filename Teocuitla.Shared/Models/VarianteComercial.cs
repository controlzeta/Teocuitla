using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    /// <summary>
    /// Representa la instancia específica de un producto en un catálogo o tienda web concreta,
    /// registrando su SKU local, URL directa, precios actuales y estado de stock.
    /// </summary>
    [Table("Variantes_Comerciales")]
    public class VarianteComercial
    {
        /// <summary>
        /// Identificador único autoincremental de la variante.
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Identificador foráneo hacia el producto maestro conceptual correspondiente.
        /// </summary>
        [Required]
        public int ProductoMaestroId { get; set; }

        /// <summary>
        /// Navegación al producto maestro global.
        /// </summary>
        [ForeignKey(nameof(ProductoMaestroId))]
        public ProductoMaestro? ProductoMaestro { get; set; }

        /// <summary>
        /// Identificador del sitio web o tienda donde se comercializa esta variante.
        /// </summary>
        [Required]
        public int CatalogoSitioId { get; set; }

        /// <summary>
        /// Navegación a la configuración del portal / sitio web.
        /// </summary>
        [ForeignKey(nameof(CatalogoSitioId))]
        public CatalogoSitio? CatalogoSitio { get; set; }

        /// <summary>
        /// Identificador de inventario o código de artículo propio de la tienda web.
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Sku { get; set; } = string.Empty;

        /// <summary>
        /// Nombre descriptivo del producto tal como se presenta en la tienda.
        /// </summary>
        [Required]
        [MaxLength(300)]
        public string Nombre { get; set; } = string.Empty;

        /// <summary>
        /// URL absoluta directa para acceder y rastrear la ficha de este producto.
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string UrlProducto { get; set; } = string.Empty;

        /// <summary>
        /// Precio vigente capturado en el último ciclo de monitoreo.
        /// </summary>
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? PrecioActual { get; set; }

        /// <summary>
        /// Precio registrado antes del último cambio detectado.
        /// </summary>
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? PrecioAnterior { get; set; }

        /// <summary>
        /// Indica si el producto se encuentra disponible para compra inmediata.
        /// </summary>
        public bool EnStock { get; set; }

        /// <summary>
        /// Fecha y hora en que se extrajo con éxito el último dato de precio o stock.
        /// </summary>
        public DateTime? UltimaActualizacion { get; set; }

        /// <summary>
        /// Contador de intentos de rastreo ejecutados en la jornada en curso.
        /// </summary>
        public int IntentosDiaActual { get; set; }

        /// <summary>
        /// Fecha y hora del último intento de rastreo (haya sido exitoso o fallido).
        /// </summary>
        public DateTime? FechaUltimoIntento { get; set; }

        /// <summary>
        /// URL de la imagen principal del producto.
        /// </summary>
        [MaxLength(1000)]
        public string? ImagenUrl { get; set; }

        /// <summary>
        /// Bandera de borrado lógico / estado operativo de la variante.
        /// </summary>
        public bool Activo { get; set; } = true;

        /// <summary>
        /// Colección histórica de cambios de precio de esta variante.
        /// </summary>
        public ICollection<HistorialPrecio> HistorialPrecios { get; set; } = new List<HistorialPrecio>();
    }
}
