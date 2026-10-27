using Infoman.Models;
using Infoman.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Infoman.Models;
using Infoman.ViewModels;
using Infoman.Data;

namespace Infoman.Controllers
{
    public class PurchaseOrderController : Controller
    {
        private readonly ConnectionDatabase _context;

        public PurchaseOrderController(ConnectionDatabase context)
        {
            _context = context;
        }

        // GET: Kunin lahat ng POs
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var pos = await _context.PurchaseOrders
                .OrderByDescending(p => p.DateCreated)
                .Select(p => new
                {
                    p.POId,
                    p.PONumber,
                    p.QuotationNumber,
                    SupplierName = _context.Suppliers
                        .Where(s => s.SupplierId == p.SupplierId)
                        .Select(s => s.Name).FirstOrDefault(),
                    p.OrderDate,
                    p.GrandTotal,
                    p.Status
                })
                .ToListAsync();

            return Json(pos);
        }

        // GET: Kunin ang listahan ng Accepted Quotations (para sa dropdown)
        [HttpGet]
        public async Task<IActionResult> GetAcceptedQuotations()
        {
            // TODO: Palitan ito kapag may Quotations table na.
            // For now, empty list muna since wala pang supplier portal.

            var acceptedQuotations = new List<AcceptedQuotationViewModel>();

            // Kapag ready na, ganito:
            // var acceptedQuotations = await _context.Quotations
            //     .Where(q => q.Status == "Accepted")
            //     .Select(q => new AcceptedQuotationViewModel
            //     {
            //         QuotationId = q.QuotationId,
            //         QuotationNumber = q.QuotationNumber,
            //         SupplierName = q.Supplier.Name,
            //         SupplierId = q.SupplierId,
            //         InquiryId = q.InquiryId,
            //         InquiryNumber = q.Inquiry.InquiryNumber,
            //         ExpectedDeliveryDate = q.ExpectedDeliveryDate,
            //         Items = q.Items.Select(i => new POItemViewModel
            //         {
            //             RawMaterialId = i.RawMaterialId,
            //             MaterialName = i.RawMaterial.Name,
            //             Quantity = i.Quantity,
            //             Unit = i.Unit,
            //             UnitPrice = i.UnitPrice,
            //             Subtotal = i.Quantity * i.UnitPrice
            //         }).ToList(),
            //         Subtotal = q.Subtotal,
            //         DeliveryFee = q.DeliveryFee,
            //         GrandTotal = q.GrandTotal
            //     })
            //     .ToListAsync();

            return Json(acceptedQuotations);
        }

        // GET: Kunin ang specific Accepted Quotation details (para auto-fill)
        [HttpGet]
        public async Task<IActionResult> GetQuotationDetails(int quotationId)
        {
            // TODO: Palitan kapag may Quotations table
            // var quotation = await _context.Quotations
            //     .Include(q => q.Items)
            //     .FirstOrDefaultAsync(q => q.QuotationId == quotationId);

            return Json(new { success = false, message = "Quotation not found." });
        }

        // POST: Gumawa ng bagong PO
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] PurchaseOrderViewModel model)
        {
            if (model == null || model.Items == null || model.Items.Count == 0)
                return Json(new { success = false, message = "No items provided." });

            // Auto-generate PO Number kung wala
            var poNumber = string.IsNullOrEmpty(model.PONumber)
                ? "PO-" + DateTime.Now.ToString("yyyyMMddHHmmss")
                : model.PONumber;

            var newPO = new PurchaseOrder
            {
                PONumber = poNumber,
                QuotationId = model.QuotationId,
                QuotationNumber = model.QuotationNumber,
                SupplierId = model.SupplierId,
                InquiryId = model.InquiryId,
                OrderDate = model.OrderDate == default ? DateTime.Now : model.OrderDate,
                ExpectedDeliveryDate = model.ExpectedDeliveryDate,
                DeliveryLocation = model.DeliveryLocation,
                PaymentTerms = model.PaymentTerms,
                DeliveryInstructions = model.DeliveryInstructions,
                Notes = model.Notes,
                Subtotal = model.Subtotal,
                DeliveryFee = model.DeliveryFee,
                TaxesDiscount = model.TaxesDiscount,
                GrandTotal = model.GrandTotal,
                Status = model.Status ?? "Draft"
            };

            _context.PurchaseOrders.Add(newPO);
            await _context.SaveChangesAsync();

            foreach (var item in model.Items)
            {
                _context.PurchaseOrderItems.Add(new PurchaseOrderItem
                {
                    POId = newPO.POId,
                    RawMaterialId = item.RawMaterialId,
                    MaterialName = item.MaterialName,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    UnitPrice = item.UnitPrice,
                    Subtotal = item.Subtotal
                });
            }
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "PO saved successfully!", poId = newPO.POId });
        }
    }
}