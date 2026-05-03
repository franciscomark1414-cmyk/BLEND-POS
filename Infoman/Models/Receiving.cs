// Models/Receiving.cs - CREATE THIS FILE
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("Receivings")]
    public class Receiving
    {
        [Key]
        public int ReceivingId { get; set; }

        [Required]
        public int SupplierId { get; set; }

        [Required]
        [MaxLength(100)]
        public string DRNumber { get; set; }

        [MaxLength(100)]
        public string? InvoiceNumber { get; set; }

        public DateTime DateReceived { get; set; } = DateTime.Now;

        [MaxLength(500)]
        public string? DRImagePath { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [MaxLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(20)]
        public string Status { get; set; } = "Completed";

        // Navigation properties
        [ForeignKey("SupplierId")]
        public virtual Supplier Supplier { get; set; }

        public virtual ICollection<ReceivingItem> ReceivingItems { get; set; }

        public Receiving()
        {
            ReceivingItems = new HashSet<ReceivingItem>();
        }
    }

    [Table("ReceivingItems")]
    public class ReceivingItem
    {
        [Key]
        public int ReceivingItemId { get; set; }

        [Required]
        public int ReceivingId { get; set; }

        [Required]
        public int RawMaterialId { get; set; }

        public decimal? OrderedQty { get; set; }

        [Required]
        public decimal ReceivedQty { get; set; }

        [Required]
        public decimal AcceptedQty { get; set; }

        [Required]
        public decimal RejectedQty { get; set; }

        [MaxLength(200)]
        public string? RejectionReason { get; set; }

        [Required]
        public decimal UnitPrice { get; set; }

        public DateTime? ExpiryDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("ReceivingId")]
        public virtual Receiving Receiving { get; set; }

        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }
    }
}