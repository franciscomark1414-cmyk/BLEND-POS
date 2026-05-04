using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class RecipeItem
    {
        [Key]
        public int RecipeItemId { get; set; }

        [Required]
        public int ProductSizeId { get; set; }

        [ForeignKey("ProductSizeId")]
        public virtual ProductSize ProductSize { get; set; }

        [Required]
        public int RawMaterialId { get; set; }

        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }

        [Required]
        public decimal Quantity { get; set; } // e.g., 150g, 200ml
        [Required]
        public string UnitUsed { get; set; } = "g";
    }
}