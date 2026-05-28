// Models/VoidLog.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class VoidLog
    {
        [Key]
        public int VoidLogId { get; set; }

        public int? TransactionId { get; set; }
        [ForeignKey("TransactionId")]
        public virtual Order Transaction { get; set; }

        public int? ItemId { get; set; }
        [ForeignKey("ItemId")]
        public virtual OrderItem Item { get; set; }

        [Required]
        [StringLength(50)]
        public string VoidType { get; set; } // "item" or "transaction"

        [Required]
        [StringLength(500)]
        public string Reason { get; set; }

        public bool PreparedFlag { get; set; }

        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        public int? ApprovedBy { get; set; }
        [ForeignKey("ApprovedBy")]
        public virtual User Approver { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string PreparedStatus { get; set; } // "NOT_PREPARED", "PREPARED"
        [Column(TypeName = "decimal(18,2)")]
        public decimal RefundAmount { get; set; } = 0;

        public bool IsRefundProcessed { get; set; } = false;
    }
}