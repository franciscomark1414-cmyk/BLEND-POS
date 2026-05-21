// Controllers/ReturnExchangeController.cs
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

            return Json(new
            {
                success = true,
                order = new
                {
                    order.OrderId,
                    order.OrderNumber,
                    order.TotalAmount,
                    order.Date,
                    Items = order.Items.Where(i => !i.IsRefunded && !i.IsVoided).Select(i => new
                    {
                        i.OrderItemId,
                        ProductName = i.Product.Name,
                        SizeName = i.ProductSize.SizeName,
                        i.Quantity,
                        i.Price,
                        Subtotal = i.Price * i.Quantity
                    })
                }
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetProductsForExchange()
        {
            var products = await _db.ProductSizes
                .Include(ps => ps.Product)
                .Where(ps => ps.IsAvailableInPOS)
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

        // ✅ Convert Controller's ExchangeItemRequest to Service's ExchangeItemRequest
        var serviceItemsToReturn = request.ItemsToReturn.Select(i => new global::Infoman.Service.ExchangeItemRequest
        {
            OrderItemId = i.OrderItemId,
            ProductSizeId = i.ProductSizeId,
            Quantity = i.Quantity
        }).ToList();

        var serviceItemsToReplace = request.ItemsToReplace.Select(i => new global::Infoman.Service.ExchangeItemRequest
        {
            OrderItemId = i.OrderItemId,
            ProductSizeId = i.ProductSizeId,
            Quantity = i.Quantity
        }).ToList();

        var result = await _exchangeService.ProcessExchangeAsync(
            request.OrderId,
            userId,
            serviceItemsToReturn,      // ✅ Use converted list
            serviceItemsToReplace,     // ✅ Use converted list
            request.Reason
        );

        if (result.success)
            return Json(new { success = true, message = result.message, exchangeId = result.exchangeId });
        else
            return Json(new { success = false, message = result.message });
    }

    // Request Models for Controller
    public class ExchangeRequest
    {
        public int OrderId { get; set; }
        public List<ExchangeItemRequest> ItemsToReturn { get; set; }
        public List<ExchangeItemRequest> ItemsToReplace { get; set; }
        public string Reason { get; set; }
        public bool UseStoreCredit { get; set; }
    }

    // ✅ IBAHIN ANG PANGALAN para hindi mag-conflict sa Service version
    public class ExchangeItemRequest
    {
        public int OrderItemId { get; set; }
        public int ProductSizeId { get; set; }
        public int Quantity { get; set; }
    }

}
}