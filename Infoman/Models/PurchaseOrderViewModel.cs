using System;
using System.Collections.Generic;

namespace Infoman.ViewModels
{
    public class PurchaseOrderViewModel
    {
        public int POId { get; set; }
        public string PONumber { get; set; }
        public int QuotationId { get; set; }
        public string QuotationNumber { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public int? InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public string DeliveryLocation { get; set; }
        public string PaymentTerms { get; set; }
        public string DeliveryInstructions { get; set; }
        public string Notes { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal TaxesDiscount { get; set; }
        public decimal GrandTotal { get; set; }
        public string Status { get; set; }
        public List<POItemViewModel> Items { get; set; } = new List<POItemViewModel>();
    }

    public class POItemViewModel
    {
        public int RawMaterialId { get; set; }
        public string MaterialName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
    }

    // For dropdown - Accepted Quotations
    public class AcceptedQuotationViewModel
    {
        public int QuotationId { get; set; }
        public string QuotationNumber { get; set; }
        public string SupplierName { get; set; }
        public int SupplierId { get; set; }
        public int InquiryId { get; set; }
        public string InquiryNumber { get; set; }
        public DateTime? ExpectedDeliveryDate { get; set; }
        public List<POItemViewModel> Items { get; set; } = new List<POItemViewModel>();
        public decimal Subtotal { get; set; }
        public decimal DeliveryFee { get; set; }
        public decimal GrandTotal { get; set; }
    }
}