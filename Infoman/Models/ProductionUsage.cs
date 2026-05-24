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

        // ✅ IDAGDAG ITO - For Order relationship
        public int? OrderId { get; set; }

        // ✅ IDAGDAG ITO - For Product Size relationship
        public int? ProductSizeId { get; set; }

        // Navigation properties
        [ForeignKey("BatchId")]
        public virtual RawMaterialBatch Batch { get; set; }

        // ✅ IDAGDAG ITO - Navigation property for Order
        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; }

        // ✅ IDAGDAG ITO - Navigation property for ProductSize
        [ForeignKey("ProductSizeId")]
        public virtual ProductSize ProductSize { get; set; }
    }
}