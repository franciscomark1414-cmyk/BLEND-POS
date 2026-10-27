using Infoman.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class InquiryItem
    {
        [Key]
        public int InquiryItemId { get; set; }

        [Required]
        public int InquiryId { get; set; }

        [ForeignKey("InquiryId")]
        public virtual Inquiry Inquiry { get; set; }

        [Required]
        public int RawMaterialId { get; set; }

        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }

        [Required]
        [StringLength(20)]
        public string Unit { get; set; }
    }
}