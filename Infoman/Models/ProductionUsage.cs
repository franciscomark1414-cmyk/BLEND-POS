using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("ProductionUsages")]
    public class ProductionUsage
    {
        [Key]
        public int UsageId { get; set; }

        [Required]
        public int BatchId { get; set; }

        [Required]
        public int ProductId { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal QuantityUsed { get; set; }

        [Required]
        public DateTime DateUsed { get; set; }

        [StringLength(200)]
        public string ReferenceNo { get; set; }

        public int? OrderId { get; set; }

        public int? ProductSizeId { get; set; }

        // ✅ BAGO: snapshot ng cost na galing sa batch sa oras ng deduction
        // CostPerCostUnit = ₱ per kg / L / pcs (ayon sa UnitPrice ng batch)
        // Nullable para hindi masira ang existing rows
        [Column(TypeName = "decimal(18,4)")]
        public decimal? CostPerCostUnit { get; set; }

        // ✅ BAGO: total cost ng usage na ito (QuantityUsed sa cost unit x CostPerCostUnit)
        [Column(TypeName = "decimal(18,2)")]
        public decimal? TotalCost { get; set; }

        // Navigation properties
        [ForeignKey("BatchId")]
        public virtual RawMaterialBatch Batch { get; set; }

        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; }

        [ForeignKey("ProductSizeId")]
        public virtual ProductSize ProductSize { get; set; }
    }
}