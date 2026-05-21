using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class ExchangeItem
    {
        [Key]
        public int ExchangeItemId { get; set; }
        public int ExchangeId { get; set; }
        public int OriginalOrderItemId { get; set; }
        public int ReplacementProductSizeId { get; set; }
        public int QuantityReturned { get; set; }
        public int QuantityReplaced { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OriginalPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReplacementPrice { get; set; }

        public DateTime CreatedAt { get; set; }

        // Navigation properties
        [ForeignKey("ExchangeId")]
        public virtual ExchangeTransaction ExchangeTransaction { get; set; }

        [ForeignKey("OriginalOrderItemId")]
        public virtual OrderItem OriginalOrderItem { get; set; }

        [ForeignKey("ReplacementProductSizeId")]
        public virtual ProductSize ReplacementProductSize { get; set; }
    }
}