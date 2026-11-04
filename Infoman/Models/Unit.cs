using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class Unit
    {
        [Key]
        public int UnitId { get; set; }

        [Required]
        [StringLength(50)]
        public string Name { get; set; } // e.g., kilogram, gram, pieces

        [Required]
        [StringLength(10)]
        public string Symbol { get; set; } // e.g., kg, g, pcs

        [Required]
        [StringLength(20)]
        public string BaseUnit { get; set; } // e.g., g, mL, pcs

        [Required]
        public decimal ConversionFactor { get; set; } = 1; // e.g., 1000 (kung kg to g)

        public bool IsActive { get; set; } = true;
    }
}