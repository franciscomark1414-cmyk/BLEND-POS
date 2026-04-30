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
                // ✅ FIX: Exclude expired batches
                decimal availableQty = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                        && b.QuantityRemaining > 0
                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today)) // ← KEY FIX
                    .SumAsync(b => b.QuantityRemaining);

                int possibleOrders = (int)(availableQty / recipeItem.Quantity);
                if (possibleOrders < maxPossibleOrders)
                    maxPossibleOrders = possibleOrders;

                if (availableQty < recipeItem.Quantity * quantity)
                {
                    insufficientIngredients.Add($"{recipeItem.RawMaterial.Name}: need {recipeItem.Quantity * quantity} {recipeItem.RawMaterial.Unit}, only {availableQty} available");
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

                // Compute original total at discounted total
                decimal originalTotal = request.Quantity * productSize.Price;
                decimal discountedTotal = originalTotal * (1 - request.DiscountPercent);
                decimal discountAmount = originalTotal - discountedTotal;

                // Check raw materials availability
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;
                    decimal availableQty = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                            && b.QuantityRemaining > 0
                            && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                        .SumAsync(b => b.QuantityRemaining);

                    if (availableQty < neededQty)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Insufficient {recipeItem.RawMaterial.Name}. Need {neededQty} {recipeItem.RawMaterial.Unit}, only {availableQty} available."
                        });
                    }
                }

                // Deduct raw materials
                string orderNumber = request.OrderNumber ?? $"ORD-{DateTime.Now:yyyyMMddHHmmss}";
                decimal totalActualCost = 0;

                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;
                    var (success, cost, usedBatches) = await _fifoService.DeductStockWithCostAsync(
                        recipeItem.RawMaterialId,
                        neededQty,
                        productSize.ProductId,
                        orderNumber
                    );

                    if (!success)
                    {
                        return Json(new { success = false, message = $"Failed to deduct {recipeItem.RawMaterial.Name}" });
                    }

                    totalActualCost += cost;
                }

                // ✅ Record sale WITH DISCOUNT AND PAYMENT INFO
                var order = new Order
                {
                    OrderNumber = orderNumber,
                    Date = DateTime.Now,
                    TotalAmount = discountedTotal,  // ← Discounted total
                    Status = "Completed",
                    PaymentMethod = request.PaymentMethod ?? "Cash",  // ← IDAGDAG
                    DiscountType = string.IsNullOrEmpty(request.DiscountType) ? "None" : request.DiscountType,  // ← IDAGDAG
                    DiscountPercent = request.DiscountPercent,  // ← IDAGDAG
                    DiscountAmount = discountAmount,  // ← IDAGDAG
                    ReceiptNumber = $"RC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}"  // ← IDAGDAG (optional)
                };

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                var orderItem = new OrderItem
                {
                    OrderId = order.OrderId,
                    ProductId = productSize.ProductId,
                    ProductSizeId = productSize.ProductSizeId,
                    Quantity = request.Quantity,
                    Price = productSize.Price,  // Original price per unit
                    ActualCost = totalActualCost
                };

                _db.OrderItems.Add(orderItem);
                await _db.SaveChangesAsync();

                // Record performance
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue && userId > 0)
                {
                    await RecordEmployeePerformance(userId.Value, discountedTotal);
                }

                decimal revenue = discountedTotal;
                decimal profit = revenue - totalActualCost;

                return Json(new
                {
                    success = true,
                    message = $"✅ Order #{orderNumber} completed!\n" +
                             $"{request.Quantity} x {productSize.Product.Name} - {productSize.SizeName}\n" +
                             $"Subtotal: ₱{originalTotal:N2}\n" +
                             (request.DiscountPercent > 0 ? $"Discount ({request.DiscountType} {request.DiscountPercent * 100}%): -₱{discountAmount:N2}\n" : "") +
                             $"Total: ₱{revenue:N2}\n" +
                             $"Payment: {request.PaymentMethod}\n" +
                             $"Actual Cost: ₱{totalActualCost:N2}\n" +
                             $"Profit: ₱{profit:N2}",
                    orderNumber = orderNumber,
                    revenue = revenue,
                    actualCost = totalActualCost,
                    profit = profit
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
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
                        // ✅ FIX: Exclude expired batches
                        decimal availableQty = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId
                                && b.QuantityRemaining > 0
                                && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today)) // ← KEY FIX
                            .SumAsync(b => b.QuantityRemaining);

                        int possibleOrders = (int)(availableQty / recipe.Quantity);
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