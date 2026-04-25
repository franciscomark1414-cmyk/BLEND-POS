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
        private bool IsUserLoggedIn()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            return userId.HasValue && userId.Value > 0;
        }

        // POS Main Page
        public async Task<IActionResult> POS()
        {
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

                // Deduct raw materials at kunin ang actual cost
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

                // Record sale
                var order = new Order
                {
                    OrderNumber = orderNumber,
                    Date = DateTime.Now,
                    TotalAmount = request.Quantity * productSize.Price,
                    Status = "Completed"
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
                    ActualCost = totalActualCost
                };

                _db.OrderItems.Add(orderItem);
                await _db.SaveChangesAsync();


                // ========== ✅ BAGONG CODE: RECORD PERFORMANCE ==========
                // After ng order creation, bago tawagin ang RecordEmployeePerformance
                var userId = HttpContext.Session.GetInt32("UserId");
                System.Diagnostics.Debug.WriteLine($"=== PLACE ORDER ===");
                System.Diagnostics.Debug.WriteLine($"Session UserId: {userId}");

                if (userId.HasValue && userId > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Calling RecordEmployeePerformance for UserId: {userId.Value}");
                    await RecordEmployeePerformance(userId.Value, request.Quantity * productSize.Price);
                    System.Diagnostics.Debug.WriteLine("RecordEmployeePerformance completed");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("UserId is NULL or ZERO! Cannot record performance.");
                }

                decimal revenue = request.Quantity * productSize.Price;
                decimal profit = revenue - totalActualCost;

                return Json(new
                {
                    success = true,
                    message = $"✅ Order #{orderNumber} completed!\n" +
                             $"{request.Quantity} x {productSize.Product.Name} - {productSize.SizeName}\n" +
                             $"Revenue: ₱{revenue:N2}\n" +
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
            System.Diagnostics.Debug.WriteLine("=== RECORD PERFORMANCE CALLED ===");
            System.Diagnostics.Debug.WriteLine($"UserId: {userId}, OrderAmount: {orderAmount}");
            var today = DateTime.Today;
            var now = DateTime.Now;

            // Hanapin kung may existing performance record na ngayong araw
            var existingPerformance = await _db.EmployeePerformances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Date.Date == today);

            if (existingPerformance != null)
            {
                // I-update ang existing record
                existingPerformance.TotalSales += orderAmount;
                existingPerformance.OrdersProcessed += 1;
                existingPerformance.UpdatedAt = now;
            }
            else
            {
                // Gumawa ng bagong record
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

            // I-update din ang User table summary
            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                var allPerformances = await _db.EmployeePerformances
                    .Where(p => p.UserId == userId)
                    .ToListAsync();

                user.TotalOrdersProcessed = allPerformances.Sum(p => p.OrdersProcessed);
                user.AverageRating = allPerformances.Any()
                    ? allPerformances.Where(p => p.Rating > 0).Average(p => p.Rating)
                    : 0;
                user.UpdatedAt = now;  // ← IDAGDAG MO ITO
            }

            await _db.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine("SaveChanges completed");
        }
        
        public class PlaceOrderRequest
        {
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
            public string? OrderNumber { get; set; }
        }
    }
}