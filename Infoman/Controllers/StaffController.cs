using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Controllers
{
    public class StaffController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;

        public StaffController(ConnectionDatabase db)
        {
            _db = db;
            _fifoService = new FIFOService(_db);
        }
        private async Task EnsureExpiredBatchesProcessed()
        {
            await _fifoService.ProcessExpiredBatches();
        }
        private bool IsUserLoggedIn()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            return userId.HasValue && userId.Value > 0;
        }

        // POS Main Page
        public async Task<IActionResult> POS()
        {
            await EnsureExpiredBatchesProcessed();

            if (!IsUserLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }
            var userId = HttpContext.Session.GetInt32("UserId");
            var username = HttpContext.Session.GetString("Username") ?? "Staff";

            ViewBag.StaffUsername = username;
            ViewBag.StaffUserId = userId;

            // Get unique categories
            var categories = await _db.Products
                .Where(p => p.Sizes.Any(s => s.IsAvailableInPOS))
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            // Get products with their sizes (grouped by product)
            var products = await _db.Products
                .Include(p => p.Sizes)
                .Where(p => p.Sizes.Any(s => s.IsAvailableInPOS))
                .Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.Category,
                    p.ImagePath,
                    Sizes = p.Sizes.Where(s => s.IsAvailableInPOS).Select(s => new
                    {
                        s.ProductSizeId,
                        s.SizeName,
                        s.Price
                    }).ToList()
                })
                .ToListAsync();

            ViewBag.Categories = categories;
            return View(products);
        }
        // Check availability (base sa raw materials)
        [HttpGet]
        public async Task<IActionResult> CheckAvailability(int productSizeId, int quantity)
        {
            await EnsureExpiredBatchesProcessed();

            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == productSizeId);

            if (productSize == null)
                return Json(new { available = false, message = "Product not found" });

            var insufficientIngredients = new List<string>();
            var maxPossibleOrders = int.MaxValue;
            var today = DateTime.Today;

            foreach (var recipeItem in productSize.RecipeItems)
            {
                // Get available quantity from batches (this is in kg or L)
                decimal availableQty = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                        && b.QuantityRemaining > 0
                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                    .SumAsync(b => b.QuantityRemaining);

                // ✅ FIX: Convert recipeItem.Quantity to kg/L to match availableQty
                decimal quantityPerOrder = recipeItem.Quantity;
                if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                {
                    quantityPerOrder = recipeItem.Quantity / 1000;  // grams/mL → kg/L
                }

                int possibleOrders = (int)(availableQty / quantityPerOrder);
                if (possibleOrders < maxPossibleOrders)
                    maxPossibleOrders = possibleOrders;

                // ✅ FIX: Use converted quantity for comparison
                if (availableQty < quantityPerOrder * quantity)
                {
                    insufficientIngredients.Add($"{recipeItem.RawMaterial.Name}: need {recipeItem.Quantity * quantity} {recipeItem.RawMaterial.Unit}, only {availableQty * 1000} {recipeItem.RawMaterial.Unit} available");
                }
            }

            if (insufficientIngredients.Any())
            {
                return Json(new
                {
                    available = false,
                    maxPossibleOrders = maxPossibleOrders == int.MaxValue ? 0 : maxPossibleOrders,
                    message = string.Join("\n", insufficientIngredients)
                });
            }

            return Json(new
            {
                available = true,
                maxPossibleOrders = maxPossibleOrders == int.MaxValue ? 0 : maxPossibleOrders,
                message = $"Available! Can make up to {maxPossibleOrders} orders."
            });
        }
        // StaffController.cs - PlaceOrder method

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
        {
            System.Diagnostics.Debug.WriteLine("=== PLACE ORDER METHOD HIT ===");
            try
            {
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product not found" });

                if (request.Quantity <= 0)
                    return Json(new { success = false, message = "Invalid quantity" });

                decimal originalTotal = request.Quantity * productSize.Price;
                decimal discountedTotal = originalTotal * (1 - request.DiscountPercent);
                decimal discountAmount = originalTotal - discountedTotal;

                // ✅ CHECK availability lang (huwag munang mag-deduct)
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;
                    decimal availableQty = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                            && b.QuantityRemaining > 0
                            && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                        .SumAsync(b => b.QuantityRemaining);

                    decimal neededQtyInKg = neededQty;
                    if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                    {
                        neededQtyInKg = neededQty / 1000;
                    }

                    if (availableQty < neededQtyInKg)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Insufficient {recipeItem.RawMaterial.Name}. Need {neededQty} {recipeItem.RawMaterial.Unit}, only {availableQty} {recipeItem.RawMaterial.BaseUnit} available."
                        });
                    }
                }

                string orderNumber = request.OrderNumber ?? $"ORD-{DateTime.Now:yyyyMMddHHmmss}";

                // ✅ CREATE ORDER with status = "PENDING_PAYMENT" (hindi pa nadededuct)
                var order = new Order
                {
                    OrderNumber = orderNumber,
                    Date = DateTime.Now,
                    TotalAmount = discountedTotal,
                    Status = "PENDING_PAYMENT",  // ← IMPORTANTE: Hindi pa nadededuct
                    PaymentMethod = request.PaymentMethod ?? "Cash",
                    DiscountType = string.IsNullOrEmpty(request.DiscountType) ? "None" : request.DiscountType,
                    DiscountPercent = request.DiscountPercent,
                    DiscountAmount = discountAmount,
                    ReceiptNumber = $"RC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                    OriginalTotalAmount = discountedTotal,  
                    CreatedAt = DateTime.Now
                };

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                var orderItem = new OrderItem
                {
                    OrderId = order.OrderId,
                    ProductId = productSize.ProductId,
                    ProductSizeId = productSize.ProductSizeId,
                    Quantity = request.Quantity,
                    Price = productSize.Price,
                    ActualCost = 0
                };

                _db.OrderItems.Add(orderItem);
                await _db.SaveChangesAsync();

                // Record performance
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue && userId > 0)
                {
                    await RecordEmployeePerformance(userId.Value, discountedTotal);
                }

                return Json(new
                {
                    success = true,
                    message = $"✅ Order #{orderNumber} created! Payment received.",
                    orderId = order.OrderId,
                    orderNumber = orderNumber,
                    totalAmount = discountedTotal,
                    status = "PENDING_PAYMENT"
                });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += " | INNER: " + ex.InnerException.Message;
                }
                System.Diagnostics.Debug.WriteLine("=== PLACE ORDER ERROR ===");
                System.Diagnostics.Debug.WriteLine(errorMessage);
                return Json(new { success = false, message = errorMessage });
            }
        }
        // ========== ATTENDANCE METHODS FOR STAFF ==========

        // GET: Staff/GetTodayAttendance
        [HttpGet]
        public async Task<IActionResult> GetTodayAttendance()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { isClockedIn = false, hoursToday = 0 });

            var today = DateTime.Today;
            var attendance = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (attendance == null)
            {
                return Json(new { isClockedIn = false, hasClockedOut = false, hoursToday = 0 });
            }

            var hoursWorked = attendance.HoursWorked;
            if (attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue)
            {
                // Currently clocked in, compute running hours
                var now = DateTime.Now.TimeOfDay;
                hoursWorked = (decimal)(now - attendance.CheckInTime.Value).TotalHours;
            }

            return Json(new
            {
                isClockedIn = attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue,
                hasClockedOut = attendance.CheckOutTime.HasValue,
                hoursToday = Math.Round(hoursWorked, 1),
                checkInTime = attendance.CheckInTime?.ToString(@"hh\:mm"),
                checkOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm")
            });
        }

        // POST: Staff/TimeIn
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TimeIn()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "User not logged in" });

            var today = DateTime.Today;
            var existing = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (existing != null && existing.CheckInTime.HasValue)
            {
                return Json(new { success = false, message = "Already clocked in today!" });
            }

            if (existing == null)
            {
                var attendance = new EmployeeAttendance
                {
                    UserId = userId,
                    Date = today,
                    CheckInTime = DateTime.Now.TimeOfDay,
                    Status = AttendanceStatus.Present,
                    HoursWorked = 0
                };
                _db.EmployeeAttendances.Add(attendance);
            }
            else
            {
                existing.CheckInTime = DateTime.Now.TimeOfDay;
                existing.Status = AttendanceStatus.Present;
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, message = $"✅ Time in recorded at {DateTime.Now:hh:mm tt}" });
        }

        // POST: Staff/TimeOut
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TimeOut()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "User not logged in" });

            var today = DateTime.Today;
            var attendance = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (attendance == null || !attendance.CheckInTime.HasValue)
            {
                return Json(new { success = false, message = "You haven't clocked in yet!" });
            }

            if (attendance.CheckOutTime.HasValue)
            {
                return Json(new { success = false, message = "Already clocked out!" });
            }

            attendance.CheckOutTime = DateTime.Now.TimeOfDay;
            var checkIn = attendance.CheckInTime.Value;
            var checkOut = attendance.CheckOutTime.Value;
            attendance.HoursWorked = (decimal)(checkOut - checkIn).TotalHours;

            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"✅ Time out recorded at {DateTime.Now:hh:mm tt}. Hours worked: {attendance.HoursWorked:F1} hrs",
                hoursWorked = attendance.HoursWorked
            });
        }

        // GET: Staff/GetProductWithSizes - para sa size modal
        [HttpGet]
        public async Task<IActionResult> GetProductWithSizes(int productId)
        {
            await EnsureExpiredBatchesProcessed();

            var product = await _db.Products
                .Include(p => p.Sizes)
                    .ThenInclude(s => s.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(p => p.ProductId == productId);

            if (product == null)
                return Json(new { success = false, message = "Product not found" });

            var sizes = new List<object>();
            var today = DateTime.Today;

            foreach (var size in product.Sizes.Where(s => s.IsAvailableInPOS))
            {
                int maxOrders = int.MaxValue;

                if (size.RecipeItems != null && size.RecipeItems.Any())
                {
                    foreach (var recipe in size.RecipeItems)
                    {
                        // Get available quantity from batches (this is in kg or L)
                        decimal availableQty = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId
                                && b.QuantityRemaining > 0
                                && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                            .SumAsync(b => b.QuantityRemaining);

                        // ✅ FIX: Convert recipe.Quantity to kg/L to match availableQty
                        decimal quantityPerOrder = recipe.Quantity;
                        if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                        {
                            quantityPerOrder = recipe.Quantity / 1000;  // grams/mL → kg/L
                        }

                        int possibleOrders = (int)(availableQty / quantityPerOrder);
                        if (possibleOrders < maxOrders)
                            maxOrders = possibleOrders;
                    }
                }

                int availableStock = (maxOrders == int.MaxValue || maxOrders < 0) ? 0 : maxOrders;

                sizes.Add(new
                {
                    size.ProductSizeId,
                    size.SizeName,
                    size.Price,
                    Stock = availableStock
                });
            }

            return Json(new
            {
                success = true,
                productName = product.Name,
                sizes = sizes
            });
        }

        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        private async Task RecordEmployeePerformance(int userId, decimal orderAmount)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("=== RECORD PERFORMANCE CALLED ===");
                System.Diagnostics.Debug.WriteLine($"UserId: {userId}, OrderAmount: {orderAmount}");
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
                        Notes = "Auto-recorded from POS",
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _db.EmployeePerformances.Add(performance);
                }

                var user = await _db.Users.FindAsync(userId);
                if (user != null)
                {
                    var allPerformances = await _db.EmployeePerformances
                        .Where(p => p.UserId == userId)
                        .ToListAsync();

                    user.TotalOrdersProcessed = allPerformances.Sum(p => p.OrdersProcessed);

                    // ✅ SAFE AVERAGE COMPUTATION
                    var ratedPerformances = allPerformances.Where(p => p.Rating > 0).ToList();
                    user.AverageRating = ratedPerformances.Any()
                        ? ratedPerformances.Average(p => p.Rating)
                        : 0;

                    user.UpdatedAt = now;
                }

                await _db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine("SaveChanges completed");
            }
            catch (Exception ex)
            {
                // I-log lang ang error, huwag i-throw para hindi ma-undo ang order
                System.Diagnostics.Debug.WriteLine($"ERROR in RecordEmployeePerformance: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // ========== ORDER STATUS MANAGEMENT ==========

        /// <summary>
        /// Update order status to PREPARING (starting to blend)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> StartPreparing(int orderId)
        {
            var order = await _db.Orders.FindAsync(orderId);
            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            if (order.Status != "PENDING")
                return Json(new { success = false, message = $"Cannot start preparing order with status '{order.Status}'" });

            order.Status = "PREPARING";
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = $"Order #{order.OrderNumber} is now being prepared." });
        }

        /// <summary>
        /// Update order status to COMPLETED (ready to serve)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CompleteOrder(int orderId)
        {
            var order = await _db.Orders.FindAsync(orderId);
            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            if (order.Status != "PREPARING")
                return Json(new { success = false, message = $"Cannot complete order with status '{order.Status}'" });

            order.Status = "COMPLETED";
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = $"Order #{order.OrderNumber} is now ready to serve." });
        }

        /// <summary>
        /// Update order status to CLOSED (paid and finalized)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CloseOrder(int orderId)
        {
            var order = await _db.Orders.FindAsync(orderId);
            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            if (order.Status != "COMPLETED")
                return Json(new { success = false, message = $"Cannot close order with status '{order.Status}'" });

            order.Status = "CLOSED";
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = $"Order #{order.OrderNumber} is now closed." });
        }

        /// <summary>
        /// Get all active orders for kitchen display
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetActiveOrders()
        {
            var orders = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .Where(o => o.Status == "PENDING_PAYMENT")  // ✅ ITO LANG! HUWAG isama ang COMPLETED, VOIDED, REFUNDED
                .Where(o => o.Items.Any(i => !i.IsVoided))  // ✅ MAY AT LEAST ISANG ACTIVE ITEM
                .OrderBy(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    Items = o.Items.Where(i => !i.IsVoided && !i.IsRefunded).Select(oi => new
                    {
                        oi.OrderItemId,
                        ProductName = oi.Product.Name,
                        SizeName = oi.ProductSize.SizeName,
                        oi.Quantity,
                        oi.Price
                    })
                })
                .ToListAsync();

            return Json(orders);
        }
        /// <summary>
        /// Get single order details
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetOrder(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            return Json(new
            {
                success = true,
                order = new
                {
                    order.OrderId,
                    order.OrderNumber,
                    order.Status,
                    order.TotalAmount,
                    order.Date,
                    order.PaymentMethod,
                    order.DiscountType,
                    order.DiscountPercent,
                    order.DiscountAmount,
                    Items = order.Items.Where(i => !i.IsVoided).Select(oi => new
                    {
                        oi.OrderItemId,
                        ProductName = oi.Product.Name,
                        SizeName = oi.ProductSize.SizeName,
                        oi.Quantity,
                        oi.Price,
                        Subtotal = oi.Price * oi.Quantity
                    })
                }
            });
        }

        // ========== BLENDING / PREPARATION ==========
        [HttpPost]
        public async Task<IActionResult> StartBlending([FromBody] StartBlendingRequest request)
        {

            
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                        .ThenInclude(ps => ps.RecipeItems)
                            .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            if (order.Status != "PENDING_PAYMENT")
                return Json(new { success = false, message = $"Cannot start blending. Order status is '{order.Status}'" });

            decimal totalActualCost = 0;

            foreach (var orderItem in order.Items)
            {
                var productSize = orderItem.ProductSize;
                decimal itemCost = 0;

                foreach (var recipe in productSize.RecipeItems)
                {
                    var material = await _db.RawMaterials.FindAsync(recipe.RawMaterialId);
                    if (material == null) continue;

                    System.Diagnostics.Debug.WriteLine($"=== START BLENDING DEBUG ===");
                    System.Diagnostics.Debug.WriteLine($"Material: {material.Name}");
                    System.Diagnostics.Debug.WriteLine($"material.Unit: {material.Unit}");
                    System.Diagnostics.Debug.WriteLine($"material.BaseUnit: {material.BaseUnit}");
                    System.Diagnostics.Debug.WriteLine($"recipe.Quantity: {recipe.Quantity}");
                    System.Diagnostics.Debug.WriteLine($"recipe.UnitUsed: {recipe.UnitUsed}");


                    // recipe.Quantity is stored in BaseUnit (g or mL)
                    // batches are stored in material.Unit (kg or L)
                    decimal totalInBaseUnit = recipe.Quantity * orderItem.Quantity;
                    decimal totalForBatch = totalInBaseUnit;

                    System.Diagnostics.Debug.WriteLine($"totalInBaseUnit: {totalInBaseUnit}");
                    System.Diagnostics.Debug.WriteLine($"totalForBatch: {totalForBatch}");

                    // Convert from BaseUnit to Unit (how batches are stored)
                    if (material.BaseUnit == "g" && material.Unit == "kg")
                        totalForBatch = totalInBaseUnit / 1000;  // 180g → 0.18 kg
                    else if (material.BaseUnit == "mL" && material.Unit == "L")
                        totalForBatch = totalInBaseUnit / 1000;  // 60mL → 0.06 L
                                                                 // else: pcs, or same unit — no conversion needed

                    System.Diagnostics.Debug.WriteLine($"[BLEND] {material.Name}: {recipe.Quantity} {material.BaseUnit} → {totalForBatch} {material.Unit}");

                    var (success, cost, usedBatches) = await _fifoService.DeductStockWithCostAsync(
    recipe.RawMaterialId,
    totalForBatch,
    productSize.ProductId,
    order.OrderNumber,
    orderId: order.OrderId,                   
    productSizeId: orderItem.ProductSizeId     
);

                    if (!success)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Failed to deduct {material.Name}. Need {totalForBatch:F4} {material.Unit}, check batch stock."
                        });
                    }

                    itemCost += cost;
                }

                orderItem.ActualCost = itemCost;
                totalActualCost += itemCost;
            }

            order.Status = "COMPLETED";
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"✅ Order #{order.OrderNumber} completed! Ingredients deducted.",
                actualCost = totalActualCost
            });
        }

        public class StartBlendingRequest
        {
            public int OrderId { get; set; }
        }
        public class PlaceOrderRequest
        {
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
            public string? OrderNumber { get; set; }
            public string? PaymentMethod { get; set; }     
            public string? DiscountType { get; set; }       
            public decimal DiscountPercent { get; set; }
        }
    }
}