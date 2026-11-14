using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Controllers
{
    public class VoucherController : Controller
    {
        private readonly ConnectionDatabase _db;
        public VoucherController(ConnectionDatabase db) { _db = db; }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var vouchers = await _db.Vouchers.OrderByDescending(v => v.CreatedAt).ToListAsync();
            return Json(new { success = true, vouchers = vouchers });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate([FromBody] GenerateVoucherRequest req)
        {
            if (req.Quantity < 1 || req.DiscountPercent <= 0 || req.DiscountPercent > 100)
                return Json(new { success = false, message = "Invalid quantity or discount percent (1-100 only)." });

            var newVouchers = new List<Voucher>();
            for (int i = 0; i < req.Quantity; i++)
            {
                string code;
                do
                {
                    code = "JV" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
                } while (await _db.Vouchers.AnyAsync(v => v.Code == code));

                newVouchers.Add(new Voucher
                {
                    Code = code,
                    DiscountPercent = req.DiscountPercent, // ✅ Percent
                    EventName = req.EventName,
                    ExpiryDate = req.ExpiryDate,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            _db.Vouchers.AddRange(newVouchers);
            await _db.SaveChangesAsync();
            return Json(new { success = true, message = "Vouchers generated." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var voucher = await _db.Vouchers.FindAsync(id);
            if (voucher == null) return Json(new { success = false, message = "Voucher not found." });
            _db.Vouchers.Remove(voucher);
            await _db.SaveChangesAsync();
            return Json(new { success = true, message = "Voucher deleted." });
        }
    }

    public class GenerateVoucherRequest
    {
        public int Quantity { get; set; }
        public string EventName { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal DiscountPercent { get; set; } // ✅ Percent
    }
}