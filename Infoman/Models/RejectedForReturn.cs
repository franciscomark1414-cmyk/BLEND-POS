using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("RejectedForReturn")]
    public class RejectedForReturn
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ReceivingId { get; set; }

        [Required]
        public int RawMaterialId { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        [StringLength(200)]
        public string? Reason { get; set; }

        [Required]
        public decimal UnitPrice { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Pending Return";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ForeignKey("ReceivingId")]
        public virtual Receiving Receiving { get; set; }

        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }
    }
}