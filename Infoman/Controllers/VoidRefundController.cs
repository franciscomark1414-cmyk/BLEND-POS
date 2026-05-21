using Infoman.Data;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class VoidRefundController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly VoidRefundService _voidRefundService;
        private readonly RefundService _refundService;  // ✅ ADD THIS

        public VoidRefundController(ConnectionDatabase db)
        {
            _db = db;
            _voidRefundService = new VoidRefundService(_db);
            _refundService = new RefundService(_db);  // ✅ ADD THIS
        }

        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        private string GetCurrentUserRole()
        {
            return HttpContext.Session.GetString("UserRole") ?? "Staff";
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderDetails(int orderId)
        {
            var details = await _voidRefundService.GetOrderDetails(orderId);
            if (details == null)
                return Json(new { success = false, message = "Order not found" });

            return Json(new { success = true, order = details });
        }

        [HttpGet]
        public async Task<IActionResult> GetPendingOrders()
        {
            var orders = await _voidRefundService.GetPendingOrders();
            return Json(orders);
        }

        [HttpGet]
        public async Task<IActionResult> GetCompletedOrders()
        {
            var orders = await _voidRefundService.GetCompletedOrders();
            return Json(orders);
        }

        [HttpPost]
        public async Task<IActionResult> VoidItem([FromBody] VoidItemRequest request)
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userId == 0)
                return Json(new { success = false, message = "User not logged in" });

            var result = await _voidRefundService.VoidItem(
                request.OrderItemId, userId, userRole, request.Reason);

            if (result.needsOwner)
            {
                return Json(new { success = false, requiresOwner = true, message = result.message });
            }

            return Json(new { success = result.success, message = result.message });
        }

        [HttpPost]
        public async Task<IActionResult> VoidTransaction([FromBody] VoidTransactionRequest request)
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userId == 0)
                return Json(new { success = false, message = "User not logged in" });

            var result = await _voidRefundService.VoidTransaction(
                request.OrderId, userId, userRole, request.Reason);

            if (result.needsOwner)
            {
                return Json(new { success = false, requiresOwner = true, message = result.message });
            }

            return Json(new { success = result.success, message = result.message });
        }

        // ✅ UPDATED: Use RefundService, not VoidRefundService
        [HttpPost]
        public async Task<IActionResult> ProcessRefund([FromBody] RefundRequest request)
        {
            var userId = GetCurrentUserId();
            var userRole = GetCurrentUserRole();

            if (userId == 0)
                return Json(new { success = false, message = "User not logged in" });

            // ✅ Use _refundService, not _voidRefundService
            var result = await _refundService.RequestRefundAsync(
                request.OrderId, request.ItemIds, request.Reason, userId);

            if (result.success)
            {
                return Json(new { success = true, requiresOwner = true, message = result.message, requestId = result.requestId });
            }

            return Json(new { success = false, message = result.message });
        }

        // Request Models
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

        public class RefundRequest
        {
            public int OrderId { get; set; }
            public List<int> ItemIds { get; set; }
            public string Reason { get; set; }
        }
    }
}