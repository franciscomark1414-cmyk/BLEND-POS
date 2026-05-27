using Infoman.Data;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class ReturnExchangeController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly ExchangeService _exchangeService;
        private readonly FIFOService _fifoService;

        public ReturnExchangeController(ConnectionDatabase db)
        {
            _db = db;
            _fifoService = new FIFOService(_db);
            _exchangeService = new ExchangeService(db, _fifoService);
        }

        private int GetCurrentUserId()
        {
            // ✅ POS user uses Staff_UserId
            var staffUserId = HttpContext.Session.GetInt32("Staff_UserId");
            if (staffUserId.HasValue && staffUserId.Value > 0)
                return staffUserId.Value;

            // ✅ Owner user uses Owner_UserId
            var ownerUserId = HttpContext.Session.GetInt32("Owner_UserId");
            if (ownerUserId.HasValue && ownerUserId.Value > 0)
                return ownerUserId.Value;

            // ✅ Fallback to generic UserId
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        [HttpGet]
        public async Task<IActionResult> GetCompletedOrderDetails(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.Status == "COMPLETED");

            if (order == null)
                return Json(new { success = false, message = "Order not found or not completed" });

            decimal discountPercent = order.DiscountPercent;

            return Json(new
            {
                success = true,
                order = new
                {
                    order.OrderId,
                    order.OrderNumber,
                    order.TotalAmount,
                    order.Date,
                    discountPercent = discountPercent,  // ✅ ISAMA ANG DISCOUNT PERCENT
                    Items = order.Items.Where(i => !i.IsRefunded && !i.IsVoided).Select(i => new
                    {
                        i.OrderItemId,
                        ProductName = i.Product.Name,
                        SizeName = i.ProductSize.SizeName,
                        i.Quantity,
                        OriginalPrice = i.Price,
                        Price = discountPercent > 0 ? i.Price * (1 - discountPercent) : i.Price,  // ✅ ACTUAL PRICE
                        Subtotal = (discountPercent > 0 ? i.Price * (1 - discountPercent) : i.Price) * i.Quantity
                    })
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetProductsForExchange(int? excludeOrderId = null)
        {
            var query = _db.ProductSizes
                .Include(ps => ps.Product)
                .Where(ps => ps.IsAvailableInPOS && !ps.IsDeleted);

            // ✅ I-exclude ang mga produkto na nasa order (kung may excludeOrderId)
            if (excludeOrderId.HasValue && excludeOrderId.Value > 0)
            {
                var excludeProductSizeIds = await _db.OrderItems
                    .Where(oi => oi.OrderId == excludeOrderId.Value && !oi.IsVoided && !oi.IsRefunded)
                    .Select(oi => oi.ProductSizeId)
                    .Distinct()
                    .ToListAsync();

                query = query.Where(ps => !excludeProductSizeIds.Contains(ps.ProductSizeId));
            }

            var products = await query
                .Select(ps => new
                {
                    ps.ProductSizeId,
                    ProductName = ps.Product.Name,
                    ps.SizeName,
                    ps.Price,
                    ps.SKU
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessExchange([FromBody] ExchangeRequest request)
        {
            if (request == null || request.ItemsToReturn == null || request.ItemsToReplace == null)
                return Json(new { success = false, message = "Invalid request data" });

            var userId = GetCurrentUserId();
            if (userId == 0)
                return Json(new { success = false, message = "User not logged in" });

            if (request.ItemsToReturn.Sum(i => i.Quantity) == 0)
                return Json(new { success = false, message = "No items selected to return" });

            if (request.ItemsToReplace.Sum(i => i.Quantity) == 0)
                return Json(new { success = false, message = "No replacement items selected" });

            // ✅ Convert to service format (using fully qualified name)
            var serviceItemsToReturn = request.ItemsToReturn.Select(i => new Infoman.Service.ExchangeItemRequest
            {
                OrderItemId = i.OrderItemId,
                ProductSizeId = i.ProductSizeId,
                Quantity = i.Quantity
            }).ToList();

            var serviceItemsToReplace = request.ItemsToReplace.Select(i => new Infoman.Service.ExchangeItemRequest
            {
                OrderItemId = i.OrderItemId,
                ProductSizeId = i.ProductSizeId,
                Quantity = i.Quantity
            }).ToList();

            // Call ExchangeService
            var result = await _exchangeService.ProcessExchangeAsync(
                request.OrderId,
                userId,
                serviceItemsToReturn,
                serviceItemsToReplace,
                request.Reason,
                request.PaymentForDifference
            );

            if (result.success)
            {
                return Json(new
                {
                    success = true,
                    message = result.message,
                    newOrderId = result.newOrderId,
                    originalOrderId = request.OrderId
                });
            }
            else
            {
                return Json(new { success = false, message = result.message });
            }
        }

        // ✅ Request Models (bagong pangalan para hindi mag-conflict)
        public class ExchangeRequest
        {
            public int OrderId { get; set; }
            public List<ExchangeItemRequestDto> ItemsToReturn { get; set; }
            public List<ExchangeItemRequestDto> ItemsToReplace { get; set; }
            public string Reason { get; set; }
            public bool UseStoreCredit { get; set; }
            public string? PaymentForDifference { get; set; }
        }

        // ✅ PINALITAN NG PANGALAN: ExchangeItemRequestDto
        public class ExchangeItemRequestDto
        {
            public int OrderItemId { get; set; }
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
        }
    }
}