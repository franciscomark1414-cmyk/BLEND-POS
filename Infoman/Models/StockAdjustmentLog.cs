
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{

    public class StockAdjustmentLog
    {
        [Key]
        public int AdjustmentId { get; set; }

        [Required]
        public int BatchId { get; set; }

        [Required]
        public decimal OldQuantity { get; set; }

        [Required]
        public decimal NewQuantity { get; set; }

        [Required]
        [StringLength(200)]
        public string Reason { get; set; }

        public int AdjustedBy { get; set; }
        public DateTime AdjustedAt { get; set; }

        [StringLength(500)]
        public string Notes { get; set; }

        [ForeignKey("BatchId")]
        public virtual RawMaterialBatch Batch { get; set; }

        [ForeignKey("AdjustedBy")]
        public virtual User AdjustedByUser { get; set; }
    }
}