using System;
using System.Collections.Generic;

namespace Infoman.ViewModels
{
    public class InquiryViewModel
    {
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? QuotationDeadline { get; set; }
        public DateTime? PreferredDeliveryDate { get; set; }
        public string DeliveryLocation { get; set; }
        public string PaymentTerms { get; set; }
        public string Notes { get; set; }
        public string Status { get; set; }
        public List<InquiryItemViewModel> Items { get; set; } = new List<InquiryItemViewModel>();
    }

    public class InquiryItemViewModel
    {
        public int RawMaterialId { get; set; }
        public string MaterialName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
    }
}