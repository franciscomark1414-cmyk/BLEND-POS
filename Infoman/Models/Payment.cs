using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Payment
    {
        [Key]
        public int PaymentId { get; set; }

        public int OrderId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [StringLength(50)]
        public string PaymentMethod { get; set; } // GCash, Cash
        [StringLength(50)]
        public string PaymentReference { get; set; } // PayMongo session ID or reference
        [StringLength(50)]
        public string Status { get; set; } // PENDING, PAID, FAILED, CANCELLED
        public DateTime PaymentDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("OrderId")]
        public virtual Order Order { get; set; }
    }
}