// ============================================================
// FILE: Models/AddOnRecipeItem.cs
// ============================================================
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class AddOnRecipeItem
    {
        [Key]
        public int AddOnRecipeItemId { get; set; }

        public int AddOnId { get; set; }

        public int RawMaterialId { get; set; }

        /// <summary>
        /// Quantity sa BASE UNIT (g / mL / pcs)
        /// </summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        /// <summary>
        /// Base unit na (g / mL / pcs) — same sa RecipeItem
        /// </summary>
        [Required]
        [StringLength(10)]
        public string UnitUsed { get; set; }

        public bool IsDeleted { get; set; } = false;

        // Navigation
        [ForeignKey(nameof(AddOnId))]
        public AddOn? AddOn { get; set; }

        [ForeignKey(nameof(RawMaterialId))]
        public RawMaterial? RawMaterial { get; set; }
    }
}