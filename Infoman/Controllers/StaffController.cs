using Infoman.Constants;
using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infoman.Controllers
{

    public class StaffController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;
        private readonly PaymentOptions _paymentOptions;


        public StaffController(ConnectionDatabase db, IOptions<PaymentOptions> paymentOptions)
        {
            _db = db;
            _fifoService = new FIFOService(_db);
            _paymentOptions = paymentOptions.Value;
        }

        // PHASE 1: Payment gate. Cash lang ang tinatanggap kapag naka-OFF ang GCash/QRPh.
        private bool IsPaymentMethodAvailable(string? paymentMethod)
        {
            return !string.IsNullOrWhiteSpace(paymentMethod) && _paymentOptions.IsEnabled(paymentMethod);
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
                // ✅ TAMA NA: Parehong base unit (grams/mL) ang availableQty at recipeItem.Quantity
                decimal quantityPerOrder = recipeItem.Quantity;

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

                // PHASE 1: Payment gate
                if (!IsPaymentMethodAvailable(request.PaymentMethod))
                    return Json(new { success = false, message = "Payment method not available. Please use Cash." });

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

                // ✅ I-VALIDATE ANG DISCOUNT PERCENT
                if (request.DiscountPercent < 0 || request.DiscountPercent > 1)
                {
                    return Json(new { success = false, message = "Invalid discount percent. Must be between 0 and 1." });
                }

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
                        ReceiptNumber = await GenerateUniqueReceiptNumberAsync(),
                        OriginalTotalAmount = 0,
                        CreatedAt = DateTime.Now,
                        StaffUserId = userId
                    };
                    _db.Orders.Add(order);
                    const int maxRetries = 3;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        try
                        {
                            await _db.SaveChangesAsync();
                            break; // ✅ Tagumpay
                        }
                        catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < maxRetries)
                        {
                            // Race condition — may nag-save ng parehong ReceiptNumber
                            _db.Entry(order).State = EntityState.Detached;
                            order.ReceiptNumber = await GenerateUniqueReceiptNumberAsync();
                            _db.Orders.Add(order);
                        }
                    }
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

                // For Cash: gumawa o i-update ang payment record (Amount = kabuuan ng order)
                if (request.PaymentMethod == "Cash")
                {
                    var existingPayment = await _db.Payments
                        .FirstOrDefaultAsync(p => p.OrderId == orderId && p.PaymentMethod == "Cash");

                    if (existingPayment == null)
                    {
                        _db.Payments.Add(new Payment
                        {
                            OrderId = orderId,
                            Amount = newDiscountedTotal,
                            PaymentMethod = "Cash",
                            PaymentReference = $"PAY-{DateTime.Now:yyyyMMddHHmmss}-{orderId}",
                            Status = "PAID",
                            PaymentDate = DateTime.Now,
                            CreatedAt = DateTime.Now
                        });
                    }
                    else
                    {
                        // PHASE 3: Kapag may dagdag na item, i-update ang amount ng payment.
                        existingPayment.Amount = newDiscountedTotal;
                    }

                    await _db.SaveChangesAsync();
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

        private async Task<string> GenerateUniqueReceiptNumberAsync()
        {
            string datePart = DateTime.Now.ToString("yyyyMMdd");
            string prefix = $"RC-{datePart}-";

            // Kunin lahat ng existing receipts ngayong araw
            var existing = await _db.Orders
                .Where(o => o.ReceiptNumber != null && o.ReceiptNumber.StartsWith(prefix))
                .Select(o => o.ReceiptNumber)
                .ToListAsync();

            int max = 0;
            foreach (var rn in existing)
            {
                var numPart = rn.Substring(prefix.Length);
                if (int.TryParse(numPart, out var n) && n > max) max = n;
            }

            return $"{prefix}{(max + 1):D4}";
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TimeIn()
        {
            var userId = GetCurrentUserId();
            if (userId == 0) return Json(new { success = false, message = "User not logged in" });

            var today = DateTime.Today;

            // ✅ RETRY LOOP para sa race condition (double-click, mobile latency)
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var existing = await _db.EmployeeAttendances
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.Date == today);

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
                    existing.CheckInTime = DateTime.Now.TimeOfDay;
                    existing.CheckOutTime = null;
                    existing.Status = AttendanceStatus.Present;
                }

                try
                {
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, message = $"✅ Time in recorded at {DateTime.Now:hh:mm tt}" });
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < 3)
                {
                    // Race condition — may nag-create ng parehong (UserId, Date)
                    foreach (var entry in _db.ChangeTracker.Entries<EmployeeAttendance>().ToList())
                    {
                        if (entry.State == EntityState.Added) entry.State = EntityState.Detached;
                    }
                    // Loop ulit — makuha na ang existing row ngayon
                }
            }

            return Json(new { success = false, message = "System busy, please try again." });
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
    .Where(a => a.UserId == userId && a.Date == today)
    .OrderByDescending(a => a.CheckInTime)
    .FirstOrDefaultAsync();

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
                        // ✅ TAMA NA: Parehong base unit (grams/mL) ang availableQty at recipe.Quantity
                        decimal availableQtyInDisplayUnit = availableQtyInBaseUnit;
                        decimal quantityPerOrder = recipe.Quantity;

                        // ✅ I-validate para iwas divide by zero
                        if (quantityPerOrder <= 0) continue;

                        

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
        public IActionResult StartPreparing(int orderId)
        {
            return Json(new { success = false, message = "This step is no longer used. Use Start Blending." });
        }

        /// <summary>
        /// Update order status to COMPLETED (ready to serve)
        /// </summary>
        [HttpPost]
        public IActionResult CompleteOrder(int orderId)
        {
            return Json(new { success = false, message = "This step is no longer used. Use Start Blending." });
        }

        /// <summary>
        /// Update order status to CLOSED (paid and finalized)
        /// </summary>
        [HttpPost]
        public IActionResult CloseOrder(int orderId)
        {
            return Json(new { success = false, message = "This step is no longer used." });
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
                                .Where(o => o.Status == "PENDING_BLENDING" &&  // PHASE 2: queue = paid, waiting for blending
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

        // POST: Staff/StartBlending
        // PHASE 2: Atomic na transition. Isang request lang ang makakapasa sa PENDING_BLENDING.
        // Ang status change at ang deduction ay nasa iisang transaction.
        // Kung may mag-fail, lahat ay mare-rollback (walang partial deduction, walang maling status).
        [HttpPost]
        public async Task<IActionResult> StartBlending([FromBody] StartBlendingRequest request)
        {
            if (request == null || request.OrderId <= 0)
                return Json(new { success = false, message = "Invalid order." });

            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // 1. ATOMIC CLAIM: kung ibang request na ang nag-claim, zero rows ang maa-update.
                int claimed = await _db.Orders
                    .Where(o => o.OrderId == request.OrderId && o.Status == "PENDING_BLENDING")
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, "COMPLETED"));

                if (claimed == 0)
                {
                    await tx.RollbackAsync();
                    return Json(new { success = false, message = "Order is not waiting for blending. It may be unpaid, already blended, or not found." });
                }

                var orderNumber = await _db.Orders
                    .Where(o => o.OrderId == request.OrderId)
                    .Select(o => o.OrderNumber)
                    .FirstAsync();

                // 2. Kunin ang available items at ang kanilang recipe (hindi kasama ang deleted recipe)
                var items = await _db.OrderItems
                    .Include(oi => oi.ProductSize)
                        .ThenInclude(ps => ps.RecipeItems)
                            .ThenInclude(ri => ri.RawMaterial)
                    .Where(oi => oi.OrderId == request.OrderId
                              && oi.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                    .ToListAsync();

                if (items.Count == 0)
                {
                    await tx.RollbackAsync();
                    return Json(new { success = false, message = "No active items to blend." });
                }

                decimal totalActualCost = 0;

                // 3. Deduction ayon sa recipe × quantity. Lahat ay nasa loob ng iisang transaction.
                foreach (var item in items)
                {
                    var recipes = item.ProductSize?.RecipeItems?
                        .Where(r => !r.IsDeleted)
                        .ToList() ?? new List<RecipeItem>();

                    if (recipes.Count == 0)
                    {
                        await tx.RollbackAsync();
                        return Json(new { success = false, message = $"No active recipe found for {item.ProductSize?.SizeName ?? "this item"}. Nothing was deducted." });
                    }

                    decimal itemCost = 0;

                    foreach (var recipe in recipes)
                    {
                        // Ang RecipeItem.Quantity ay naka-save na sa base unit (g o mL) ayon sa SaveRecipe at AddProduct.
                        decimal neededBaseUnit = recipe.Quantity * item.Quantity;

                        var (ok, cost, _) = await _fifoService.DeductStockWithCostAsync(
                            recipe.RawMaterialId,
                            neededBaseUnit,
                            item.ProductId,
                            orderNumber,
                            orderId: request.OrderId,
                            productSizeId: item.ProductSizeId);

                        if (!ok)
                        {
                            await tx.RollbackAsync();
                            return Json(new { success = false, message = $"Insufficient stock for {recipe.RawMaterial?.Name ?? "an ingredient"}. Nothing was deducted." });
                        }

                        itemCost += cost;
                    }

                    item.ActualCost = Math.Round(itemCost, 2);
                    totalActualCost += item.ActualCost;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"Order #{orderNumber} ingredients deducted. Order is now COMPLETED.",
                    actualCost = totalActualCost
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
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

                // ✅ I-VALIDATE ANG DISCOUNT PERCENT
                if (request.DiscountPercent < 0 || request.DiscountPercent > 1)
                {
                    return Json(new { success = false, message = "Invalid discount percent. Must be between 0 and 1." });
                }

                // PHASE 1: Payment gate
                if (!IsPaymentMethodAvailable(request.PaymentMethod))
                    return Json(new { success = false, message = "Payment method not available. Please use Cash." });

                // ✅ Allow BOTH PENDING_PAYMENT and PENDING_BLENDING
                var order = await _db.Orders
                    .FirstOrDefaultAsync(o => o.OrderId == request.OrderId &&
                                                o.Status == "PENDING_PAYMENT");  // PHASE 2: paid orders (PENDING_BLENDING) hindi na pwedeng dagdagan

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
        // POST: Staff/UpdateOrderTotals
        [HttpPost]
        public async Task<IActionResult> UpdateOrderTotals([FromBody] UpdateOrderTotalsRequest request)
        {
            try
            {
                // ✅ I-VALIDATE ANG DISCOUNT PERCENT
                if (request.DiscountPercent < 0 || request.DiscountPercent > 1)
                {
                    return Json(new { success = false, message = "Invalid discount percent. Must be between 0 and 1." });
                }

                // PHASE 1: Payment gate
                if (!IsPaymentMethodAvailable(request.PaymentMethod))
                    return Json(new { success = false, message = "Payment method not available. Please use Cash." });

                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.OrderId == request.OrderId);

                if (order == null)
                    return Json(new { success = false, message = "Order not found" });

                // PHASE 2: Paid order (PENDING_BLENDING o COMPLETED) hindi na pwedeng baguhin ang discount o payment method.
                var orderStatusCheck = await _db.Orders
                    .Where(o => o.OrderId == request.OrderId)
                    .Select(o => o.Status)
                    .FirstOrDefaultAsync();

                if (orderStatusCheck == null)
                    return Json(new { success = false, message = "Order not found" });

                if (orderStatusCheck != "PENDING_PAYMENT")
                    return Json(new { success = false, message = "This order is already paid and cannot be edited." });

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
                // PHASE 2: Hindi na pwedeng baguhin ang quantity ng paid order (D5).
                // Ang void ng paid item ay dadaan sa Owner approval (Phase 3).
                if (orderItem.Order.Status != "PENDING_PAYMENT")
                {
                    return Json(new { success = false, message = "Cannot modify a paid order. Item voids require Owner approval." });
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

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            return ex.InnerException is Microsoft.Data.SqlClient.SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627);
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