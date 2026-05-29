using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class OrderItem
    {
        [Key]
        public int OrderItemId { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int ProductSizeId { get; set; }
        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ActualCost { get; set; }

        public bool IsExchanged { get; set; } = false;
        public int? QuantityExchanged { get; set; }
        public int? ExchangeTransactionId { get; set; }

        // ✅ NEW PROPERTIES - REPLACE OLD IsVoided/IsRefunded
        public int DispositionStatusId { get; set; } = 1; // Default = AVAILABLE (1)

        [StringLength(500)]
        public string? DispositionReason { get; set; }

        public DateTime? DispositionProcessedAt { get; set; }
        public int? DispositionProcessedBy { get; set; }

        // Optional: Keep old properties for backward compatibility (but mark as obsolete)
        [Obsolete("Use DispositionStatusId instead")]
        public bool IsVoided
        {
            get => DispositionStatusId == 2; // VOIDED = 2
            set { if (value) DispositionStatusId = 2; }
        }

        [Obsolete("Use DispositionStatusId instead")]
        public bool IsRefunded
        {
            get => DispositionStatusId == 3; // REFUNDED = 3
            set { if (value) DispositionStatusId = 3; }
        }

        [Obsolete("Use DispositionReason instead")]
        public string? VoidReason
        {
            get => DispositionReason;
            set => DispositionReason = value;
        }

        [Obsolete("Use DispositionReason instead")]
        public string? RefundReason
        {
            get => DispositionReason;
            set => DispositionReason = value;
        }

        // Navigation properties
        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; }

        [ForeignKey("ProductSizeId")]
        public virtual ProductSize ProductSize { get; set; }

        [ForeignKey("DispositionStatusId")]
        public virtual ItemDispositionStatus DispositionStatus { get; set; }

        [ForeignKey("DispositionProcessedBy")]
        public virtual User DispositionProcessedByUser { get; set; }

        
    }
}