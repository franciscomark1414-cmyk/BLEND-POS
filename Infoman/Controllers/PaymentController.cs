using Infoman.Constants;
using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class PaymentController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly PayMongoService _payMongoService;
        private readonly PaymentOptions _paymentOptions;

        // PHASE 1: Naka-OFF ang webhook. Wala pa itong signature verification,
        // kaya hindi ito dapat makapag-update ng order o payment kahit kailan.
        // Huwag i-set sa true hangga't hindi naidadagdag ang verification.
        private static readonly bool WebhookEnabled = false;

        public PaymentController(ConnectionDatabase db, IOptions<PaymentOptions> paymentOptions)
        {
            _db = db;
            _payMongoService = new PayMongoService();
            _paymentOptions = paymentOptions.Value;
        }

        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        private JsonResult GCashDisabledJson()
        {
            return Json(new { success = false, message = "GCash is temporarily unavailable. Please use Cash." });
        }

        private JsonResult QRPhDisabledJson()
        {
            return Json(new { success = false, message = "QRPh is temporarily unavailable. Please use Cash." });
        }

        public class GCashPaymentRequest
        {
            public int OrderId { get; set; }
            public decimal Amount { get; set; }
            public string? PhoneNumber { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateGCashPayment([FromBody] GCashPaymentRequest request)
        {
            // PHASE 1: Gate. Walang PayMongo call at walang Payment record kapag naka-OFF.
            if (!_paymentOptions.EnableGCash)
                return GCashDisabledJson();

            try
            {
                var order = await _db.Orders.FindAsync(request.OrderId);
                if (order == null)
                {
                    return Json(new { success = false, message = "Order not found" });
                }

                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var successUrl = $"{baseUrl}/Payment/GCashSuccess";
                var cancelUrl = $"{baseUrl}/Payment/GCashCancel";

                System.Diagnostics.Debug.WriteLine($"✅ Success URL: {successUrl}");
                System.Diagnostics.Debug.WriteLine($"✅ Cancel URL: {cancelUrl}");

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

                // ✅ I-SAVE ANG SOURCE ID SA SESSION PARA MAGAMIT PAGBALIK
                HttpContext.Session.SetString("PendingGCashSourceId", result.sourceId);
                HttpContext.Session.SetInt32("PendingGCashOrderId", request.OrderId);

                var payment = new Payment
                {
                    OrderId = request.OrderId,
                    Amount = request.Amount,
                    PaymentMethod = "GCash",
                    PaymentReference = result.sourceId,
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
            // PHASE 1: Gate
            if (!_paymentOptions.EnableQRPh)
                return Json(new { status = "disabled" });

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

        [HttpGet]
        public async Task<IActionResult> GCashSuccess()
        {
            // PHASE 1: Gate. Walang status na mababago kapag naka-OFF ang GCash.
            if (!_paymentOptions.EnableGCash)
            {
                TempData["Error"] = "GCash is temporarily unavailable. Please use Cash.";
                return RedirectToAction("POS", "Staff");
            }

            System.Diagnostics.Debug.WriteLine($"=== GCASHSUCCESS METHOD HIT ===");
            System.Diagnostics.Debug.WriteLine($"Query string: {Request.QueryString}");

            // KUNIN ANG SOURCE ID MULA SA SESSION
            string sourceId = HttpContext.Session.GetString("PendingGCashSourceId");
            int? orderId = HttpContext.Session.GetInt32("PendingGCashOrderId");

            System.Diagnostics.Debug.WriteLine($"SourceId from session: {sourceId}");
            System.Diagnostics.Debug.WriteLine($"OrderId from session: {orderId}");

            // Also check query string as fallback
            if (string.IsNullOrEmpty(sourceId))
            {
                sourceId = Request.Query["source_id"].ToString();
                System.Diagnostics.Debug.WriteLine($"From query string: {sourceId}");
            }

            if (string.IsNullOrEmpty(sourceId) && orderId.HasValue)
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.OrderId == orderId.Value && p.PaymentMethod == "GCash");
                if (payment != null)
                {
                    sourceId = payment.PaymentReference;
                    System.Diagnostics.Debug.WriteLine($"Found from payment record: {sourceId}");
                }
            }

            if (string.IsNullOrEmpty(sourceId))
            {
                System.Diagnostics.Debug.WriteLine("Source ID is null or empty");
                TempData["Error"] = "Invalid payment session";
                return RedirectToAction("POS", "Staff");
            }

            // Wait for PayMongo to process
            await Task.Delay(2000);

            var result = await _payMongoService.GetSourceStatusAsync(sourceId);
            System.Diagnostics.Debug.WriteLine($"Source status: success={result.success}, status={result.status}");

            if (result.success && result.status == "chargeable")
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == sourceId);

                if (payment != null)
                {
                    payment.Status = "PAID";
                    payment.PaymentDate = DateTime.Now;
                    await _db.SaveChangesAsync();

                    // ✅ IMPORTANTE: I-load ang order KASAMA ang ITEMS
                    var order = await _db.Orders
                        .Include(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                        .Include(o => o.Items)
                        .ThenInclude(oi => oi.ProductSize)
                        .FirstOrDefaultAsync(o => o.OrderId == payment.OrderId);

                    if (order != null)
                    {
                        // ✅ HUWAG GALAWIN ANG ITEMS! I-update lang ang status
                        order.Status = "PENDING_BLENDING";
                        order.PaymentMethod = "GCash";
                        await _db.SaveChangesAsync();

                        // I-log kung ilang items ang nasa order
                        System.Diagnostics.Debug.WriteLine($"✅ Order #{order.OrderId} has {order.Items.Count} items");
                        foreach (var item in order.Items)
                        {
                            System.Diagnostics.Debug.WriteLine($"   - {item.Product?.Name} x {item.Quantity}");
                        }

                        await RecordEmployeePerformance(order.StaffUserId ?? 0, order.TotalAmount);

                        // ✅ I-redirect na may orderId para makita ang tamang receipt
                        return RedirectToAction("POS", "Staff", new { showReceipt = true, orderId = order.OrderId });
                    }
                }
            }

            // I-CLEAR ANG SESSION
            HttpContext.Session.Remove("PendingGCashSourceId");
            HttpContext.Session.Remove("PendingGCashOrderId");

            return RedirectToAction("POS", "Staff", new { showReceipt = true });
        }

        private async Task RecordEmployeePerformance(int userId, decimal orderAmount)
        {
            if (userId == 0) return;

            try
            {
                var today = DateTime.Today;
                var now = DateTime.Now;

                var existingPerformance = await _db.EmployeePerformances
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.Date.Date == today);

                if (existingPerformance != null)
                {
                    existingPerformance.TotalSales += orderAmount;
                    existingPerformance.OrdersProcessed += 1;
                    existingPerformance.UpdatedAt = now;
                }
                else
                {
                    var performance = new EmployeePerformance
                    {
                        UserId = userId,
                        Date = today,
                        TotalSales = orderAmount,
                        OrdersProcessed = 1,
                        Rating = 0,
                        Notes = "Auto-recorded from GCash payment",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _db.EmployeePerformances.Add(performance);
                }

                // Update user stats
                var user = await _db.Users.FindAsync(userId);
                if (user != null)
                {
                    var allPerformances = await _db.EmployeePerformances
                        .Where(p => p.UserId == userId)
                        .ToListAsync();

                    user.TotalOrdersProcessed = allPerformances.Sum(p => p.OrdersProcessed);
                    var ratedPerformances = allPerformances.Where(p => p.Rating > 0).ToList();
                    user.AverageRating = ratedPerformances.Any() ? ratedPerformances.Average(p => p.Rating) : 0;
                    user.UpdatedAt = now;
                }

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log error but don't throw - we don't want to break the payment flow
                Console.WriteLine($"Error recording performance: {ex.Message}");
            }
        }

        public async Task<IActionResult> GCashCancel(string source_id, string id)
        {
            // PHASE 1: Gate
            if (!_paymentOptions.EnableGCash)
            {
                TempData["Error"] = "GCash is temporarily unavailable. Please use Cash.";
                return RedirectToAction("POS", "Staff");
            }

            string sourceId = source_id ?? id;

            if (!string.IsNullOrEmpty(sourceId))
            {
                var payment = await _db.Payments
                    .FirstOrDefaultAsync(p => p.PaymentReference == sourceId);

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
            // PHASE 1: Gate. Walang PayMongo call at walang Payment record kapag naka-OFF.
            if (!_paymentOptions.EnableQRPh)
                return QRPhDisabledJson();

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
            // PHASE 1: Hindi ito pinoproseso. Walang nababago sa order o payment.
            // 503 para ma-retry ng PayMongo kung sakaling i-enable ito balang araw.
            if (!WebhookEnabled)
            {
                return StatusCode(503);
            }

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
                }
            }

            return Ok();
        }
    }
}