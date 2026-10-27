using Infoman.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class PurchaseOrderItem
    {
        [Key]
        public int POItemId { get; set; }
        public int POId { get; set; }
        [ForeignKey("POId")]
        public virtual PurchaseOrder PurchaseOrder { get; set; }

        public int RawMaterialId { get; set; }

        [Required]
        [StringLength(200)]
        public string MaterialName { get; set; }

        public decimal Quantity { get; set; }

        [StringLength(20)]
        public string Unit { get; set; }

        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
    }
}