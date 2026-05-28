using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class SupplierReturn
    {
        [Key]
        public int ReturnId { get; set; }

        [Required]
        [StringLength(100)]
        public string ReturnNumber { get; set; }

        public int ReceivingId { get; set; }
        public int ReceivingItemId { get; set; }
        public int SupplierId { get; set; }
        public int RawMaterialId { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        public decimal UnitPrice { get; set; }
        [StringLength(500)]

        public string Reason { get; set; }
        [StringLength(100)]

        public string Status { get; set; } = "Pending Return"; // Pending Return, Returned, Cancelled

        public DateTime ReturnDate { get; set; } = DateTime.Now;
        public DateTime? ActualReturnDate { get; set; }

        public int CreatedBy { get; set; }
        [StringLength(500)]
        public string Notes { get; set; }

        // Navigation properties
        [ForeignKey("ReceivingId")]
        public virtual Receiving Receiving { get; set; }

        [ForeignKey("ReceivingItemId")]  // ← IDAGDAG ITO
        public virtual ReceivingItem ReceivingItem { get; set; }  // ← IDAGDAG ITO

        [ForeignKey("SupplierId")]
        public virtual Supplier Supplier { get; set; }

        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }
    }
}