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

        // POS Main Page
        public async Task<IActionResult> POS()
        {
            // Get staff info from session
            var userId = HttpContext.Session.GetInt32("UserId");
            var username = HttpContext.Session.GetString("Username") ?? "Staff";
            var userRole = HttpContext.Session.GetString("UserRole") ?? "Staff";

            ViewBag.StaffUsername = username;
            ViewBag.StaffUserId = userId;

            // Your existing code...
            var categories = await _db.Products
                .Where(p => p.Sizes.Any(s => s.IsAvailableInPOS))
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            var availableProducts = await _db.ProductSizes
                .Include(ps => ps.Product)
                .Include(ps => ps.RecipeItems)
                .Where(ps => ps.IsAvailableInPOS == true)
                .Select(ps => new
                {
                    ps.ProductSizeId,
                    ProductName = ps.Product.Name,
                    ProductCategory = ps.Product.Category,
                    ps.SizeName,
                    ps.Price,

                    ImagePath = ps.Product.ImagePath,

                    RecipeItems = ps.RecipeItems.Select(r => new
                    {
                        r.RawMaterialId,
                        r.Quantity,
                        RawMaterialName = r.RawMaterial.Name,
                        RawMaterialUnit = r.RawMaterial.Unit
                    }).ToList()
                })
                .ToListAsync();

            ViewBag.Categories = categories;
            return View(availableProducts);
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

            foreach (var recipeItem in productSize.RecipeItems)
            {
                decimal availableQty = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipeItem.RawMaterialId)
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
                    maxPossibleOrders = maxPossibleOrders,
                    message = string.Join("\n", insufficientIngredients)
                });
            }

            return Json(new
            {
                available = true,
                maxPossibleOrders = maxPossibleOrders,
                message = $"Available! Can make up to {maxPossibleOrders} orders."
            });
        }

        // StaffController.cs - PlaceOrder method

        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();

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

                // Check raw materials availability (excluding expired)
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;
                    decimal availableQty = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                            && b.QuantityRemaining > 0
                            && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today)) // Skip expired
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

                // Deduct raw materials at kunin ang actual cost ng bawat ingredient
                string orderNumber = request.OrderNumber ?? $"ORD-{DateTime.Now:yyyyMMddHHmmss}";
                decimal totalActualCost = 0;
                var costBreakdown = new List<object>();

                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;

                    // ✅ FIXED: May AWAIT at tamang method name (ASYNC)
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

                    // I-save ang cost breakdown (opsyonal)
                    costBreakdown.Add(new
                    {
                        Ingredient = recipeItem.RawMaterial.Name,
                        Quantity = neededQty,
                        Unit = recipeItem.RawMaterial.Unit,
                        ActualCost = cost,
                        BatchesUsed = usedBatches.Select(b => new { b.BatchId, b.UnitPrice, b.QuantityUsed })
                    });
                }

                // Record sale
                var order = new Order
                {
                    OrderNumber = orderNumber,
                    Date = DateTime.Now,
                    TotalAmount = request.Quantity * productSize.Price,  // Revenue
                    Status = "Completed"
                };

                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                // I-record ang OrderItem na may ACTUAL COST
                var orderItem = new OrderItem
                {
                    OrderId = order.OrderId,
                    ProductId = productSize.ProductId,
                    ProductSizeId = productSize.ProductSizeId,
                    Quantity = request.Quantity,
                    Price = productSize.Price,           // Selling price (fixed)
                    ActualCost = totalActualCost         // ← ACTUAL COST FROM BATCHES
                };

                _db.OrderItems.Add(orderItem);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // Compute profit
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
                await transaction.RollbackAsync();
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

        // Helper method to get current user ID from session
        private int GetCurrentUserId()
        {
            var userIdString = HttpContext.Session.GetString("UserId");
            return int.TryParse(userIdString, out int id) ? id : 0;
        }

        public class PlaceOrderRequest
        {
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
            public string? OrderNumber { get; set; }
        }
    }
}