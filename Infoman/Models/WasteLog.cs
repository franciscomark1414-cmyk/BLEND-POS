using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class WasteLog
    {
        public enum WasteReason {
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
        public int? ProductId { get; set; } // For product waste (blended mango shake)
        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; }
        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public WasteReason Reason { get; set; }

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        [MaxLength(100)]
        public string? RecordedBy { get; set; }

        public string? Notes { get; set; }

        // Computed property for total cost wasted
        public decimal TotalCost => Quantity * (RawMaterial?.Price ?? 0);


    }
}
