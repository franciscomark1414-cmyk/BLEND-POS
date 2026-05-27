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
        private bool IsStaffLoggedIn()
        {
            var userId = HttpContext.Session.GetInt32("Staff_UserId");
            return userId.HasValue && userId.Value > 0;
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

            if (!IsStaffLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            var userId = GetCurrentUserId();
            var username = HttpContext.Session.GetString("Staff_Username") ?? "Staff";

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
                // ========== 1. CHECK IF USER IS LOGGED IN ==========
                var userId = GetCurrentUserId();
                if (userId == 0)
                {
                    return Json(new { success = false, message = "User not logged in", requiresTimeIn = true });
                }

                // ========== 2. CHECK IF STAFF IS CLOCKED IN ==========
                var today = DateTime.Today;
                var attendance = await _db.EmployeeAttendances
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

                if (attendance == null || !attendance.CheckInTime.HasValue)
                {
                    return Json(new { success = false, message = "⚠️ You must TIME IN first!", requiresTimeIn = true });
                }

                if (attendance.CheckOutTime.HasValue)
                {
                    return Json(new { success = false, message = "⚠️ You have already clocked out!", requiresTimeIn = true });
                }

                // ========== 3. VALIDATE PRODUCT ==========
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product not found" });

                if (request.Quantity <= 0)
                    return Json(new { success = false, message = "Invalid quantity" });

                // ========== 4. CHECK INGREDIENT AVAILABILITY ==========
                decimal originalTotal = request.Quantity * productSize.Price;
                decimal discountedTotal = originalTotal * (1 - request.DiscountPercent);
                decimal discountAmount = originalTotal - discountedTotal;

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
                            message = $"Insufficient {recipeItem.RawMaterial.Name}. Need {neededQty} {recipeItem.RawMaterial.Unit}"
                        });
                    }
                }

                // ========== 5. CHECK IF ORDER ALREADY EXISTS ==========
                var existingOrder = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.OrderNumber == request.OrderNumber && o.Status == "PENDING_PAYMENT");

                int orderId;
                decimal newTotalAmount = 0;

                if (existingOrder != null)
                {
                    // ✅ ADD ITEM TO EXISTING ORDER
                    orderId = existingOrder.OrderId;

                    var orderItem = new OrderItem
                    {
                        OrderId = existingOrder.OrderId,
                        ProductId = productSize.ProductId,
                        ProductSizeId = productSize.ProductSizeId,
                        Quantity = request.Quantity,
                        Price = productSize.Price,
                        ActualCost = 0,
                        DispositionStatusId = 1
                    };
                    _db.OrderItems.Add(orderItem);

                    // Update order totals
                    decimal newSubtotal = existingOrder.OriginalTotalAmount + (request.Quantity * productSize.Price);
                    decimal newDiscountedTotalAmount = newSubtotal * (1 - request.DiscountPercent);
                    decimal newDiscountAmountValue = newSubtotal - newDiscountedTotalAmount;

                    existingOrder.OriginalTotalAmount = newSubtotal;
                    existingOrder.TotalAmount = newDiscountedTotalAmount;
                    existingOrder.DiscountAmount = newDiscountAmountValue;

                    await _db.SaveChangesAsync();
                    newTotalAmount = newDiscountedTotalAmount;
                }
                else
                {
                    // ✅ CREATE NEW ORDER
                    string orderNumber = request.OrderNumber ?? $"ORD-{DateTime.Now:yyyyMMddHHmmss}";

                    var newOrder = new Order
                    {
                        OrderNumber = orderNumber,
                        Date = DateTime.Now,
                        TotalAmount = discountedTotal,
                        Status = "PENDING_PAYMENT",  // Still PENDING until payment is recorded
                        PaymentMethod = request.PaymentMethod ?? "Cash",
                        DiscountType = string.IsNullOrEmpty(request.DiscountType) ? "None" : request.DiscountType,
                        DiscountPercent = request.DiscountPercent,
                        DiscountAmount = discountAmount,
                        ReceiptNumber = $"RC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                        OriginalTotalAmount = discountedTotal,
                        CreatedAt = DateTime.Now,
                        StaffUserId = userId
                    };

                    _db.Orders.Add(newOrder);
                    await _db.SaveChangesAsync();
                    orderId = newOrder.OrderId;
                    newTotalAmount = discountedTotal;

                    var orderItem = new OrderItem
                    {
                        OrderId = newOrder.OrderId,
                        ProductId = productSize.ProductId,
                        ProductSizeId = productSize.ProductSizeId,
                        Quantity = request.Quantity,
                        Price = productSize.Price,
                        ActualCost = 0,
                        DispositionStatusId = 1
                    };
                    _db.OrderItems.Add(orderItem);
                    await _db.SaveChangesAsync();
                }

                // ========== ✅ 6. CREATE PAYMENT RECORD (POSITIVE AMOUNT) ==========
                // ✅ This is where payment is recorded - when order is confirmed
                var payment = new Payment
                {
                    OrderId = orderId,
                    Amount = newTotalAmount,  // ✅ POSITIVE AMOUNT (not negative)
                    PaymentMethod = request.PaymentMethod ?? "Cash",
                    PaymentReference = $"PAY-{DateTime.Now:yyyyMMddHHmmss}",
                    Status = "PAID",  // ✅ PAID status
                    PaymentDate = DateTime.Now,
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(payment);

                // ========== 7. UPDATE ORDER STATUS TO COMPLETED (since payment is received) ==========
                var orderToUpdate = await _db.Orders.FindAsync(orderId);
                if (orderToUpdate != null)
                {
                    orderToUpdate.Status = "PENDING_BLENDING"; // ✅ ORDER IS COMPLETED AT CONFIRMATION
                }

                await _db.SaveChangesAsync();

                // ========== 8. RECORD EMPLOYEE PERFORMANCE ==========
                await RecordEmployeePerformance(userId, newTotalAmount);

                return Json(new
                {
                    success = true,
                    message = $"✅ Order confirmed! Payment received: ₱{newTotalAmount:F2}",
                    orderId = orderId,
                    orderNumber = request.OrderNumber,
                    totalAmount = newTotalAmount,
                    status = "PENDING_BLENDING"// ✅ Return COMPLETED status
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

        // GET: Staff/CheckTimeInStatus
        [HttpGet]
        public async Task<IActionResult> CheckTimeInStatus()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return Json(new { isClockedIn = false, message = "Not logged in" });

            var today = DateTime.Today;
            var attendance = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            var isClockedIn = attendance != null && attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue;
            var isClockedOut = attendance != null && attendance.CheckOutTime.HasValue;

            return Json(new
            {
                isClockedIn = isClockedIn,
                isClockedOut = isClockedOut,
                message = isClockedIn ? "Clocked in - can take orders" : "Not clocked in - please time in first"
            });
        }

        // ========== ATTENDANCE METHODS FOR STAFF ==========

        // GET: Staff/GetTodayAttendance
        [HttpGet]
        public async Task<IActionResult> GetTodayAttendance()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { isClockedIn = false, hoursToday = 0, hasClockedOut = false });

            var today = DateTime.Today;
            var attendance = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            if (attendance == null)
            {
                return Json(new { isClockedIn = false, hasClockedOut = false, hoursToday = 0 });
            }

            decimal hoursWorked = attendance.HoursWorked;
            if (attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue)
            {
                var now = DateTime.Now.TimeOfDay;
                hoursWorked = (decimal)(now - attendance.CheckInTime.Value).TotalHours;
            }

            return Json(new
            {
                isClockedIn = attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue,  // ✅ CLocked, hindi Locked
                hasClockedOut = attendance.CheckOutTime.HasValue,  // ✅ CLocked, hindi Locked
                hoursToday = Math.Round(hoursWorked, 1),
                checkInTime = attendance.CheckInTime?.ToString(@"hh\:mm"),
                checkOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm")
            });
        }

        // POST: Staff/TimeIn
        // POST: Staff/TimeIn - Modified to allow multiple time in/out
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TimeIn()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "User not logged in" });

            var today = DateTime.Today;
            var existing = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            // ✅ Allow new time in even if already clocked out (for multiple shifts)
            if (existing != null && existing.CheckInTime.HasValue && !existing.CheckOutTime.HasValue)
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
                // ✅ If already clocked out, create a new record or update existing?
                // For simplicity, allow updating the same record
                existing.CheckInTime = DateTime.Now.TimeOfDay;
                existing.CheckOutTime = null; // Clear previous timeout
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

            // ✅ NOTE: Time out does NOT change IsLoggedIn
            // Employee is still logged in but just clocked out

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
                        // Get available quantity from batches (stored in BASE UNIT - grams or mL)
                        decimal availableQtyInBaseUnit = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId
                                && b.QuantityRemaining > 0
                                && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                            .SumAsync(b => b.QuantityRemaining);

                        // ✅ CONVERT availableQty to KG or L (display unit)
                        decimal availableQtyInDisplayUnit = availableQtyInBaseUnit;
                        if (recipe.RawMaterial.BaseUnit == "g" && recipe.RawMaterial.Unit == "kg")
                        {
                            availableQtyInDisplayUnit = availableQtyInBaseUnit / 1000;  // 150,000g → 150 kg
                        }
                        else if (recipe.RawMaterial.BaseUnit == "mL" && recipe.RawMaterial.Unit == "L")
                        {
                            availableQtyInDisplayUnit = availableQtyInBaseUnit / 1000;  // mL → L
                        }

                        // ✅ CONVERT recipe quantity to KG or L
                        decimal quantityPerOrder = recipe.Quantity;
                        if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                        {
                            quantityPerOrder = recipe.Quantity / 1000;  // 1000g → 1 kg
                        }

                        System.Diagnostics.Debug.WriteLine($"=== DEBUG ===");
                        System.Diagnostics.Debug.WriteLine($"Available (base): {availableQtyInBaseUnit} {recipe.RawMaterial.BaseUnit}");
                        System.Diagnostics.Debug.WriteLine($"Available (display): {availableQtyInDisplayUnit} {recipe.RawMaterial.Unit}");
                        System.Diagnostics.Debug.WriteLine($"Recipe Quantity: {recipe.Quantity} {recipe.UnitUsed}");
                        System.Diagnostics.Debug.WriteLine($"Per Order (display): {quantityPerOrder} {recipe.RawMaterial.Unit}");

                        // ✅ NOW both are in the SAME UNIT (kg or L)
                        int possibleOrders = (int)(availableQtyInDisplayUnit / quantityPerOrder);
                        System.Diagnostics.Debug.WriteLine($"Possible Orders: {possibleOrders}");

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
            return HttpContext.Session.GetInt32("Staff_UserId") ?? 0;
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

            if(order.Status != "PENDING_PAYMENT")
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
                .Where(o => (o.Status == "PENDING_BLENDING" || o.Status == "PREPARING") &&  // ✅ Updated
                            o.Items.Any(i => i.DispositionStatusId == 1))
                .OrderBy(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    Items = o.Items.Where(i => i.DispositionStatusId == 1).Select(oi => new
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
                    Items = order.Items.Where(i => i.DispositionStatusId == 1).Select(oi => new  // ✅ AVAILABLE LANG
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

            // ✅ Allow PENDING_BLENDING and PREPARING status
            if (order.Status != "PENDING_BLENDING" && order.Status != "PREPARING")
                return Json(new { success = false, message = $"Cannot start blending. Order status is '{order.Status}'" });

            // Check if already blended (to prevent double deduction)
            if (order.Items.All(i => i.ActualCost > 0))
            {
                return Json(new { success = false, message = "Order already blended!" });
            }

            decimal totalActualCost = 0;

            var activeItems = order.Items.Where(i => i.DispositionStatusId == 1).ToList();

            if (activeItems.Count == 0)
                return Json(new { success = false, message = "No active items to blend." });

            foreach (var orderItem in activeItems)
            {
                var productSize = orderItem.ProductSize;
                decimal itemCost = 0;

                foreach (var recipe in productSize.RecipeItems)
                {
                    var material = await _db.RawMaterials.FindAsync(recipe.RawMaterialId);
                    if (material == null) continue;

                    decimal totalInBaseUnit = recipe.Quantity * orderItem.Quantity;
                    decimal totalForBatch = totalInBaseUnit;

                    if (material.BaseUnit == "g" && material.Unit == "kg")
                        totalForBatch = totalInBaseUnit / 1000;
                    else if (material.BaseUnit == "mL" && material.Unit == "L")
                        totalForBatch = totalInBaseUnit / 1000;

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

            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"✅ Order #{order.OrderNumber} ingredients deducted!",
                actualCost = totalActualCost
            });
        }

        // POST: Staff/AddItemToExistingOrder
        [HttpPost]
        public async Task<IActionResult> AddItemToExistingOrder([FromBody] AddItemToExistingOrderRequest request)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                    return Json(new { success = false, message = "User not logged in" });

                // ✅ Allow BOTH PENDING_PAYMENT and PENDING_BLENDING
                var order = await _db.Orders
                    .FirstOrDefaultAsync(o => o.OrderId == request.OrderId &&
                        (o.Status == "PENDING_PAYMENT" || o.Status == "PENDING_BLENDING"));

                if (order == null)
                    return Json(new { success = false, message = "Order not found" });

                var productSize = await _db.ProductSizes
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product not found" });

                // Add item to existing order
                var orderItem = new OrderItem
                {
                    OrderId = order.OrderId,
                    ProductId = productSize.ProductId,
                    ProductSizeId = request.ProductSizeId,
                    Quantity = request.Quantity,
                    Price = productSize.Price,
                    ActualCost = 0,
                    DispositionStatusId = 1
                };
                _db.OrderItems.Add(orderItem);

                // Update order total
                decimal newSubtotal = order.OriginalTotalAmount + (productSize.Price * request.Quantity);
                decimal newDiscountedTotal = newSubtotal * (1 - request.DiscountPercent);

                order.OriginalTotalAmount = newSubtotal;
                order.TotalAmount = newDiscountedTotal;
                order.DiscountAmount = newSubtotal - newDiscountedTotal;
                order.PaymentMethod = request.PaymentMethod;
                order.DiscountType = request.DiscountType;
                order.DiscountPercent = request.DiscountPercent;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Item added successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Staff/UpdateOrderTotals
        [HttpPost]
        public async Task<IActionResult> UpdateOrderTotals([FromBody] UpdateOrderTotalsRequest request)
        {
            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

                if (order == null)
                    return Json(new { success = false, message = "Order not found" });

                // ✅ Recompute total from active items (DispositionStatusId == 1 = AVAILABLE)
                var activeItems = order.Items.Where(i => i.DispositionStatusId == 1).ToList();
                decimal newSubtotal = activeItems.Sum(i => i.Price * i.Quantity);
                decimal newDiscountedTotal = newSubtotal * (1 - request.DiscountPercent);

                order.OriginalTotalAmount = newSubtotal;
                order.TotalAmount = newDiscountedTotal;
                order.DiscountAmount = newSubtotal - newDiscountedTotal;
                order.PaymentMethod = request.PaymentMethod;
                order.DiscountType = request.DiscountType;
                order.DiscountPercent = request.DiscountPercent;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Order updated" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrderItemQuantity([FromBody] UpdateOrderItemQuantityRequest request)
        {
            try
            {
                var orderItem = await _db.OrderItems
                    .Include(oi => oi.Order)  // ✅ Include Order para ma-check ang status
                    .FirstOrDefaultAsync(oi => oi.OrderItemId == request.OrderItemId);

                if (orderItem == null)
                    return Json(new { success = false, message = "Order item not found" });

                // ✅ Check if order is still editable (not yet blended)
                if (orderItem.Order.Status == "COMPLETED")
                {
                    return Json(new { success = false, message = "Cannot modify - order already blended!" });
                }

                if (request.Quantity <= 0)
                {
                    orderItem.DispositionStatusId = 2;  // VOIDED
                    orderItem.DispositionReason = "Quantity reduced to zero";
                }
                else
                {
                    orderItem.Quantity = request.Quantity;
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Quantity updated" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Staff/GetOnlineStatus
        [HttpGet]
        public async Task<IActionResult> GetOnlineStatus()
        {
            var userId = GetCurrentUserId();
            if (userId == 0)
                return Json(new { isOnline = false, message = "Not logged in" });

            var today = DateTime.Today;
            var attendance = await _db.EmployeeAttendances
                .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

            var isOnline = attendance != null && attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue;

            return Json(new { isOnline = isOnline });
        }

        // Request model
        public class UpdateOrderItemQuantityRequest
        {
            public int OrderItemId { get; set; }
            public int Quantity { get; set; }
        }

        // Request models
        public class AddItemToExistingOrderRequest
        {
            public int OrderId { get; set; }
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
            public string PaymentMethod { get; set; }
            public string DiscountType { get; set; }
            public decimal DiscountPercent { get; set; }
        }

        public class UpdateOrderTotalsRequest
        {
            public int OrderId { get; set; }
            public string PaymentMethod { get; set; }
            public string DiscountType { get; set; }
            public decimal DiscountPercent { get; set; }
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