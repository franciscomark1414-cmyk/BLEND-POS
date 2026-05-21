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

        public string Status { get; set; } // PENDING, PREPARING, COMPLETED, VOIDED, REFUNDED
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

        // Navigation properties
        public virtual ICollection<OrderItem> Items { get; set; }
        public virtual ICollection<OrderAdjustment> Adjustments { get; set; }

        // Computed property - never stored in DB
        [NotMapped]
        public decimal NetRevenue
        {
            get
            {
                if (Status == "VOIDED" || Status == "REFUNDED")
                    return 0;

                decimal netRevenue = OriginalTotalAmount;

                if (Adjustments != null)
                {
                    foreach (var adj in Adjustments)
                    {
                        if (adj.Status == "APPROVED" || adj.Status == "COMPLETED")
                        {
                            // ✅ Use string comparison, not int IDs
                            if (adj.AdjustmentType == "REFUND" ||
                                adj.AdjustmentType == "EXCHANGE_REFUND")
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