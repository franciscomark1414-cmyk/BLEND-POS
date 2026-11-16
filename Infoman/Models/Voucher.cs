using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Voucher
    {
        [Key]
        public int VoucherId { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        public decimal DiscountPercent { get; set; }

        [StringLength(100)]
        public string? EventName { get; set; } // "Others" o "Specify" text

        public DateTime? ExpiryDate { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsUsed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}