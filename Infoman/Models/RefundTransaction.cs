using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class RefundTransaction
    {
        [Key]
        public int RefundId { get; set; }
        public int AdjustmentId { get; set; }
        public int OriginalOrderId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        [StringLength(500)]
        public string Reason { get; set; }
        public int ProcessedBy { get; set; }
        [StringLength(100)]
        public string RefundReference { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation properties
        [ForeignKey("AdjustmentId")]
        public virtual OrderAdjustment Adjustment { get; set; }

        [ForeignKey("OriginalOrderId")]
        public virtual Order OriginalOrder { get; set; }

        [ForeignKey("ProcessedBy")]
        public virtual User ProcessedByUser { get; set; }
    }
}