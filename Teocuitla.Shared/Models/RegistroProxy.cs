using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Teocuitla.Shared.Models
{
    [Table("Registro_Proxies")]
    public class RegistroProxy
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Ip { get; set; } = string.Empty;

        [Required]
        public int Puerto { get; set; }

        [MaxLength(100)]
        public string Usuario { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        public int LatenciaMs { get; set; }

        public int FallosAcumulados { get; set; }

        public DateTime? UltimoUso { get; set; }

        public bool Baneado { get; set; }
    }
}
