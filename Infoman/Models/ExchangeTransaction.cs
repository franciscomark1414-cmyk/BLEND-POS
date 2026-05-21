using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class ExchangeTransaction
    {
        [Key]
        public int ExchangeId { get; set; }

        public int OriginalOrderId { get; set; }

        public DateTime ExchangeDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalReturnedValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalReplacementValue { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceDifference { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AdditionalPayment { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PartialRefund { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int CreatedBy { get; set; }

        // Navigation properties
        [ForeignKey("OriginalOrderId")]
        public virtual Order OriginalOrder { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }

        public virtual ICollection<ExchangeItem> ExchangeItems { get; set; }
    }
}