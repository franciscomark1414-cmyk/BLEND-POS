using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class VoidRefundController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly VoidRefundService _voidRefundService;

        public VoidRefundController(ConnectionDatabase db, VoidRefundService voidRefundService)
        {
            _db = db;
            _voidRefundService = voidRefundService;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderDetails(int orderId)
        {
            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                    .Include(o => o.Items)
                        .ThenInclude(oi => oi.ProductSize)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                    return Json(new { success = false, message = "Order not found" });

                // ✅ I-COMPUTE ANG DISCOUNTED PRICE PER ITEM
                var allItems = order.Items.Select(oi => new
                {
                    oi.OrderItemId,
                    oi.ProductId,
                    oi.ProductSizeId,
                    ProductName = oi.Product != null ? oi.Product.Name : "Unknown Product",
                    SizeName = oi.ProductSize != null ? oi.ProductSize.SizeName : "Regular",
                    oi.Quantity,
                    Price = order.DiscountPercent > 0 ? oi.Price * (1 - order.DiscountPercent) : oi.Price,
                    Subtotal = (order.DiscountPercent > 0 ? oi.Price * (1 - order.DiscountPercent) : oi.Price) * oi.Quantity,
                    IsVoided = oi.DispositionStatusId == 2,
                    IsRefunded = oi.DispositionStatusId == 3,
                    DispositionStatusId = oi.DispositionStatusId
                }).ToList();

                // ✅ Ang subtotal ay sum ng discounted subtotals
                decimal subtotal = allItems
                    .Where(i => !i.IsVoided && !i.IsRefunded)
                    .Sum(i => i.Subtotal);

                // ✅ TAMA NA: Hindi na kailangang i-multiply sa (1 - DiscountPercent) kasi discounted na ang items
                decimal total = subtotal;

                decimal adjustmentAmount = order.AdjustmentAmount;
                decimal storeCreditApplied = order.StoreCreditApplied;

                return Json(new
                {
                    success = true,
                    order = new
                    {
                        order.OrderId,
                        order.OrderNumber,
                        order.Status,
                        order.TotalAmount,
                        order.PaymentMethod,
                        order.DiscountType,
                        order.DiscountPercent,
                        Items = allItems,
                        subtotal = subtotal,
                        total = total,
                        discountAmount = order.DiscountAmount,
                        adjustmentAmount = adjustmentAmount,
                        originalOrderId = order.OriginalOrderId,
                        returnStatus = order.ReturnStatus ?? "ACTIVE",
                        storeCreditApplied = storeCreditApplied
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // VoidRefundController.cs - Add this endpoint
        [HttpGet]
        public async Task<IActionResult> GetOrderIdByPaymentReference(string paymentRef)
        {
            if (string.IsNullOrEmpty(paymentRef))
                return Json(new { success = false, message = "No payment reference provided" });

            System.Diagnostics.Debug.WriteLine($"GetOrderIdByPaymentReference called with: {paymentRef}");

            // Hanapin sa Payments table
            var payment = await _db.Payments
                .FirstOrDefaultAsync(p => p.PaymentReference == paymentRef);

            if (payment != null)
            {
                return Json(new { success = true, orderId = payment.OrderId });
            }

            // Kung wala sa Payments, hanapin sa Orders table
            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.OrderNumber == paymentRef || o.OrderId.ToString() == paymentRef);

            if (order != null)
            {
                return Json(new { success = true, orderId = order.OrderId });
            }

            return Json(new { success = false, message = "Order not found" });
        }


        // GET: VoidRefund/GetPendingOrders
        [HttpGet]
        public async Task<IActionResult> GetPendingOrders(DateTime? date)
        {
            try
            {
                var targetDate = date ?? DateTime.Today;
                var orders = await _voidRefundService.GetPendingOrdersByDate(targetDate);
                return Json(orders);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: VoidRefund/GetCompletedOrders
        [HttpGet]
        public async Task<IActionResult> GetCompletedOrders(DateTime? date)
        {
            try
            {
                var targetDate = date ?? DateTime.Today;
                var orders = await _voidRefundService.GetCompletedOrdersByDate(targetDate);
                return Json(orders);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

       

        // GET: VoidRefund/GetReports
        [HttpGet]
        public async Task<IActionResult> GetReports(DateTime startDate, DateTime endDate)
        {
            try
            {
                var reports = await _voidRefundService.GetReports(startDate, endDate);
                return Json(reports);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ===== PHASE 3: Void at refund (Owner approval para sa paid) =====

        // Owner session lang ang tinatanggap para sa approval (hindi Staff session).
        private async Task<int> GetOwnerIdAsync()
        {
            int ownerId = HttpContext.Session.GetInt32("Owner_UserId") ?? 0;
            if (ownerId <= 0) return 0;

            var user = await _db.Users.FindAsync(ownerId);
            return (user != null && user.Role == "Owner") ? ownerId : 0;
        }

        // POST: VoidRefund/VoidItem
        // DISABLED: hindi na pwedeng direktang mag-void ng item. Gamitin ang Refund request.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult VoidItem([FromBody] VoidItemRequest request)
        {
            return Json(new { success = false, message = "Direct item void is disabled. Use the Refund button instead." });
        }

        // POST: VoidRefund/VoidTransaction
        // Unpaid: CANCELLED. Paid (staff): request lang, kailangan ng Owner approval. Paid (Owner): direct.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidTransaction([FromBody] VoidTransactionRequest request)
        {
            try
            {
                if (request == null || request.OrderId == 0)
                    return Json(new { success = false, message = "Invalid request" });

                if (string.IsNullOrWhiteSpace(request.Reason))
                    return Json(new { success = false, message = "Reason is required" });

                bool isOwner = false;
                int userId = GetCurrentUserId();

                if (userId == 0)
                    return Json(new { success = false, message = "User not logged in" });

                var result = await _voidRefundService.RequestTransactionVoidAsync(
                    request.OrderId, userId, isOwner, request.Reason.Trim());

                return Json(new
                {
                    success = result.success,
                    message = result.message,
                    requiresOwner = result.requiresOwner,
                    refundAmount = result.refundAmount,
                    updatedItems = new List<object>(),
                    isFullRefund = result.success && !result.requiresOwner
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: VoidRefund/ApproveRefund (Owner only). Iisang approval path para sa VOID at REFUND.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRefund([FromBody] ApproveRefundRequest request)
        {
            try
            {
                int ownerId = await GetOwnerIdAsync();
                if (ownerId == 0)
                    return Json(new { success = false, message = "Only Owner can approve" });

                var type = await _db.PendingRequests
                    .Where(r => r.Id == request.RequestId)
                    .Select(r => r.RequestType)
                    .FirstOrDefaultAsync();

                if (type == "VOID")
                {
                    var voidResult = await _voidRefundService.ApproveTransactionVoidAsync(request.RequestId, ownerId);
                    return Json(new { success = voidResult.success, message = voidResult.message });
                }

                if (type == "REFUND")
                {
                    var refundResult = await _voidRefundService.ApproveItemRefundAsync(request.RequestId, ownerId);
                    return Json(new { success = refundResult.success, message = refundResult.message });
                }

                return Json(new { success = false, message = "Request not found" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: VoidRefund/RejectRefund (Owner only). Pending lang ang pwedeng i-reject.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRefund([FromBody] RejectRefundRequest request)
        {
            try
            {
                int ownerId = await GetOwnerIdAsync();
                if (ownerId == 0)
                    return Json(new { success = false, message = "Only Owner can reject" });

                var pending = await _db.PendingRequests
                    .FirstOrDefaultAsync(r => r.Id == request.RequestId
                        && (r.RequestType == "REFUND" || r.RequestType == "VOID")
                        && r.Status == "PENDING");

                if (pending == null)
                    return Json(new { success = false, message = "Request not found or already processed" });

                pending.Status = "REJECTED";
                pending.RejectionReason = request.RejectionReason;
                pending.ApprovedBy = ownerId;
                pending.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = "Request rejected" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // STEP 1: Create Refund Request (Pending muna)
        // STEP 1: Create Refund Request (Laging PENDING - lahat need approval)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRefund([FromBody] ProcessRefundRequest request)
        {
            try
            {
                if (request == null || request.ItemIds == null || request.ItemIds.Count == 0)
                {
                    return Json(new { success = false, message = "No items selected for refund" });
                }

                var userId = GetCurrentUserId();

                // Compute total refund amount for display
                decimal totalAmount = 0;
                foreach (var itemId in request.ItemIds)
                {
                    var orderItem = await _db.OrderItems.FindAsync(itemId);
                    if (orderItem != null)
                    {
                        totalAmount += orderItem.Price * orderItem.Quantity;
                    }
                }

                // Create pending request (LAGING PENDING - kahit Owner pa, dadaan sa approval)
                var pendingRequest = new PendingRequest
                {
                    OrderId = request.OrderId,
                    ItemIds = string.Join(",", request.ItemIds),
                    RequestType = "REFUND",
                    Reason = request.Reason,
                    RequestedBy = userId,
                    Status = "PENDING",
                    RequestedAt = DateTime.Now
                };

                _db.PendingRequests.Add(pendingRequest);
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Refund request of ₱{totalAmount:F2} submitted for owner approval",
                    requestId = pendingRequest.Id
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

       

        // Request model
        public class ProcessRefundRequest
        {
            public int OrderId { get; set; }
            public List<int> ItemIds { get; set; }
            public string Reason { get; set; }
        }

        #region Private Helpers

        private int GetCurrentUserId()
        {
            var staffId = HttpContext.Session.GetInt32("Staff_UserId");
            if (staffId.HasValue && staffId.Value > 0)
                return staffId.Value;

            var ownerId = HttpContext.Session.GetInt32("Owner_UserId");
            if (ownerId.HasValue && ownerId.Value > 0)
                return ownerId.Value;

            return 0;
        }

        private string GetCurrentUserRole()
        {
            var staffId = HttpContext.Session.GetInt32("Staff_UserId");
            if (staffId.HasValue && staffId.Value > 0)
                return "Staff";

            var ownerId = HttpContext.Session.GetInt32("Owner_UserId");
            if (ownerId.HasValue && ownerId.Value > 0)
                return "Owner";

            return "Unknown";
        }

        #endregion
    }

    #region Request Models

    // Request model for approval
    public class ApproveRefundRequest
    {
        public int RequestId { get; set; }  // Ito ay Id ng PendingRequest
    }

    public class RejectRefundRequest
    {
        public int RequestId { get; set; }  // Ito ay Id ng PendingRequest
        public string RejectionReason { get; set; }
    }

    public class VoidItemRequest
    {
        public int OrderItemId { get; set; }
        public string Reason { get; set; }
    }

    public class VoidTransactionRequest
    {
        public int OrderId { get; set; }
        public string Reason { get; set; }
    }

    #endregion
}