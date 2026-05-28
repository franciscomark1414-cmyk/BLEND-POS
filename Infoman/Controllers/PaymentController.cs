using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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

        public class GCashPaymentRequest
        {
            public int OrderId { get; set; }
            public decimal Amount { get; set; }
            public string? PhoneNumber { get; set; }
        }

        /// <summary>
        /// Initiate GCash payment using Sources API (works in test mode!)
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

                // ✅ Use Sources API instead of Checkout Session
                var successUrl = Url.Action("GCashSuccess", "Payment", null, Request.Scheme);
                var cancelUrl = Url.Action("GCashCancel", "Payment", null, Request.Scheme);

                var result = await _payMongoService.CreateGCashSourceAsync(
                    request.Amount,
                    order.OrderNumber,
                    successUrl,
                    cancelUrl
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
                    PaymentReference = result.sourceId,  // Source ID, hindi session ID
                    Status = "PENDING",
                    PaymentDate = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                return Json(new { success = true, checkoutUrl = result.checkoutUrl });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Check QRPh payment status via AJAX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckQRPhStatus(string paymentIntentId)
        {
            try
            {
                // Hanapin ang payment sa database
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == paymentIntentId);

                if (payment != null && payment.Status == "PAID")
                {
                    return Json(new { status = "paid" });
                }
                else if (payment != null && payment.Status == "PENDING")
                {
                    return Json(new { status = "pending" });
                }
                else
                {
                    return Json(new { status = "not_found" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { status = "error", message = ex.Message });
            }
        }

        /// <summary>
        /// Success callback from PayMongo (Sources API)
        /// </summary>
        public async Task<IActionResult> GCashSuccess(string id)  // 'id' ang source ID
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["Error"] = "Invalid payment session";
                return RedirectToAction("POS", "Staff");
            }

            // Get source status
            var result = await _payMongoService.GetSourceStatusAsync(id);

            if (result.success && result.status == "chargeable")
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == id);

                if (payment != null)
                {
                    payment.Status = "PAID";
                    payment.PaymentDate = DateTime.Now;
                    await _db.SaveChangesAsync();

                    var order = await _db.Orders.FindAsync(payment.OrderId);
                    if (order != null)
                    {
                        order.Status = "COMPLETED";
                        order.PaymentMethod = "GCash";
                        await _db.SaveChangesAsync();
                    }

                    TempData["Success"] = $"Payment of ₱{payment.Amount:N2} successful!";
                }
            }
            else
            {
                TempData["Error"] = $"Payment not completed. Status: {result.status}";
            }

            return RedirectToAction("POS", "Staff");
        }

        /// <summary>
        /// Cancel callback from PayMongo (Sources API)
        /// </summary>
        public async Task<IActionResult> GCashCancel(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == id);

                if (payment != null)
                {
                    payment.Status = "CANCELLED";
                    await _db.SaveChangesAsync();
                }
            }

            TempData["Error"] = "Payment was cancelled.";
            return RedirectToAction("POS", "Staff");
        }

        // Keep these for backward compatibility (if needed)
        public async Task<IActionResult> Success(string session_id)
        {
            TempData["Error"] = "Please use GCash payment method.";
            return RedirectToAction("POS", "Staff");
        }

        public async Task<IActionResult> Cancel(string session_id)
        {
            TempData["Error"] = "Payment was cancelled.";
            return RedirectToAction("POS", "Staff");
        }

        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(string sessionId)
        {
            return Json(new { success = false, status = "Use Sources API instead" });
        }

        /// <summary>
        /// Creates QRPh payment for POS (customer scans)
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQRPhPayment([FromBody] QRPhPaymentRequest request)
        {
            try
            {
                var order = await _db.Orders.FindAsync(request.OrderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                // Create QRPh payment intent
                var result = await _payMongoService.CreateQRPhPaymentIntentAsync(
                    request.Amount,
                    order.OrderNumber
                );

                if (!result.success)
                {
                    return Json(new { success = false, message = result.errorMessage });
                }

                // Save payment record (pending muna)
                var payment = new Payment
                {
                    OrderId = request.OrderId,
                    Amount = request.Amount,
                    PaymentMethod = "QRPh",
                    PaymentReference = result.paymentIntentId,
                    Status = "PENDING",
                    PaymentDate = DateTime.Now
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();

                // Return QR code as base64 image
                return Json(new
                {
                    success = true,
                    qrCodeBase64 = result.qrCodeBase64,
                    paymentIntentId = result.paymentIntentId
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Request model
        public class QRPhPaymentRequest
        {
            public int OrderId { get; set; }
            public decimal Amount { get; set; }
        }

        [HttpPost("webhook/paymongo")]
        public async Task<IActionResult> HandlePayMongoWebhook()
        {
            var json = await new StreamReader(Request.Body).ReadToEndAsync();
            Console.WriteLine($"Webhook received: {json}");

            // I-parse ang webhook payload
            dynamic webhookData = JsonConvert.DeserializeObject(json);
            string eventType = webhookData.data.attributes.type;

            // Hanapin ang payment_intent.paid event
            if (eventType == "payment_intent.paid")
            {
                string paymentIntentId = webhookData.data.attributes.data.id;

                // Hanapin ang payment sa database
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == paymentIntentId);

                if (payment != null && payment.Status != "PAID")
                {
                    payment.Status = "PAID";
                    payment.PaymentDate = DateTime.Now;

                    // Update order status
                    var order = await _db.Orders.FindAsync(payment.OrderId);
                    if (order != null)
                    {
                        order.Status = "COMPLETED";
                        order.PaymentMethod = "QRPh";
                    }

                    await _db.SaveChangesAsync();

                    // Optional: Mag-trigger ng SignalR para real-time update sa POS
                    // await _signalRService.NotifyPaymentReceived(payment.OrderId);
                }
            }

            // Dapat mag-respond ng 200 OK [citation:2]
            return Ok();
        }
    }
}