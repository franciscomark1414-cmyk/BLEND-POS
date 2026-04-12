using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace Infoman.Models
{
    [Table("RawMaterialBatches")]
    public class RawMaterialBatch
    {
        
        
            [Key]
            public int BatchId { get; set; }

            [Required]
            public int RawMaterialId { get; set; }

            [Required]
            [Range(0, double.MaxValue)]
            public decimal QuantityReceived { get; set; }

            [Required]
            [Range(0, double.MaxValue)]
            public decimal QuantityRemaining { get; set; }

            [Required]
            public DateTime ReceivedDate { get; set; }

            public DateTime? ExpiryDate { get; set; }

            [Required]
            [Range(0, double.MaxValue)]
            public decimal UnitPrice { get; set; }

            // Navigation property
            [ForeignKey("RawMaterialId")]
            public virtual RawMaterial RawMaterial { get; set; }
        public virtual ICollection<ProductionUsage> ProductionUsages { get; set; }

        // ========== ADD CONSTRUCTOR TO INITIALIZE ==========
        public RawMaterialBatch()
        {
            ProductionUsages = new HashSet<ProductionUsage>();
        }
    }
}
