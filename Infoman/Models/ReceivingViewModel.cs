// Models/ViewModels/ReceivingViewModel.cs
namespace Infoman.Models
{
    public class ReceivingViewModel
    {
        public int ReceivingId { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; }
        public string DRNumber { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime DateReceived { get; set; } = DateTime.Now;
        public string Remarks { get; set; }
        public List<ReceivingItemViewModel> Items { get; set; } = new List<ReceivingItemViewModel>();
    }

    public class ReceivingItemViewModel
    {
        public int RawMaterialId { get; set; }
        public string RawMaterialName { get; set; }
        public decimal? OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal AcceptedQty { get; set; }
        public decimal RejectedQty { get; set; }
        public string Disposition { get; set; }
        public string RejectionReason { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }
}