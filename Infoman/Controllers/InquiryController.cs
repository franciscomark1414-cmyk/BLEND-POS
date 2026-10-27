using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Infoman.Data;  // Palitan ng totoong namespace
using Infoman.Models;
using Infoman.ViewModels;

namespace Infoman.Controllers
{
    public class InquiryController : Controller
    {
        private readonly ConnectionDatabase _context;

        public InquiryController(ConnectionDatabase context)
        {
            _context = context;
        }

        // GET: I-load lahat ng inquiries
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var inquiries = await _context.Inquiries
                .Include(i => i.Supplier)
                .Include(i => i.Items)
                .OrderByDescending(i => i.DateCreated)
                .Select(i => new
                {
                    i.InquiryId,
                    i.InquiryNumber,
                    i.DateCreated,
                    SupplierName = i.Supplier.Name,
                    ItemsCount = i.Items.Count,
                    i.Status,
                    i.QuotationDeadline
                })
                .ToListAsync();

            return Json(inquiries);
        }

        // POST: Gumawa ng bagong Inquiry
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] InquiryViewModel model)
        {
            if (model == null || model.Items == null || model.Items.Count == 0)
                return Json(new { success = false, message = "No items provided" });

            var newInquiry = new Inquiry
            {
                InquiryNumber = string.IsNullOrEmpty(model.InquiryNumber)
    ? "INQ-" + DateTime.Now.ToString("yyyyMMddHHmmss")
    : model.InquiryNumber,
                SupplierId = model.SupplierId,
                DateCreated = DateTime.Now,
                QuotationDeadline = model.QuotationDeadline,
                PreferredDeliveryDate = model.PreferredDeliveryDate,
                DeliveryLocation = model.DeliveryLocation,
                PaymentTerms = model.PaymentTerms,
                Notes = model.Notes,
                Status = model.Status ?? "Draft"
            };

            _context.Inquiries.Add(newInquiry);
            await _context.SaveChangesAsync();

            foreach (var item in model.Items)
            {
                _context.InquiryItems.Add(new InquiryItem
                {
                    InquiryId = newInquiry.InquiryId,
                    RawMaterialId = item.RawMaterialId,
                    Quantity = item.Quantity,
                    Unit = item.Unit
                });
            }
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Inquiry saved successfully!", inquiryId = newInquiry.InquiryId });
        }

        // GET: Kunin ang isang inquiry para sa Document View
        [HttpGet]
        public async Task<IActionResult> GetDetails(int id)
        {
            var inquiry = await _context.Inquiries
                .Include(i => i.Supplier)
                .Include(i => i.Items).ThenInclude(item => item.RawMaterial)
                .FirstOrDefaultAsync(i => i.InquiryId == id);

            if (inquiry == null)
                return Json(new { success = false, message = "Inquiry not found" });

            var result = new
            {
                inquiry.InquiryId,
                inquiry.InquiryNumber,
                inquiry.DateCreated,
                inquiry.QuotationDeadline,
                inquiry.PreferredDeliveryDate,
                inquiry.DeliveryLocation,
                inquiry.PaymentTerms,
                inquiry.Notes,
                inquiry.Status,
                Supplier = new { inquiry.Supplier.Name },
                Items = inquiry.Items.Select(ii => new
                {
                    ii.RawMaterialId,
                    ii.Quantity,
                    ii.Unit,
                    RawMaterial = new { ii.RawMaterial.Name }
                })
            };

            return Json(new { success = true, data = result });
        }
    }
}