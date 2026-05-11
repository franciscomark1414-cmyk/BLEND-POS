using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required]
        public string OrderNumber { get; set; } = string.Empty;
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public decimal TotalAmount { get; set; }
        [Required]
        public string Status { get; set; } = "PENDING_PAYMENT";

        // ✅ IDAGDAG ANG MGA RECEIPT FIELDS
        public string? ReceiptNumber { get; set; }
        public string? PaymentMethod { get; set; }
        public string? DiscountType { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }

        public virtual ICollection<OrderItem> Items { get; set; }

        public string? VoidReason { get; set; }
        public int? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
    }
}
