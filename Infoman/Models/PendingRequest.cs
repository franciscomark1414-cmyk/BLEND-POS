using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class PendingRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int OrderId { get; set; }
        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; }
        [StringLength(500)]

        public string? ItemIds { get; set; } // Comma-separated list of item IDs

        [Required]
        [StringLength(50)]
        public string RequestType { get; set; } // "VOID" or "REFUND" or "EXCHANGE_REFUND"

        [Required]
        [StringLength(500)]
        public string Reason { get; set; }

        [Required]
        public int RequestedBy { get; set; }
        [ForeignKey("RequestedBy")]
        public virtual User Requester { get; set; }
        [StringLength(50)]

        public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED

        public DateTime RequestedAt { get; set; } = DateTime.Now;

        public int? ApprovedBy { get; set; }
        [ForeignKey("ApprovedBy")]
        public virtual User Approver { get; set; }

        public DateTime? ApprovedAt { get; set; }

        [StringLength(200)]
        public string? RejectionReason { get; set; }
    }
}