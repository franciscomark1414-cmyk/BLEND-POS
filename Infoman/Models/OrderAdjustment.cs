using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class OrderAdjustment
    {
        [Key]
        public int AdjustmentId { get; set; }

        public int OriginalOrderId { get; set; }

        [Required]
        [StringLength(50)]
        public string AdjustmentType { get; set; }  // "REFUND", "EXCHANGE_PAYMENT", "EXCHANGE_REFUND"

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(500)]
        public string Reason { get; set; }

        [Required]
        [StringLength(50)]
        public string ReferenceNumber { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "PENDING";

        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        [StringLength(500)]
        public string? RejectionReason { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int CreatedBy { get; set; }

        // Navigation properties
        [ForeignKey("OriginalOrderId")]
        public virtual Order OriginalOrder { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }

        [ForeignKey("ApprovedBy")]
        public virtual User ApprovedByUser { get; set; }
    }
}