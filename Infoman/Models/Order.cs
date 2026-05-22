using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public DateTime Date { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; } // This can change (e.g., after void)

        [Column(TypeName = "decimal(18,2)")]
        public decimal OriginalTotalAmount { get; set; } // IMMUTABLE - never changes

        public string Status { get; set; } // PENDING, PREPARING, COMPLETED, VOIDED, REFUNDED, EXCHANGED
        public string PaymentMethod { get; set; }
        public string DiscountType { get; set; }
        public decimal DiscountPercent { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        public string ReceiptNumber { get; set; }
        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }

        public bool HasPendingRefund { get; set; } = false;

        // Audit fields
        public DateTime CreatedAt { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? UpdatedBy { get; set; }

        // ✅ BAGONG PROPERTIES PARA SA EXCHANGE AT STORE CREDIT
        public int? OriginalOrderId { get; set; }           // Kung ito ay replacement order (e.g., Order #17 ito, ang Original ay #16)
        public int? ExchangeTransactionId { get; set; }     // Link sa ExchangeTransaction (optional)

        [Column(TypeName = "decimal(18,2)")]
        public decimal StoreCreditApplied { get; set; } = 0;  // ₱19.00 na na-apply mula sa original order

        [Column(TypeName = "decimal(18,2)")]
        public decimal AdjustmentAmount { get; set; } = 0;    // +₱6.00 (dagdag bayad) or -₱4.00 (sobra)

        public string? AdjustmentType { get; set; }           // 'ADDITIONAL_PAYMENT', 'CHANGE'

        public string? ReturnStatus { get; set; } = "ACTIVE"; // 'ACTIVE', 'EXCHANGED', 'RETURNED', 'REFUNDED'
        public DateTime? ExchangeDate { get; set; }
        public string? ExchangeReason { get; set; }

        // Navigation properties
        public virtual ICollection<OrderItem> Items { get; set; }
        public virtual ICollection<OrderAdjustment> Adjustments { get; set; }

        // ✅ Self-reference para makuha ang original order
        [ForeignKey("OriginalOrderId")]
        public virtual Order? OriginalOrder { get; set; }

        // ✅ Para makuha ang mga replacement orders (kung marami)
        public virtual ICollection<Order>? ReplacementOrders { get; set; }

        // Computed property - never stored in DB
        [NotMapped]
        public decimal NetRevenue
        {
            get
            {
                if (Status == "VOIDED" || Status == "REFUNDED" || Status == "EXCHANGED")
                    return 0;

                decimal netRevenue = OriginalTotalAmount;

                if (Adjustments != null)
                {
                    foreach (var adj in Adjustments)
                    {
                        if (adj.Status == "APPROVED" || adj.Status == "COMPLETED")
                        {
                            if (adj.AdjustmentType == "REFUND" || adj.AdjustmentType == "EXCHANGE_REFUND")
                            {
                                netRevenue -= adj.Amount;
                            }
                            else if (adj.AdjustmentType == "EXCHANGE_PAYMENT")
                            {
                                netRevenue += adj.Amount;
                            }
                        }
                    }
                }

                return netRevenue > 0 ? netRevenue : 0;
            }
        }
    }
}