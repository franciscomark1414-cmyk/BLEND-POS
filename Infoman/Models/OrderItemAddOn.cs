// ============================================================
// FILE: Models/OrderItemAddOn.cs
// ============================================================
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class OrderItemAddOn
    {
        [Key]
        public int OrderItemAddOnId { get; set; }

        public int OrderItemId { get; set; }

        public int AddOnId { get; set; }

        /// <summary>
        /// Snapshot ng name — hindi magbabago kahit i-edit/delete ang add-on
        /// </summary>
        [Required]
        [StringLength(100)]
        public string AddOnName { get; set; }

        /// <summary>
        /// Snapshot ng price sa oras ng order
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Actual FIFO cost (galing sa StartBlending deduction)
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal ActualCost { get; set; } = 0;

        // Navigation
        [ForeignKey(nameof(OrderItemId))]
        public OrderItem? OrderItem { get; set; }

        [ForeignKey(nameof(AddOnId))]
        public AddOn? AddOn { get; set; }
    }
}