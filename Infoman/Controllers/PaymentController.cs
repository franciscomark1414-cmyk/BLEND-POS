using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly PayMongoService _payMongoService;

        public PaymentController(ConnectionDatabase db)
        {
            _db = db;
            _payMongoService = new PayMongoService();
        }

        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        /// <summary>
        /// Initiate GCash payment for an order
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateGCashPayment([FromBody] GCashPaymentRequest request)
        {
            try
            {
                var order = await _db.Orders.FindAsync(request.OrderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                var result = await _payMongoService.CreateCheckoutSessionAsync(
                    request.Amount,
                    $"Payment for Order #{order.OrderNumber}",
                    order.OrderNumber
                );

                if (!result.success)
                {
                    return Json(new { success = false, message = result.errorMessage });
                }

                var payment = new Payment
                {
                    OrderId = request.OrderId,
                    Amount = request.Amount,
                    PaymentMethod = "GCash",
                    PaymentReference = result.sessionId,
                    Status = "PENDING",
                    PaymentDate = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                // ✅ IMPORTANT: Return JSON, not Redirect
                return Json(new { success = true, checkoutUrl = result.checkoutUrl });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Add this request model class inside PaymentController or outside
        public class GCashPaymentRequest
        {
            public int OrderId { get; set; }
            public decimal Amount { get; set; }
        }

        /// <summary>
        /// Success callback from PayMongo
        /// </summary>
        public async Task<IActionResult> Success(string session_id)
        {
            if (string.IsNullOrEmpty(session_id))
            {
                TempData["Error"] = "Invalid payment session";
                return RedirectToAction("POS", "Staff");
            }

            // Verify payment status
            var result = await _payMongoService.GetCheckoutSessionStatusAsync(session_id);

            if (!result.success || result.status != "paid")
            {
                TempData["Error"] = "Payment not completed. Please try again.";
                return RedirectToAction("POS", "Staff");
            }

            // Find the payment record
            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.PaymentReference == session_id);

            if (payment != null)
            {
                payment.Status = "PAID";
                payment.PaymentDate = DateTime.Now;
                await _db.SaveChangesAsync();

                // Update order status
                var order = await _db.Orders.FindAsync(payment.OrderId);
                if (order != null)
                {
                    order.Status = "COMPLETED";
                    order.PaymentMethod = "GCash";
                    await _db.SaveChangesAsync();
                }

                TempData["Success"] = $"Payment of ₱{payment.Amount:N2} successful!";
            }

            return RedirectToAction("POS", "Staff");
        }

        /// <summary>
        /// Cancel callback from PayMongo
        /// </summary>
        public async Task<IActionResult> Cancel(string session_id)
        {
            if (!string.IsNullOrEmpty(session_id))
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == session_id);

                if (payment != null)
                {
                    payment.Status = "CANCELLED";
                    await _db.SaveChangesAsync();
                }
            }

            TempData["Error"] = "Payment was cancelled.";
            return RedirectToAction("POS", "Staff");
        }

        /// <summary>
        /// Check payment status via AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(string sessionId)
        {
            var result = await _payMongoService.GetCheckoutSessionStatusAsync(sessionId);

            if (result.success && result.status == "paid")
            {
                return Json(new { success = true, status = "paid" });
            }

            return Json(new { success = false, status = result.status });
        }
    }
}