// Models/InventoryMovement.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class InventoryMovement
    {
        [Key]
        public int InventoryMovementId { get; set; }

        public int? RawMaterialId { get; set; }
        [ForeignKey("RawMaterialId")]
        public virtual RawMaterial RawMaterial { get; set; }

        public int? ProductId { get; set; }

        public decimal Quantity { get; set; }

        [Required]
        [StringLength(50)]
        public string MovementType { get; set; } // "SALE", "VOID_RETURN", "WASTE", "REFUND", "PRODUCTION"
        [StringLength(100)]

        public string ReferenceId { get; set; } // OrderNumber, RefundId, etc.

        public DateTime Timestamp { get; set; } = DateTime.Now;
        [StringLength(200)]

        public string Details { get; set; }

        public int? UserId { get; set; }

        public decimal UnitPrice { get; set; } // Para ma-compute ang cost
    }
}