// ============================================================
// FILE: Models/AddOn.cs
// ============================================================
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class AddOn
    {
        [Key]
        public int AddOnId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(50)]
        public string? SKU { get; set; }

        [StringLength(50)]
        public string? Category { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal Margin { get; set; } = 0.3000m;

        public bool IsManualPrice { get; set; } = true;

        public int MaxPerOrder { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<AddOnRecipeItem> RecipeItems { get; set; } = new List<AddOnRecipeItem>();
    }
}