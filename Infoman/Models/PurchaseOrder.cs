using Infoman.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class PurchaseOrder
    {
        [Key]
        public int POId { get; set; }

        [Required]
        [StringLength(50)]
        public string PONumber { get; set; }

        public int QuotationId { get; set; }
        [StringLength(50)]
        public string QuotationNumber { get; set; }

        public int SupplierId { get; set; }
        public int? InquiryId { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.Now;
        public DateTime? ExpectedDeliveryDate { get; set; }

        [StringLength(255)]
        public string DeliveryLocation { get; set; }

        [StringLength(100)]
        public string PaymentTerms { get; set; }

        public string DeliveryInstructions { get; set; }
        public string Notes { get; set; }

        public decimal Subtotal { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal TaxesDiscount { get; set; }
        public decimal GrandTotal { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Draft";

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    }
}