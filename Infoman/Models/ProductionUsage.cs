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
        public int ProductId { get; set; }  // Mango shake ID

        [Required]
        [Range(0, double.MaxValue)]
        public decimal QuantityUsed { get; set; }

        [Required]
        public DateTime DateUsed { get; set; }

        [StringLength(200)]
        public string ReferenceNo { get; set; }  // Order # or Production #

        // Navigation property
        [ForeignKey("BatchId")]
        public virtual RawMaterialBatch Batch { get; set; }
    }
}
