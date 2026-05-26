using Infoman.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Order
{
    [Key]
    public int OrderId { get; set; }
    public string OrderNumber { get; set; }
    public DateTime Date { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OriginalTotalAmount { get; set; }

    public string Status { get; set; }
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

    // ✅ ADD THIS - Staff who processed the order
    public int? StaffUserId { get; set; }  // ← IDAGDAG ITO

    // Exchange properties
    public int? OriginalOrderId { get; set; }
    public int? ExchangeTransactionId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StoreCreditApplied { get; set; } = 0;

    [Column(TypeName = "decimal(18,2)")]
    public decimal AdjustmentAmount { get; set; } = 0;

    public string? AdjustmentType { get; set; }
    public string? ReturnStatus { get; set; } = "ACTIVE";
    public DateTime? ExchangeDate { get; set; }
    public string? ExchangeReason { get; set; }

    // Navigation properties
    public virtual ICollection<OrderItem> Items { get; set; }
    public virtual ICollection<OrderAdjustment> Adjustments { get; set; }

    [ForeignKey("OriginalOrderId")]
    public virtual Order? OriginalOrder { get; set; }
    public virtual ICollection<Order>? ReplacementOrders { get; set; }

    // ✅ ADD THIS - Navigation property for Staff
    [ForeignKey("StaffUserId")]
    public virtual User? Staff { get; set; }

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