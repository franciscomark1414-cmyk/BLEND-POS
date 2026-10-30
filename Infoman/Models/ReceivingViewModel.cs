// Models/ViewModels/ReceivingViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class ReceivingViewModel
    {
        public int ReceivingId { get; set; }
        public int SupplierId { get; set; }
        [StringLength(100)]
        public string SupplierName { get; set; }
        [StringLength(100)]
        public string DRNumber { get; set; }
        [StringLength(100)]
        public string InvoiceNumber { get; set; }
        public DateTime DateReceived { get; set; } = DateTime.Now;
        [StringLength(100)]
        public string Remarks { get; set; }
        public List<ReceivingItemViewModel> Items { get; set; } = new List<ReceivingItemViewModel>();
    }

    public class ReceivingItemViewModel
    {
        public int RawMaterialId { get; set; }
        [StringLength(100)]
        public string RawMaterialName { get; set; }
        public decimal? OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal AcceptedQty { get; set; }
        public decimal RejectedQty { get; set; }
        public string Unit { get; set; }
        public string StoreName { get; set; }
        [StringLength(50)]
        public string Disposition { get; set; }
        [StringLength(100)]
        public string RejectionReason { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}