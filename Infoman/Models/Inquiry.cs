using Infoman.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models  // <-- Palitan ng totoong namespace mo
{
    public class Inquiry
    {
        [Key]
        public int InquiryId { get; set; }

        [Required]
        [StringLength(50)]
        public string InquiryNumber { get; set; }

        [Required]
        public int SupplierId { get; set; }

        [ForeignKey("SupplierId")]
        public virtual Supplier Supplier { get; set; }  // Navigation property

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public DateTime? QuotationDeadline { get; set; }

        public DateTime? PreferredDeliveryDate { get; set; }

        [StringLength(255)]
        public string DeliveryLocation { get; set; }

        [StringLength(100)]
        public string PaymentTerms { get; set; }

        public string Notes { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Draft";  // Draft, Sent, Responded, Closed

        // Navigation Property para sa mga items
        public virtual ICollection<InquiryItem> Items { get; set; } = new List<InquiryItem>();
    }
}