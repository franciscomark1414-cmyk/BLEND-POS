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

        // StaffController.cs - Update the POS method
        public async Task<IActionResult> POS(bool showReceipt = false, string orderId = null)
        {

            System.Diagnostics.Debug.WriteLine("=== POS PAGE LOADED ===");
            System.Diagnostics.Debug.WriteLine($"showReceipt: {showReceipt}");
            System.Diagnostics.Debug.WriteLine($"orderId: {orderId ?? "NULL"}");

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

            // ✅ I-pass sa View ang receipt flags
            ViewBag.ShowReceipt = showReceipt;
            ViewBag.PendingOrderId = orderId;

            System.Diagnostics.Debug.WriteLine($"=== POS PAGE LOAD ===");
            System.Diagnostics.Debug.WriteLine($"showReceipt: {showReceipt}");
            System.Diagnostics.Debug.WriteLine($"orderId: {orderId}");

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
                // ✅ I-VALIDATE ANG RECIPE QUANTITY
                if (recipeItem.Quantity <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ WARNING: Recipe quantity is 0 for {recipeItem.RawMaterial.Name}. Skipping...");
                    continue; // I-skip kung zero para hindi mag-crash
                }

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
        [HttpPost]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
        {
            System.Diagnostics.Debug.WriteLine("=== PLACE ORDER METHOD HIT ===");
            try
            {
                var userId = GetCurrentUserId();
                if (userId == 0)
                    return Json(new { success = false, message = "User not logged in", requiresTimeIn = true });

                var today = DateTime.Today;
                var attendance = await _db.EmployeeAttendances
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

                if (attendance == null || !attendance.CheckInTime.HasValue)
                    return Json(new { success = false, message = "⚠️ You must TIME IN first!", requiresTimeIn = true });

                if (attendance.CheckOutTime.HasValue)
                    return Json(new { success = false, message = "⚠️ You have already clocked out!", requiresTimeIn = true });

                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product not found" });

                // ✅ BAGONG VALIDATION PARA SA FOREIGN KEY ERROR
                // Hanapin ang "Available" status dynamically imbes na hardcoded na 1
                var availableStatus = await _db.ItemDispositionStatuses
                    .FirstOrDefaultAsync(s => s.StatusName == "Available");

                if (availableStatus == null)
                {
                    return Json(new { success = false, message = "System Error: 'Available' status is missing in the database. Please run the SQL script to seed ItemDispositionStatus." });
                }

                int targetDispositionStatusId = availableStatus.DispositionId;

                if (request.Quantity <= 0)
                    return Json(new { success = false, message = "Invalid quantity" });

                // ✅ CHECK KUNG MAY EXISTING ORDER NA MAY SAME ORDER NUMBER
                Order order = null;
                int orderId;

                if (!string.IsNullOrEmpty(request.OrderNumber))
                {
                    order = await _db.Orders
                        .FirstOrDefaultAsync(o => o.OrderNumber == request.OrderNumber);
                }

                bool isNewOrder = (order == null);

                if (isNewOrder)
                {
                    // Gumawa ng bagong order
                    string finalStatus = request.PaymentMethod == "Cash" ? "PENDING_BLENDING" : "PENDING_PAYMENT";

                    order = new Order
                    {
                        OrderNumber = request.OrderNumber ?? $"POS-{DateTime.Now:yyyyMMddHHmmss}",
                        Date = DateTime.Now,
                        TotalAmount = 0,
                        Status = finalStatus,
                        PaymentMethod = request.PaymentMethod ?? "Cash",
                        DiscountType = string.IsNullOrEmpty(request.DiscountType) ? "None" : request.DiscountType,
                        DiscountPercent = request.DiscountPercent,
                        DiscountAmount = 0,
                        ReceiptNumber = $"RC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                        OriginalTotalAmount = 0,
                        CreatedAt = DateTime.Now,
                        StaffUserId = userId
                    };
                    _db.Orders.Add(order);
                    await _db.SaveChangesAsync();
                }

                orderId = order.OrderId;

                // ✅ I-add ang item sa existing order (GINAMIT NA ANG DYNAMIC ID)
                var orderItem = new OrderItem
                {
                    OrderId = orderId,
                    ProductId = productSize.ProductId,
                    ProductSizeId = productSize.ProductSizeId,
                    Quantity = request.Quantity,
                    Price = productSize.Price,
                    ActualCost = 0,
                    DispositionStatusId = targetDispositionStatusId // ✅ DITO IPINASOK ANG VARIABLE
                };
                _db.OrderItems.Add(orderItem);
                await _db.SaveChangesAsync();  // ✅ I-save muna ang item

                // ✅ I-recompute ang order total (i-reload ang items)
                var allItems = await _db.OrderItems
                    .Where(oi => oi.OrderId == orderId && oi.DispositionStatusId == targetDispositionStatusId) // ✅ GINAMIT DIN DITO
                    .ToListAsync();

                decimal newSubtotal = allItems.Sum(i => i.Price * i.Quantity);
                decimal discountPercent = order.DiscountPercent;  // ✅ Gamitin ang discount ng order
                decimal newDiscountedTotal = newSubtotal * (1 - discountPercent);
                decimal newDiscountAmount = newSubtotal - newDiscountedTotal;

                order.TotalAmount = newDiscountedTotal;
                order.OriginalTotalAmount = newSubtotal;
                order.DiscountAmount = newDiscountAmount;

                await _db.SaveChangesAsync();

                System.Diagnostics.Debug.WriteLine($"=== ORDER UPDATED ===");
                System.Diagnostics.Debug.WriteLine($"OrderId: {orderId}");
                System.Diagnostics.Debug.WriteLine($"New Subtotal: {newSubtotal}");
                System.Diagnostics.Debug.WriteLine($"New Total: {newDiscountedTotal}");

                // For Cash: gumawa ng payment record
                if (request.PaymentMethod == "Cash")
                {
                    var existingPayment = await _db.Payments
                        .FirstOrDefaultAsync(p => p.OrderId == orderId);

                    if (existingPayment == null)
                    {
                        var payment = new Payment
                        {
                            OrderId = orderId,
                            Amount = newDiscountedTotal,
                            PaymentMethod = "Cash",
                            PaymentReference = $"PAY-{DateTime.Now:yyyyMMddHHmmss}",
                            Status = "PAID",
                            PaymentDate = DateTime.Now,
                            CreatedAt = DateTime.Now
                        };
                        _db.Payments.Add(payment);
                        await _db.SaveChangesAsync();
                    }

                    await RecordEmployeePerformance(userId, newDiscountedTotal);
                }

                return Json(new
                {
                    success = true,
                    message = request.PaymentMethod == "Cash" ?
                        $"✅ Order confirmed! Payment received: ₱{newDiscountedTotal:F2}" :
                        $"✅ Item added to order! Waiting for GCash payment...",
                    orderId = orderId,
                    orderNumber = order.OrderNumber,
                    totalAmount = newDiscountedTotal,
                    status = order.Status
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in PlaceOrder: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
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

                        // ✅ I-VALIDATE ANG RECIPE QUANTITY
                        if (quantityPerOrder <= 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ WARNING: Recipe quantity is 0 for {recipe.RawMaterial.Name}. Skipping...");
                            continue; // I-skip kung zero para hindi mag-crash
                        }

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

            if (order.Status != "PENDING_BLENDING" && order.Status != "PREPARING")
                return Json(new { success = false, message = $"Cannot start blending. Order status is '{order.Status}'" });

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

                    // ✅ ILIPAT ANG NULL CHECK DITO PARA HINDI MAG-CRASH
                    if (material == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ WARNING: Material with ID {recipe.RawMaterialId} not found. Skipping...");
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine($"=== ACTUAL DATABASE VALUES ===");
                    System.Diagnostics.Debug.WriteLine($"Raw Material: {material.Name}");
                    System.Diagnostics.Debug.WriteLine($"Material.Unit: {material.Unit}");
                    System.Diagnostics.Debug.WriteLine($"Material.BaseUnit: {material.BaseUnit}");
                    System.Diagnostics.Debug.WriteLine($"Material.Stock: {material.Stock}");
                    System.Diagnostics.Debug.WriteLine($"Recipe.Quantity: {recipe.Quantity}");
                    System.Diagnostics.Debug.WriteLine($"Recipe.UnitUsed: {recipe.UnitUsed}");
                    System.Diagnostics.Debug.WriteLine($"OrderItem.Quantity: {orderItem.Quantity}");
                    System.Diagnostics.Debug.WriteLine($"==============================");

                    decimal totalNeeded = recipe.Quantity * orderItem.Quantity;
                    decimal totalForBatch = totalNeeded;

                    System.Diagnostics.Debug.WriteLine($"=== UNIT CONVERSION DEBUG ===");
                    System.Diagnostics.Debug.WriteLine($"Ingredient: {material.Name}");
                    System.Diagnostics.Debug.WriteLine($"Recipe Unit: {recipe.UnitUsed}, Recipe Qty: {recipe.Quantity}");
                    System.Diagnostics.Debug.WriteLine($"Material Unit: {material.Unit}, BaseUnit: {material.BaseUnit}");
                    System.Diagnostics.Debug.WriteLine($"Total needed (original): {totalNeeded} {recipe.UnitUsed}");

                    // ✅ CONVERSION: I-convert ang recipe unit sa unit ng batch (BaseUnit)
                    // Ang batch ay naka-store sa BaseUnit (grams or mL)
                    // Ang FIFO service ay nag-e-expect ng value sa BaseUnit

                    if (recipe.UnitUsed == "g" && material.BaseUnit == "g")
                    {
                        // Recipe is in grams, batch is in grams - no conversion
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"No conversion: {totalForBatch} g (batch is in g)");
                    }
                    else if (recipe.UnitUsed == "kg" && material.BaseUnit == "g")
                    {
                        // Recipe is in kg, batch is in grams - convert kg to g
                        totalForBatch = totalNeeded * 1000;
                        System.Diagnostics.Debug.WriteLine($"Converted kg → g: {totalNeeded} kg = {totalForBatch} g");
                    }
                    else if (recipe.UnitUsed == "g" && material.BaseUnit == "g" && material.Unit == "kg")
                    {
                        // Recipe is in grams, batch is stored in grams, but displayed as kg
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"Keep in grams: {totalForBatch} g (batch will convert to kg for display)");
                    }
                    else if (recipe.UnitUsed == "mL" && material.BaseUnit == "mL")
                    {
                        // Recipe is in mL, batch is in mL - no conversion
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"No conversion: {totalForBatch} mL");
                    }
                    else if (recipe.UnitUsed == "L" && material.BaseUnit == "mL")
                    {
                        // Recipe is in L, batch is in mL - convert L to mL
                        totalForBatch = totalNeeded * 1000;
                        System.Diagnostics.Debug.WriteLine($"Converted L → mL: {totalNeeded} L = {totalForBatch} mL");
                    }
                    else if (recipe.UnitUsed == "mL" && material.BaseUnit == "mL" && material.Unit == "L")
                    {
                        // Recipe is in mL, batch is stored in mL, but displayed as L
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"Keep in mL: {totalForBatch} mL (batch will convert to L for display)");
                    }
                    else if (recipe.UnitUsed == "pcs" && material.BaseUnit == "pcs")
                    {
                        // For pieces
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"No conversion for pieces: {totalForBatch} pcs");
                    }
                    else
                    {
                        // Default: no conversion
                        totalForBatch = totalNeeded;
                        System.Diagnostics.Debug.WriteLine($"Unknown conversion: keeping as {totalForBatch}");
                    }

                    System.Diagnostics.Debug.WriteLine($"Total for batch deduction: {totalForBatch} {material.BaseUnit}");
                    System.Diagnostics.Debug.WriteLine($"Will deduct: {totalForBatch / 1000} {material.Unit} (display unit)");

                    var (success, cost, usedBatches) = await _fifoService.DeductStockWithCostAsync(
                        recipe.RawMaterialId,
                        totalForBatch,  // ✅ ITO AY NASA BASEUNIT (grams or mL)
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
                            message = $"Failed to deduct {material.Name}. Need {totalForBatch:F4} {material.BaseUnit} ({totalForBatch / 1000:F4} {material.Unit}), check batch stock."
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
                message = $"✅ Order #{order.OrderNumber} ingredients deducted! Order is now COMPLETED.",
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