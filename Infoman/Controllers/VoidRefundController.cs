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

        // POST: VoidRefund/VoidItem
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidItem([FromBody] VoidItemRequest request)
        {
            try
            {
                if (request == null || request.OrderItemId == 0)
                    return Json(new { success = false, message = "Invalid request" });

                if (string.IsNullOrWhiteSpace(request.Reason))
                    return Json(new { success = false, message = "Reason is required" });

                var userId = GetCurrentUserId();
                var userRole = GetCurrentUserRole();

                var result = await _voidRefundService.VoidItem(
                    request.OrderItemId,
                    userId,
                    userRole,
                    request.Reason
                );

                if (result.needsOwner)
                {
                    return Json(new
                    {
                        success = false,
                        requiresOwner = true,
                        message = result.message
                    });
                }

                if (!result.success)
                {
                    return Json(new { success = false, message = result.message });
                }

                return Json(new
                {
                    success = true,
                    message = result.message,
                    refundAmount = result.refundAmount,
                    newOrderTotal = result.newOrderTotal,
                    updatedItems = result.updatedItems,
                    isPartialRefund = result.newOrderTotal > 0,
                    isFullRefund = result.newOrderTotal == 0
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: VoidRefund/VoidTransaction
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

                var userId = GetCurrentUserId();
                var userRole = GetCurrentUserRole();

                var result = await _voidRefundService.VoidTransaction(
                    request.OrderId,
                    userId,
                    userRole,
                    request.Reason
                );

                if (result.needsOwner)
                {
                    return Json(new
                    {
                        success = false,
                        requiresOwner = true,
                        message = result.message
                    });
                }

                if (!result.success)
                {
                    return Json(new { success = false, message = result.message });
                }

                return Json(new
                {
                    success = true,
                    message = result.message,
                    refundAmount = result.refundAmount,
                    updatedItems = result.updatedItems,
                    isFullRefund = true
                });
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

        // STEP 2: Approve Refund (Owner only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRefund([FromBody] ApproveRefundRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _db.Users.FindAsync(userId);

                if (user == null || user.Role != "Owner")
                {
                    return Json(new { success = false, message = "Only Owner can approve refunds" });
                }

                var pendingRequest = await _db.PendingRequests
                    .Include(r => r.Order)
                    .FirstOrDefaultAsync(r => r.Id == request.RequestId && r.RequestType == "REFUND" && r.Status == "PENDING");

                if (pendingRequest == null)
                {
                    return Json(new { success = false, message = "Refund request not found" });
                }

                // Parse item IDs
                var itemIds = pendingRequest.ItemIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToList();

                decimal totalRefundAmount = 0;
                var order = pendingRequest.Order;

                // Process each item
                foreach (var itemId in itemIds)
                {
                    var orderItem = await _db.OrderItems
                        .Include(oi => oi.ProductSize)
                        .FirstOrDefaultAsync(oi => oi.OrderItemId == itemId);

                    if (orderItem != null && !orderItem.IsRefunded && !orderItem.IsVoided)
                    {
                        orderItem.IsRefunded = true;
                        orderItem.RefundReason = pendingRequest.Reason;
                        orderItem.DispositionStatusId = 3; // REFUNDED

                        totalRefundAmount += orderItem.Price * orderItem.Quantity;
                    }
                }

                // Generate refund reference number
                string refundReference = $"REF-{pendingRequest.OrderId}-{DateTime.Now:yyyyMMddHHmmss}";

                // Create adjustment
                var adjustment = new OrderAdjustment
                {
                    OriginalOrderId = pendingRequest.OrderId,
                    AdjustmentType = "Refund",
                    Amount = -totalRefundAmount,
                    Reason = pendingRequest.Reason,
                    ReferenceNumber = refundReference,
                    Status = "COMPLETED",
                    CreatedBy = userId,
                    CreatedAt = DateTime.Now
                };
                _db.OrderAdjustments.Add(adjustment);
                await _db.SaveChangesAsync();

                // Create refund transaction
                var refundTransaction = new RefundTransaction
                {
                    AdjustmentId = adjustment.AdjustmentId,
                    OriginalOrderId = pendingRequest.OrderId,
                    Amount = totalRefundAmount,
                    Reason = pendingRequest.Reason,
                    ProcessedBy = userId,
                    RefundReference = refundReference,
                    CreatedAt = DateTime.Now
                };
                _db.RefundTransactions.Add(refundTransaction);

                // Update order totals
                decimal newTotal = order.TotalAmount - totalRefundAmount;
                order.TotalAmount = newTotal > 0 ? newTotal : 0;
                order.TotalRefundAmount = order.TotalRefundAmount + totalRefundAmount;

                // Update pending request status
                pendingRequest.Status = "APPROVED";
                pendingRequest.ApprovedBy = userId;
                pendingRequest.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Refund of ₱{totalRefundAmount:F2} processed successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // STEP 3: Reject Refund
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRefund([FromBody] RejectRefundRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var user = await _db.Users.FindAsync(userId);

                if (user == null || user.Role != "Owner")
                {
                    return Json(new { success = false, message = "Only Owner can reject refunds" });
                }

                // ITO ANG PINALITAN - gamit ang PendingRequest, hindi RefundRequest
                var pendingRequest = await _db.PendingRequests
                    .FirstOrDefaultAsync(r => r.Id == request.RequestId && r.RequestType == "REFUND" && r.Status == "PENDING");

                if (pendingRequest == null)
                {
                    return Json(new { success = false, message = "Refund request not found" });
                }

                pendingRequest.Status = "REJECTED";
                pendingRequest.RejectionReason = request.RejectionReason;
                pendingRequest.ApprovedBy = userId;
                pendingRequest.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Refund request rejected" });
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