using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Infoman.Models
{
    public class ProductSize
    {
        [Key]
        public int ProductSizeId { get; set; }

        [Required]
        public string SizeName { get; set; } = string.Empty;    // Small, Medium, Large

        [Required]
        public decimal Price { get; set; } // Selling price per size (after profit margin)

        [Required]
        public int Stock { get; set; } // Stock per size
        public decimal Margin { get; set; } = 0.3m;
        public bool IsAvailableInPOS { get; set; } = false;

        [Required]
        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; }

        public virtual ICollection<RecipeItem> RecipeItems { get; set; } = new List<RecipeItem>();

        // Computed properties (not saved to database)
        [NotMapped]
        public decimal TotalRecipeCost => RecipeItems != null ?
            RecipeItems.Sum(r => r.Quantity * r.RawMaterial.Price) : 0;

        [NotMapped]
        public decimal Profit => Price - TotalRecipeCost;

        [NotMapped]
        public bool RecipeComplete => RecipeItems != null && RecipeItems.Count > 0;
    }
}