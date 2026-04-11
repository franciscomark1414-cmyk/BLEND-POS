using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string SKU { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        // Original price? Optional — kung may base price ka
        public decimal Price { get; set; }

        // Total stock across all sizes
        public int Stock { get; set; }

        // Optional: image path
        public string ImagePath { get; set; } = string.Empty;

        // Navigation property — CONNECTION TO SIZES
        public virtual ICollection<ProductSize> Sizes { get; set; } = new List<ProductSize>();

        // FOR PROFIT MARGIN (temporary, not saved to database)
        [NotMapped]
        public string SelectedProfitMargin { get; set; }
    }
}