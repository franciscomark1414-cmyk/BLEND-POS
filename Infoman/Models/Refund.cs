// Models/Refund.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Refund
    {
        [Key]
        public int RefundId { get; set; }

        [Required]
        public int TransactionId { get; set; }
        [ForeignKey("TransactionId")]
        public virtual Order Transaction { get; set; }

        public int? ItemId { get; set; }

        public decimal Amount { get; set; }

        [Required]
        public string Reason { get; set; }

        [Required]
        public int ProcessedBy { get; set; }

        [Required]
        public int ApprovedBy { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string RefundReference { get; set; }
    }
}