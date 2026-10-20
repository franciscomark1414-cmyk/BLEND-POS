using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("WasteLogs")]
    public class WasteLog
    {
        public enum WasteReason
        {
            Expired,
            Damaged,
            Spilled,
            Overprepared,
            QualityIssue,
            CustomerCancel,
            BlendedButCancelled,
            Other
        }

        [Key]
        public int WasteLogId { get; set; }

        [Required]
        public int RawMaterialId { get; set; }
        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }

        public int? ProductId { get; set; }
        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public WasteReason Reason { get; set; }

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [StringLength(100)]
        public string? RecordedBy { get; set; }
        [StringLength(500)]
        public string? Notes { get; set; }

        public decimal TotalCost { get; set; }

        // ✅ IBALIK SA ExpiryDate (WAG NANG GAMITIN ANG WasteExpiryDate)
        public DateTime? ExpiryDate { get; set; }

        public int? BatchId { get; set; }

        [ForeignKey("BatchId")]
        public virtual RawMaterialBatch Batch { get; set; }
    }
}