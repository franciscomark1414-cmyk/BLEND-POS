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

        // GET: VoidRefund/GetOrderDetails?orderId=5
        [HttpGet]
        public async Task<IActionResult> GetOrderDetails(int orderId)
        {
            try
            {
                var orderDetails = await _voidRefundService.GetOrderDetails(orderId);
                if (orderDetails == null)
                    return Json(new { success = false, message = "Order not found" });

                return Json(new { success = true, order = orderDetails });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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