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

        // ✅ BAGONG PROPERTY: Link sa bagong order (replacement order)
        public int? NewOrderId { get; set; }  // Nullable para sa backward compatibility

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

        // ✅ BAGONG PROPERTIES para sa mas magandang tracking
        [Column(TypeName = "decimal(18,2)")]
        public decimal OriginalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReplacementAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AdjustmentAmount { get; set; }

        public string? AdjustmentType { get; set; }  // 'ADDITIONAL_PAYMENT', 'CHANGE'

        public string? PaymentMethod { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal WasteCost { get; set; }

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public int CreatedBy { get; set; }

        // Navigation properties
        [ForeignKey("OriginalOrderId")]
        public virtual Order OriginalOrder { get; set; }

        [ForeignKey("NewOrderId")]
        public virtual Order NewOrder { get; set; }

        [ForeignKey("CreatedBy")]
        public virtual User CreatedByUser { get; set; }

        public virtual ICollection<ExchangeItem> ExchangeItems { get; set; }
    }
}