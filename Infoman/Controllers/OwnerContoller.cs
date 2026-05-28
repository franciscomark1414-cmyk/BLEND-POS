using Infoman.Data;
using Infoman.Filters;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Infoman.Models.WasteLog;

namespace Infoman.Controllers
{
   
    public class OwnerController : Controller
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;
        public OwnerController(ConnectionDatabase db)
        {
            _db = db;
            _fifoService = new FIFOService(_db);
        }

        private async Task RefreshProductPricesInternal()
        {
            var allProductSizes = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .Include(ps => ps.Product)
                .ToListAsync();

            foreach (var productSize in allProductSizes)
            {
                // ✅ KUNG MANUAL PRICE, SKIP (huwag galawin)
                if (productSize.IsManualPrice)
                {
                    continue;  // Skip manual prices completely
                }

                decimal totalCost = 0;
                foreach (var ri in productSize.RecipeItems)
                {
                    decimal activePrice = await GetFIFOUnitPrice(ri.RawMaterialId);
                    decimal quantityForCost = ri.Quantity;
                    if (ri.RawMaterial?.BaseUnit == "g" || ri.RawMaterial?.BaseUnit == "mL")
                    {
                        quantityForCost = ri.Quantity / 1000;
                    }
                    totalCost += quantityForCost * activePrice;
                }

                if (productSize.Margin > 0 && productSize.Margin < 1)
                {
                    decimal computedPrice = totalCost / (1 - productSize.Margin);
                    decimal newPrice = RoundToPriceEnding(computedPrice);

                    // ✅ Only update auto-priced products
                    productSize.Price = newPrice;
                }
            }

            await _db.SaveChangesAsync();
        }

        private bool IsOwnerLoggedIn()
        {
            var userId = HttpContext.Session.GetInt32("Owner_UserId");
            return userId.HasValue && userId.Value > 0;
        }
        private bool IsUserLoggedIn()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            return userId.HasValue && userId.Value > 0;
        }

        public async Task<ActionResult> Dashboard()
        {

            if (!IsOwnerLoggedIn())
            {
                return RedirectToAction("Login", "Account");
            }

            // Optional: Verify with database
            var userId = GetCurrentUserId();
            var user = await _db.Users.FindAsync(userId);
            if (user == null || user.Role != "Owner")
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            await RefreshProductPricesInternal();
            DateTime today = DateTime.Today;

            // ========== TODAY'S NET REVENUE (Using Order NetRevenue property) ==========
            // Today's Sales
            var todaysOrders = await _db.Orders
                .Include(o => o.Adjustments)
                .Where(o => o.Date.Date == today && (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING"))
                .ToListAsync();

            decimal todaysNetRevenue = 0;
            foreach (var order in todaysOrders)
            {
                todaysNetRevenue += order.NetRevenue;
            }

            ViewBag.TodaysSales = todaysNetRevenue;
            ViewBag.TotalOrdersToday = todaysOrders.Count;

            // ALL-TIME NET REVENUE
            var allOrders = await _db.Orders
                .Include(o => o.Adjustments)
                .Where(o => o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING")
                .ToListAsync();

            decimal allTimeNetRevenue = 0;
            foreach (var order in allOrders)
            {
                allTimeNetRevenue += order.NetRevenue;
            }

            ViewBag.AllTimeRevenue = allTimeNetRevenue;

            // Para sa 30% profit estimate
            ViewBag.EstimatedProfit = allTimeNetRevenue * 0.3m;

            // ========== CRITICAL STOCK ALERT ==========
            int criticalStockCount = await _db.RawMaterials
                .Where(r => r.IsDeleted == false && r.Stock <= r.MinQty)
                .CountAsync();
            ViewBag.CriticalStockCount = criticalStockCount;

            // ========== EXPIRING SOON (from BATCHES) ==========
            var todayBatch = DateTime.Today;
            var expiringCount = await _db.RawMaterialBatches
                .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value >= todayBatch &&
                            b.ExpiryDate.Value <= todayBatch.AddDays(7) &&
                            b.QuantityRemaining > 0 &&
                            !b.RawMaterial.IsDeleted)
                .Select(b => b.RawMaterialId)
                .Distinct()
                .CountAsync();
            ViewBag.ExpiringCount = expiringCount;

            // ========== EXPIRED (from BATCHES) ==========
            var expiredCount = await _db.RawMaterialBatches
                .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value < todayBatch &&
                            b.QuantityRemaining > 0 &&
                            !b.RawMaterial.IsDeleted)
                .Select(b => b.RawMaterialId)
                .Distinct()
                .CountAsync();
            ViewBag.ExpiredCount = expiredCount;

            // ========== WASTE LOGS ============
            int wasteCount = await _db.WasteLogs.CountAsync();
            ViewBag.WasteCount = wasteCount;

            // Total other alerts (for badge)
            ViewBag.TotalOtherAlerts = expiringCount + expiredCount + wasteCount;
            ViewBag.LowStockCount = criticalStockCount;

            // ========== SALES TREND (Last 7 Days) - Using NetRevenue ==========
            var last7Days = Enumerable.Range(0, 7)
                .Select(i => today.AddDays(-6 + i))
                .ToList();

            var salesLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();
            var salesValues = new List<decimal>();

            foreach (var day in last7Days)
            {
                var dayOrders = await _db.Orders
                    .Include(o => o.Adjustments)
                    .Where(o => o.Date.Date == day.Date && o.Status == "COMPLETED")
                    .ToListAsync();

                decimal dayNetRevenue = 0;
                foreach (var order in dayOrders)
                {
                    dayNetRevenue += order.NetRevenue;
                }
                salesValues.Add(dayNetRevenue);
            }

            ViewBag.SalesLabels = salesLabels;
            ViewBag.SalesValues = salesValues;

            // ========== BEST SELLING PRODUCTS ==========
            var bestSelling = new List<BestSellingProductVM>();

            try
            {
                var orderItems = await _db.OrderItems
                    .Include(oi => oi.Order)
                    .Include(oi => oi.Product)
                    .Include(oi => oi.ProductSize)
                    .Where(oi => oi.Order.Status == "COMPLETED" && !oi.IsRefunded && !oi.IsVoided)
                    .ToListAsync();

                // ✅ GROUP BY PRODUCT AT KUNIN ANG ImagePath
                bestSelling = orderItems
                    .GroupBy(oi => new {
                        oi.Product?.Name,
                        oi.Product?.ImagePath
                    })
                    .Select(g => new BestSellingProductVM
                    {
                        Name = g.Key.Name,
                        ImagePath = g.Key.ImagePath ?? "/images/default-product.png",  // ✅ Default image kung wala
                        TotalSold = g.Sum(oi => oi.Quantity)
                    })
                    .Where(p => p.TotalSold > 0)
                    .OrderByDescending(p => p.TotalSold)
                    .Take(5)
                    .Select((p, index) => new BestSellingProductVM
                    {
                        Rank = index + 1,
                        Name = p.Name,
                        ImagePath = p.ImagePath,
                        TotalSold = p.TotalSold
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                bestSelling = new List<BestSellingProductVM>();
            }

            // Raw materials for inventory module - FIXED CONVERSION
            var rawMaterialsData = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    r.Category,
                    r.MinQty,
                    r.Price,
                    r.Unit,
                    r.SKU,
                    r.BaseUnit,
                    r.ExpirationDate,
                    TotalStockInBaseUnit = _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId)
                        .Sum(b => (decimal?)b.QuantityRemaining) ?? 0,
                    MinQtyInBaseUnit = r.MinQtyInBaseUnit
                })
                .ToListAsync();

            var rawMaterialsList = new List<RawMaterial>();
            foreach (var r in rawMaterialsData)
            {
                decimal displayStock = r.TotalStockInBaseUnit;
                decimal displayMinQty = r.MinQtyInBaseUnit;

                // Convert from base unit to display unit
                if ((r.BaseUnit == "g" && r.Unit == "kg") || (r.BaseUnit == "mL" && r.Unit == "L"))
                {
                    displayStock = r.TotalStockInBaseUnit / 1000;
                    displayMinQty = r.MinQtyInBaseUnit / 1000;
                }

                rawMaterialsList.Add(new RawMaterial
                {
                    RawMaterialId = r.RawMaterialId,
                    Name = r.Name,
                    Category = r.Category,
                    MinQty = displayMinQty,
                    Price = r.Price,
                    Unit = r.Unit,
                    SKU = r.SKU,
                    ExpirationDate = r.ExpirationDate,
                    Stock = displayStock
                });
            }

            ViewBag.RawMaterials = rawMaterialsList;

            // ========== RAW MATERIALS MASTER RECORD (para sa Raw Materials Module) ==========
            ViewBag.RawMaterialsMaster = rawMaterialsList.Select(r => new
            {
                r.RawMaterialId,
                r.Name,
                r.Category,
                r.Unit,
                r.SKU,
                Status = r.Stock <= r.MinQty ? "Low Stock" : "In Stock"
            }).ToList();




            var products = await _db.Products
                 .Where(p => !p.IsDeleted)
                .Include(p => p.Sizes)
                .Select(p => new ProductViewModel
                {
                    ProductId = p.ProductId,
                    Name = p.Name,
                    SKU = p.SKU,
                    Category = p.Category,
                    Description = p.Description,
                    ImagePath = p.ImagePath,
                    Sizes = p.Sizes.Select(s => new ProductSizeViewModel
                    {
                        ProductSizeId = s.ProductSizeId,
                        SizeName = s.SizeName,
                        Price = s.Price,
                        IsAvailableInPOS = s.IsAvailableInPOS,
                        SKU = s.SKU
                    }).ToList()
                })
                .ToListAsync();

            var dashboardViewModel = new DashboardViewModel
            {
                BestSellingProducts = bestSelling,
                Products = products
            };

            if (ViewBag.TotalRevenue == null) ViewBag.TotalRevenue = 0;
            if (ViewBag.EstimatedProfit == null) ViewBag.EstimatedProfit = 0;
            if (ViewBag.TotalWasteCost == null) ViewBag.TotalWasteCost = 0;
            if (ViewBag.CriticalStockCount == null) ViewBag.CriticalStockCount = 0;
            if (ViewBag.ExpiringCount == null) ViewBag.ExpiringCount = 0;
            if (ViewBag.ExpiredCount == null) ViewBag.ExpiredCount = 0;
            if (ViewBag.WasteCount == null) ViewBag.WasteCount = 0;
            if (ViewBag.TotalOtherAlerts == null) ViewBag.TotalOtherAlerts = 0;
            if (ViewBag.LowStockCount == null) ViewBag.LowStockCount = 0;
            if (ViewBag.TodaysSales == null) ViewBag.TodaysSales = 0;
            if (ViewBag.TotalOrdersToday == null) ViewBag.TotalOrdersToday = 0;
            if (ViewBag.AllTimeRevenue == null) ViewBag.AllTimeRevenue = 0;
            if (ViewBag.SalesLabels == null) ViewBag.SalesLabels = new List<string>();
            if (ViewBag.SalesValues == null) ViewBag.SalesValues = new List<decimal>();

            return View(dashboardViewModel);
        }

         

        public IActionResult LowStockRawMaterials()
        {
            var lowStockMaterials = _db.RawMaterials
                .Where(c => c.Stock <= c.MinQty)
                .ToList();

            return View(lowStockMaterials);
        }
        // Sa OwnerController.cs, idagdag ito:



        // O kung gusto mo ng JSON version para sa AJAX:
        [HttpGet]
        public async Task<IActionResult> GetProducts()
        {

            var products = await _db.Products
                 .Where(p => !p.IsDeleted)
                .Include(p => p.Sizes)
                .Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.SKU,
                    p.Category,
                    p.Description,
                    p.ImagePath,
                    // TotalStock = p.Sizes.Sum(s => s.Stock),  // ← TANGGALIN! Wala nang Stock column
                    Sizes = p.Sizes.Select(s => new
                    {
                        s.SizeName,
                        s.Price,
                        // s.Stock  // ← TANGGALIN DIN!
                    })
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpGet]
        public IActionResult Add()
        {
            // Kunin ang raw materials kasama ang latest batch price
            var rawMaterials = _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new RawMaterial
                {
                    RawMaterialId = r.RawMaterialId,
                    Name = r.Name,
                    Unit = r.Unit,
                    // Kunin ang latest batch price
                    Price = _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId && b.QuantityRemaining > 0)
                        .OrderByDescending(b => b.ReceivedDate)
                        .Select(b => b.UnitPrice)
                        .FirstOrDefault()
                })
                .ToList();

            var model = new ProductViewModel
            {
                RawMaterials = rawMaterials,  // ✅ I-SET ANG RawMaterials
                Sizes = new List<ProductSizeViewModel>
        {
            new ProductSizeViewModel { SizeName = "Small", RecipeItems = new List<RecipeItemViewModel>() },
            new ProductSizeViewModel { SizeName = "Medium", RecipeItems = new List<RecipeItemViewModel>() },
            new ProductSizeViewModel { SizeName = "Large", RecipeItems = new List<RecipeItemViewModel>() }
        }
            };

            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProduct(ProductViewModel model)
        {
            try
            {
                // Gawing optional ang Category, Description, at ImagePath
                ModelState.Remove("Category");
                ModelState.Remove("Description");
                ModelState.Remove("ImagePath");
                ModelState.Remove("Sizes[*].RecipeItems[*].RawMaterialName");

                if (model.Sizes == null || !model.Sizes.Any())
                {
                    return Json(new { success = false, message = "Please add at least one size" });
                }

                foreach (var size in model.Sizes)
                {
                    if (size.RecipeItems == null || !size.RecipeItems.Any() || size.RecipeItems.All(r => r.RawMaterialId == 0))
                    {
                        return Json(new { success = false, message = $"Please add at least one ingredient for {size.SizeName}" });
                    }
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .Where(x => x.Value.Errors.Any())
                        .ToDictionary(
                            x => x.Key,
                            x => x.Value.Errors.Select(e => e.ErrorMessage).ToList()
                        );
                    return Json(new { success = false, errors = errors, message = "Please fix validation errors" });
                }

                string userPrefix = model.SKU?.Trim().ToUpper() ?? "PRD";
                int existingCount = await _db.Products.CountAsync(p => p.SKU.StartsWith(userPrefix));
                string productNumber = (existingCount + 1).ToString("D3");
                string generatedProductSKU = $"{userPrefix}{productNumber}";

                var product = new Product
                {
                    Name = model.Name.Trim(),
                    SKU = generatedProductSKU,
                    Category = model.Category?.Trim() ?? "",
                    Description = model.Description?.Trim() ?? "",
                    ImagePath = model.ImagePath ?? "",
                    Sizes = new List<ProductSize>()
                };

                foreach (var sizeVM in model.Sizes)
                {
                    if (string.IsNullOrWhiteSpace(sizeVM.SizeName))
                        continue;

                    decimal margin = model.SelectedProfitMargin switch
                    {
                        "30%" => 0.3m,
                        "40%" => 0.4m,
                        "50%" => 0.5m,
                        _ => 0.3m
                    };

                    string sizeSKU = GenerateSizeSKU(generatedProductSKU, sizeVM.SizeName);

                    var productSize = new ProductSize
                    {
                        SizeName = sizeVM.SizeName.Trim(),
                        SKU = sizeSKU ?? "",
                        Margin = margin,
                        RecipeItems = new List<RecipeItem>()
                    };

                    var totalCost = 0m;
                    var validRecipeCount = 0;

                    foreach (var recipeVM in sizeVM.RecipeItems)
                    {
                        if (recipeVM.RawMaterialId > 0 && recipeVM.Quantity > 0)
                        {
                            var rawMat = await _db.RawMaterials.FindAsync(recipeVM.RawMaterialId);
                            if (rawMat == null) continue;

                            var latestPrice = await _db.RawMaterialBatches
                                .Where(b => b.RawMaterialId == recipeVM.RawMaterialId)
                                .OrderByDescending(b => b.ReceivedDate)
                                .Select(b => b.UnitPrice)
                                .FirstOrDefaultAsync();

                            if (latestPrice >= 0)
                            {
                                decimal quantityInBaseUnit = recipeVM.Quantity;

                                if (recipeVM.UnitUsed == "kg" && rawMat.BaseUnit == "g")
                                {
                                    quantityInBaseUnit = recipeVM.Quantity * 1000;
                                }
                                else if (recipeVM.UnitUsed == "L" && rawMat.BaseUnit == "mL")
                                {
                                    quantityInBaseUnit = recipeVM.Quantity * 1000;
                                }

                                decimal quantityForCost = recipeVM.Quantity;
                                if (recipeVM.UnitUsed == "kg")
                                {
                                    quantityForCost = recipeVM.Quantity;
                                }
                                else if (recipeVM.UnitUsed == "g")
                                {
                                    quantityForCost = recipeVM.Quantity / 1000;
                                }

                                var cost = quantityForCost * latestPrice;
                                totalCost += cost;
                                validRecipeCount++;

                                productSize.RecipeItems.Add(new RecipeItem
                                {
                                    RawMaterialId = recipeVM.RawMaterialId,
                                    Quantity = quantityInBaseUnit,
                                    UnitUsed = recipeVM.UnitUsed
                                });
                            }
                        }
                    }

                    if (validRecipeCount == 0)
                    {
                        return Json(new { success = false, message = $"Please add at least one ingredient for {sizeVM.SizeName}" });
                    }

                    // ✅ COMPUTE PRICE WITH FIXED ENDING 9
                    decimal computedPrice = totalCost / (1 - margin);
                    decimal finalPrice = RoundToPriceEnding(computedPrice);

                    // ✅ CHECK KUNG MANUAL PRICE O AUTO
                    if (sizeVM.Price > 0)
                    {
                        productSize.Price = sizeVM.Price;      // Manual price
                        productSize.IsManualPrice = true;      // ✅ SET MANUAL FLAG
                    }
                    else
                    {
                        productSize.Price = finalPrice;        // Auto price
                        productSize.IsManualPrice = false;     // ✅ SET AUTO FLAG
                    }

                    product.Sizes.Add(productSize);
                }

                if (!product.Sizes.Any())
                {
                    return Json(new { success = false, message = "Please add at least one valid size" });
                }

                _db.Products.Add(product);
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Product added successfully! SKU: {generatedProductSKU}",
                    stayOnPage = true
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving product: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddProductSize([FromBody] AddSizeRequest request)
        {
            try
            {
                var product = await _db.Products.FindAsync(request.ProductId);
                if (product == null)
                    return Json(new { success = false, message = "Product not found" });

                // Generate SKU for the size
                string sizeSKU = GenerateSizeSKU(product.SKU, request.SizeName);

                var productSize = new ProductSize
                {
                    ProductId = request.ProductId,
                    SizeName = request.SizeName,
                    SKU = sizeSKU,
                    Price = request.Price,
                    Margin = request.Margin,
                    IsAvailableInPOS = true,
                    RecipeItems = new List<RecipeItem>(),
                    // ✅ SET MANUAL PRICE FLAG
                    IsManualPrice = request.Price > 0  // Kung may manual price, true; kung auto (0), false
                };

                // Add recipe items
                foreach (var ingredient in request.Ingredients)
                {
                    var rawMat = await _db.RawMaterials.FindAsync(ingredient.RawMaterialId);
                    if (rawMat != null)
                    {
                        decimal quantityInBaseUnit = ingredient.Quantity;

                        if (ingredient.UnitUsed == "kg" && rawMat.BaseUnit == "g")
                            quantityInBaseUnit = ingredient.Quantity * 1000;
                        else if (ingredient.UnitUsed == "L" && rawMat.BaseUnit == "mL")
                            quantityInBaseUnit = ingredient.Quantity * 1000;

                        productSize.RecipeItems.Add(new RecipeItem
                        {
                            RawMaterialId = ingredient.RawMaterialId,
                            Quantity = quantityInBaseUnit,
                            UnitUsed = ingredient.UnitUsed
                        });
                    }
                }

                _db.ProductSizes.Add(productSize);
                await _db.SaveChangesAsync();

                // Recompute price if zero (auto price)
                if (request.Price == 0)
                {
                    await RecomputeProductSizePrice(productSize.ProductSizeId);
                }

                return Json(new { success = true, message = "Size added successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
        // ========== HELPER METHOD FOR SIZE SKU ==========
        private string GenerateSizeSKU(string productSKU, string sizeName)
        {
            string sizeCode = sizeName.Trim().ToUpper() switch
            {
                "SMALL" => "SML",
                "MEDIUM" => "MED",
                "LARGE" => "LRG",
                _ => sizeName.Trim().ToUpper().Length >= 3 ?
                     sizeName.Trim().ToUpper().Substring(0, 3) :
                     sizeName.Trim().ToUpper()
            };

            return $"{productSKU}-{sizeCode}";
        }
        private string GenerateProductSKU(string userPrefix)
        {
            string prefix = userPrefix.Trim().ToUpper();
            int count = _db.Products.Count(p => p.SKU.StartsWith(prefix));
            string number = (count + 1).ToString("D3");
            return $"{prefix}{number}";
        }

        [HttpGet]
        public async Task<IActionResult> GetRefundReports(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.Today.AddDays(-30);
            var end = endDate ?? DateTime.Today;

            var refunds = await _db.OrderAdjustments
                .Where(a => (a.AdjustmentType == "VOID_REFUND" || a.AdjustmentType == "REFUND") &&
                            a.Status == "COMPLETED" &&
                            a.CreatedAt.Date >= start.Date && a.CreatedAt.Date <= end.Date)
                .Include(a => a.OriginalOrder)
                .Select(a => new
                {
                    a.AdjustmentId,
                    OrderId = a.OriginalOrderId,
                    OrderNumber = a.OriginalOrder.OrderNumber,
                    a.AdjustmentType,
                    a.Amount,
                    a.Reason,
                    a.ReferenceNumber,
                    a.CreatedAt,
                    ProcessedBy = a.ApprovedByUser != null ? a.ApprovedByUser.Username : "System"
                })
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var summary = new
            {
                TotalRefundAmount = refunds.Sum(r => r.Amount),
                TotalRefundCount = refunds.Count,
                VoidRefunds = refunds.Count(r => r.AdjustmentType == "VOID_REFUND"),
                ExchangeRefunds = refunds.Count(r => r.AdjustmentType == "REFUND")
            };

            return Json(new { success = true, refunds = refunds, summary = summary });
        }

        [HttpGet]
        public async Task<IActionResult> GetReceipts(string date = null)
        {
            var query = _db.Orders
                .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                .ThenInclude(oi => oi.ProductSize)
                .Where(o => o.Status != "PENDING_PAYMENT" && o.Status != "VOIDED");

            if (!string.IsNullOrEmpty(date))
            {
                var filterDate = DateTime.Parse(date);
                query = query.Where(o => o.Date.Date == filterDate.Date);
            }

            var receipts = await query
                .OrderByDescending(o => o.OrderId)
                .Select(o => new
                {
                    orderId = o.OrderId,                                    // ✅ ADD THIS
                    receiptNumber = o.ReceiptNumber,
                    date = o.Date.ToString("yyyy-MM-dd HH:mm:ss"),
                    itemCount = o.Items.Count(i => !i.IsVoided),
                    subtotal = o.OriginalTotalAmount,
                    discountType = o.DiscountType,
                    discountPercent = o.DiscountPercent,
                    discountAmount = o.DiscountAmount,
                    total = o.TotalAmount,
                    paymentMethod = o.PaymentMethod,
                    returnStatus = o.ReturnStatus ?? "ACTIVE",              // ✅ ADD THIS
                    adjustmentAmount = o.AdjustmentAmount,                  // ✅ ADD THIS
                    originalOrderId = o.OriginalOrderId                     // ✅ ADD THIS
                })
                .ToListAsync();

            return Json(new { success = true, receipts = receipts });
        }
        [HttpGet]
        public async Task<IActionResult> GetReceiptDetail(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return Json(new { success = false, message = "Order not found" });

            var items = new List<object>();
            foreach (var item in order.Items.Where(i => !i.IsVoided))
            {
                items.Add(new
                {
                    name = item.Product?.Name ?? "Unknown",
                    size = item.ProductSize?.SizeName ?? "Regular",
                    quantity = item.Quantity,
                    price = item.Price,
                    subtotal = item.Price * item.Quantity
                });
            }

            var receipt = new
            {
                orderId = order.OrderId,                                    // ✅ ADD THIS
                receiptNumber = order.ReceiptNumber,
                date = order.Date,
                items = items,
                subtotal = order.OriginalTotalAmount,
                discountType = order.DiscountType,
                discountPercent = order.DiscountPercent,
                discountAmount = order.DiscountAmount,
                total = order.TotalAmount,
                paymentMethod = order.PaymentMethod,
                returnStatus = order.ReturnStatus ?? "ACTIVE",              // ✅ ADD THIS
                adjustmentAmount = order.AdjustmentAmount,                  // ✅ ADD THIS
                storeCreditApplied = order.StoreCreditApplied,              // ✅ ADD THIS
                originalOrderId = order.OriginalOrderId                     // ✅ ADD THIS
            };

            return Json(new { success = true, receipt = receipt });
        }
        // Helper method para kumuha ng FIFO unit price
        // Palitan ang GetFIFOUnitPrice ng ganito:
        private async Task<decimal> GetFIFOUnitPrice(int rawMaterialId)
        {
            return await _fifoService.GetActiveBatchPrice(rawMaterialId);
        }

        [HttpGet]
        public async Task<IActionResult> GetExchangeDetails(int orderId)
        {
            try
            {
                // Hanapin ang exchange transaction kung saan ito ay original order o replacement order
                var exchange = await _db.ExchangeTransactions
                    .FirstOrDefaultAsync(e => e.OriginalOrderId == orderId || e.NewOrderId == orderId);

                if (exchange == null)
                    return Json(new { success = false, message = "No exchange record found" });

                // Kunin ang returned items (mula sa original order)
                var returnedItems = new List<object>();

                var originalOrderItems = await _db.OrderItems
                    .Include(oi => oi.Product)
                    .Include(oi => oi.ProductSize)
                    .Where(oi => oi.OrderId == exchange.OriginalOrderId)
                    .ToListAsync();

                foreach (var item in originalOrderItems)
                {
                    returnedItems.Add(new
                    {
                        quantity = item.Quantity,
                        productName = item.Product?.Name ?? "Unknown",
                        sizeName = item.ProductSize?.SizeName ?? "Regular",
                        price = item.Price
                    });
                }

                // Kunin ang replacement items (mula sa bagong order)
                var replacementItems = new List<object>();

                if (exchange.NewOrderId.HasValue && exchange.NewOrderId.Value > 0)
                {
                    var newOrderItems = await _db.OrderItems
                        .Include(oi => oi.Product)
                        .Include(oi => oi.ProductSize)
                        .Where(oi => oi.OrderId == exchange.NewOrderId.Value)
                        .ToListAsync();

                    foreach (var item in newOrderItems)
                    {
                        replacementItems.Add(new
                        {
                            quantity = item.Quantity,
                            productName = item.Product?.Name ?? "Unknown",
                            sizeName = item.ProductSize?.SizeName ?? "Regular",
                            price = item.Price
                        });
                    }
                }

                // Gamitin ang mga values mula sa exchange transaction
                decimal originalAmount = exchange.OriginalAmount > 0 ? exchange.OriginalAmount : exchange.TotalReturnedValue;
                decimal replacementAmount = exchange.ReplacementAmount > 0 ? exchange.ReplacementAmount : exchange.TotalReplacementValue;
                decimal adjustmentAmount = exchange.AdjustmentAmount != 0 ? exchange.AdjustmentAmount : exchange.PriceDifference;

                // Determine kung ito ay original order o replacement order
                bool isOriginalOrder = (exchange.OriginalOrderId == orderId);

                var result = new
                {
                    success = true,
                    exchange = new
                    {
                        exchange.OriginalOrderId,
                        exchange.NewOrderId,
                        exchange.Reason,
                        OriginalAmount = originalAmount,
                        ReplacementAmount = replacementAmount,
                        AdjustmentAmount = adjustmentAmount,
                        AdjustmentType = exchange.AdjustmentType ?? (adjustmentAmount >= 0 ? "ADDITIONAL_PAYMENT" : "CHANGE"),
                        PaymentMethod = exchange.PaymentMethod,
                        IsOriginalOrder = isOriginalOrder,
                        ReturnedItems = returnedItems,
                        ReplacementItems = replacementItems,
                        exchange.WasteCost
                    }
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetRecipe(int id)
        {
            try
            {
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == id);

                if (productSize == null)
                    return Json(new { error = "Recipe not found" });

                if (productSize.RecipeItems == null || !productSize.RecipeItems.Any())
                {
                    return Json(new { error = "No ingredients found for this product. Please add recipe first." });
                }

                decimal totalCost = 0;
                var ingredientsWithPrices = new List<object>();

                foreach (var recipeItem in productSize.RecipeItems)
                {
                    if (recipeItem.RawMaterial == null)
                    {
                        return Json(new { error = $"Raw material not found for ingredient" });
                    }

                    decimal fifoPrice = await GetFIFOUnitPrice(recipeItem.RawMaterialId);

                    // 1. DISPLAY
                    decimal displayQuantity = recipeItem.Quantity;
                    string displayUnit = recipeItem.UnitUsed;

                    if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                    {
                        if (recipeItem.UnitUsed == "kg" || recipeItem.UnitUsed == "L")
                        {
                            displayQuantity = recipeItem.Quantity / 1000;  // 1000g → 1 kg
                        }
                        // g or mL — as-is na
                    }

                    // 2. COST COMPUTATION - laging divide by 1000 kung BaseUnit ay g o mL
                    decimal quantityForCost = recipeItem.Quantity;
                    if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                    {
                        quantityForCost = recipeItem.Quantity / 1000;
                    }

                    decimal total = quantityForCost * fifoPrice;
                    totalCost += total;

                    ingredientsWithPrices.Add(new
                    {
                        rawMaterialId = recipeItem.RawMaterialId,
                        quantity = displayQuantity,
                        name = recipeItem.RawMaterial.Name,
                        unit = displayUnit,
                        unitUsed = recipeItem.UnitUsed,
                        price = fifoPrice,
                        total = total
                    });
                }

                decimal profit = productSize.Price - totalCost;

                var today = DateTime.Today;
                int availableStock = int.MaxValue;

                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal availableQty = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                            && b.QuantityRemaining > 0
                            && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                        .SumAsync(b => b.QuantityRemaining);

                    int possibleOrders = (int)(availableQty / recipeItem.Quantity);
                    if (possibleOrders < availableStock)
                        availableStock = possibleOrders;
                }

                int finalStock = (availableStock == int.MaxValue || availableStock < 0) ? 0 : availableStock;

                var recipe = new
                {
                    productName = productSize.Product?.Name ?? "Unknown",
                    productSKU = productSize.Product?.SKU ?? "-",
                    productCategory = productSize.Product?.Category ?? "-",
                    sizeName = productSize.SizeName,
                    sellingPrice = productSize.Price,
                    totalCost = totalCost,
                    margin = productSize.Margin,
                    stock = finalStock,
                    hasStock = finalStock > 0,
                    profit = profit,
                    ingredients = ingredientsWithPrices
                };

                return Json(recipe);
            }
            catch (Exception ex)
            {
                return Json(new { error = $"Error loading recipe: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditRecipe(int id)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .Include(ps => ps.Product)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == id);

            if (productSize == null)
                return Json(new { success = false, message = "Product not found" });

            decimal totalCost = 0;
            var ingredientsWithPrices = new List<object>();

            foreach (var recipeItem in productSize.RecipeItems)
            {
                decimal fifoPrice = await GetFIFOUnitPrice(recipeItem.RawMaterialId);

                if (fifoPrice == 0 && recipeItem.RawMaterial != null)
                {
                    fifoPrice = recipeItem.RawMaterial.Price;
                }

                decimal displayQuantity = recipeItem.Quantity;
                string displayUnit = recipeItem.UnitUsed;

                if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                {
                    if (recipeItem.UnitUsed == "kg" || recipeItem.UnitUsed == "L")
                    {
                        displayQuantity = recipeItem.Quantity / 1000;
                    }
                }

                decimal quantityForCost = recipeItem.Quantity;
                if (recipeItem.RawMaterial.BaseUnit == "g" || recipeItem.RawMaterial.BaseUnit == "mL")
                {
                    quantityForCost = recipeItem.Quantity / 1000;
                }

                decimal total = quantityForCost * fifoPrice;
                totalCost += total;

                ingredientsWithPrices.Add(new
                {
                    rawMaterialId = recipeItem.RawMaterialId,
                    quantity = displayQuantity,
                    name = recipeItem.RawMaterial?.Name ?? "Unknown",
                    unit = displayUnit,
                    unitUsed = recipeItem.UnitUsed,
                    price = fifoPrice,
                    total = total
                });
            }

            var result = new
            {
                success = true,
                productSizeId = productSize.ProductSizeId,
                productName = productSize.Product?.Name ?? "",
                sizeName = productSize.SizeName,
                sellingPrice = productSize.Price,
                margin = productSize.Margin,
                totalCost = totalCost,
                isManualPrice = productSize.IsManualPrice,  // ✅ ADD THIS
                ingredients = ingredientsWithPrices
            };

            return Json(result);
        }

        [HttpGet]
        public IActionResult CanEditRecipe(int id)
        {
            var productSize = _db.ProductSizes
                .FirstOrDefault(ps => ps.ProductSizeId == id);

            if (productSize == null)
                return Json(new { canEdit = false, message = "Product not found" });

            return Json(new { canEdit = true });
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveRecipe([FromBody] RecipeSaveModel model)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                .Include(ps => ps.Product)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == model.ProductSizeId);

            if (productSize == null)
                return Json(new { success = false, message = "Product size not found" });

            // ✅ I-save ang old price at manual flag bago magbago
            bool wasManualPrice = productSize.IsManualPrice;
            decimal oldPrice = productSize.Price;

            productSize.Margin = model.Margin;

            // DELETE OLD
            if (productSize.RecipeItems != null && productSize.RecipeItems.Any())
            {
                _db.RecipeItems.RemoveRange(productSize.RecipeItems);
                await _db.SaveChangesAsync();
            }

            productSize.RecipeItems = new List<RecipeItem>();

            // ADD NEW with UnitUsed (STORE IN BASE UNIT - GRAMS)
            foreach (var ingredient in model.Ingredients)
            {
                var rawMat = await _db.RawMaterials.FindAsync(ingredient.RawMaterialId);
                if (rawMat != null)
                {
                    decimal quantityInBaseUnit = ingredient.Quantity;

                    if (ingredient.UnitUsed == "kg" && rawMat.BaseUnit == "g")
                    {
                        quantityInBaseUnit = ingredient.Quantity * 1000;
                    }
                    else if (ingredient.UnitUsed == "L" && rawMat.BaseUnit == "mL")
                    {
                        quantityInBaseUnit = ingredient.Quantity * 1000;
                    }

                    productSize.RecipeItems.Add(new RecipeItem
                    {
                        RawMaterialId = ingredient.RawMaterialId,
                        Quantity = quantityInBaseUnit,
                        UnitUsed = ingredient.UnitUsed,
                        ProductSizeId = productSize.ProductSizeId
                    });
                }
            }

            await _db.SaveChangesAsync();

            // RECOMPUTE PRICE (para malaman ang bagong auto-calc price)
            decimal totalCost = 0;
            foreach (var ingredient in model.Ingredients)
            {
                var rawMat = await _db.RawMaterials.FindAsync(ingredient.RawMaterialId);
                decimal unitPrice = await GetFIFOUnitPrice(ingredient.RawMaterialId);

                decimal quantityForCost = ingredient.Quantity;

                if (ingredient.UnitUsed == "kg")
                {
                    quantityForCost = ingredient.Quantity;
                }
                else if (ingredient.UnitUsed == "g")
                {
                    quantityForCost = ingredient.Quantity / 1000;
                }
                else if (ingredient.UnitUsed == "L")
                {
                    quantityForCost = ingredient.Quantity;
                }
                else if (ingredient.UnitUsed == "mL")
                {
                    quantityForCost = ingredient.Quantity / 1000;
                }

                totalCost += quantityForCost * unitPrice;
            }

            // ✅ APPLY FIXED ENDING 9 PRICING
            decimal computedPrice = totalCost / (1 - productSize.Margin);
            decimal newAutoPrice = RoundToPriceEnding(computedPrice);

            // ✅ KUNG MANUAL PRICE, I-RETAIN ANG OLD PRICE
            // ✅ KUNG AUTO PRICE, I-UPDATE SA BAGONG COMPUTED PRICE
            if (wasManualPrice)
            {
                productSize.Price = oldPrice;           // Keep the manual price
                productSize.IsManualPrice = true;       // Ensure flag stays true
            }
            else
            {
                productSize.Price = newAutoPrice;       // Update to new auto-calc price
                productSize.IsManualPrice = false;      // Ensure flag stays false
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, newPrice = productSize.Price });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveProduct(int id)
        {
            var product = await _db.Products
                .Include(p => p.Sizes)
                    .ThenInclude(s => s.RecipeItems)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null)
                return Json(new { success = false, message = "Product not found" });

            // ✅ Soft delete ang product
            product.IsDeleted = true;

            // ✅ Soft delete ang lahat ng sizes
            foreach (var size in product.Sizes)
            {
                size.IsAvailableInPOS = false;
                size.IsDeleted = true;  // Kailangan mag-add ng IsDeleted sa ProductSize model

                // ✅ Soft delete ang mga RecipeItems
                foreach (var recipe in size.RecipeItems)
                {
                    recipe.IsDeleted = true;  // Kailangan mag-add ng IsDeleted sa RecipeItem model
                }
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Product archived successfully" });
        }
        [HttpGet]
        public async Task<IActionResult> GetInventoryAlertsData()
        {
            var today = DateTime.Today;

            var allMaterials = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .ToListAsync();

            // ========== LOW STOCK ITEMS ==========
            var lowStockItems = new List<object>();

            foreach (var r in allMaterials)
            {
                // Compute total stock from batches (in base unit)
                decimal totalStockInBaseUnit = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == r.RawMaterialId)
                    .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

                // Convert to display unit for comparison
                decimal totalStockInDisplayUnit = totalStockInBaseUnit;
                decimal minQtyInDisplayUnit = r.MinQtyInBaseUnit;

                if ((r.BaseUnit == "g" && r.Unit == "kg") || (r.BaseUnit == "mL" && r.Unit == "L"))
                {
                    totalStockInDisplayUnit = totalStockInBaseUnit / 1000;
                    minQtyInDisplayUnit = r.MinQtyInBaseUnit / 1000;
                }

                bool isNoStock = totalStockInDisplayUnit < 0.001m;
                bool isLowStock = totalStockInDisplayUnit <= minQtyInDisplayUnit && totalStockInDisplayUnit >= 0.001m;

                if (isNoStock || isLowStock)
                {
                    lowStockItems.Add(new
                    {
                        r.RawMaterialId,
                        r.Name,
                        Category = r.Category.ToString(),
                        Stock = totalStockInDisplayUnit,
                        MinQty = minQtyInDisplayUnit,
                        r.Unit,
                        StockWithUnit = $"{totalStockInDisplayUnit:F2} {r.Unit}",
                        MinQtyWithUnit = $"{minQtyInDisplayUnit:F2} {r.Unit}",
                        Status = isNoStock ? "No Stock" : "Low Stock"
                    });
                }
            }  // ← DITO TAPOS ANG FOREACH LOOP!

            // ✅ ITO NASA LABAS NA NG LOOP
            int lowStockCount = lowStockItems.Count;

            // ========== EXPIRING SOON ==========
            var expiringDateThreshold = today.AddDays(7);

            var expiringItems = await _db.RawMaterialBatches
                .Include(b => b.RawMaterial)
                .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value >= today &&
                            b.ExpiryDate.Value <= expiringDateThreshold &&
                            b.QuantityRemaining > 0 &&
                            !b.RawMaterial.IsDeleted)
                .Select(b => new
                {
                    RawMaterialId = b.RawMaterial.RawMaterialId,
                    Name = b.RawMaterial.Name,
                    Category = b.RawMaterial.Category.ToString(),
                    ExpiringStockQuantity = (b.RawMaterial.BaseUnit == "g" && b.RawMaterial.Unit == "kg") ||
                                           (b.RawMaterial.BaseUnit == "mL" && b.RawMaterial.Unit == "L")
                        ? b.QuantityRemaining / 1000
                        : b.QuantityRemaining,
                    ExpiringStockWithUnit = ((b.RawMaterial.BaseUnit == "g" && b.RawMaterial.Unit == "kg") ||
                                            (b.RawMaterial.BaseUnit == "mL" && b.RawMaterial.Unit == "L"))
                        ? $"{(b.QuantityRemaining / 1000):F2} {b.RawMaterial.Unit}"
                        : $"{b.QuantityRemaining:F2} {b.RawMaterial.Unit}",
                    ExpiryDate = b.ExpiryDate.Value.ToString("yyyy-MM-dd"),
                    DaysLeft = (b.ExpiryDate.Value - today).Days,
                    Unit = b.RawMaterial.Unit,
                    BatchId = b.BatchId
                })
                .ToListAsync();
            int expiringCount = expiringItems.Count;

            // ========== EXPIRED ==========
            var expiredBatches = await _db.RawMaterialBatches
                .Include(b => b.RawMaterial)
                .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value < today &&
                            b.QuantityRemaining > 0 &&
                            !b.RawMaterial.IsDeleted)
                .ToListAsync();

            var expiredItems = expiredBatches
                .GroupBy(b => b.RawMaterial)
                .Select(g => new
                {
                    RawMaterialId = g.Key.RawMaterialId,
                    Name = g.Key.Name,
                    Category = g.Key.Category.ToString(),
                    ExpiredStockQuantity = g.Sum(b => b.QuantityRemaining),
                    ExpiredStockWithUnit = ((g.Key.BaseUnit == "g" && g.Key.Unit == "kg") ||
                                           (g.Key.BaseUnit == "mL" && g.Key.Unit == "L"))
                        ? $"{g.Sum(b => b.QuantityRemaining) / 1000:F2} {g.Key.Unit}"
                        : $"{g.Sum(b => b.QuantityRemaining):F2} {g.Key.Unit}",
                    ExpiryDate = g.Min(b => b.ExpiryDate).Value.ToString("yyyy-MM-dd"),
                    DaysOverdue = (today - g.Min(b => b.ExpiryDate).Value).Days,
                    Unit = g.Key.Unit,
                    BatchCount = g.Count()
                })
                .ToList();
            int expiredCount = expiredItems.Count;

            // ========== WASTE LOGS ==========
            var wasteLogs = await _db.WasteLogs
                .Include(w => w.RawMaterial)
                .OrderByDescending(w => w.Date)
                .Select(w => new
                {
                    w.WasteLogId,
                    w.Date,
                    MaterialName = w.RawMaterial.Name,
                    Quantity = (w.RawMaterial.BaseUnit == "g" && w.RawMaterial.Unit == "kg") ||
                              (w.RawMaterial.BaseUnit == "mL" && w.RawMaterial.Unit == "L")
                        ? w.Quantity / 1000
                        : w.Quantity,
                    Unit = w.RawMaterial.Unit,
                    Reason = w.Reason.ToString(),
                    TotalCost = w.TotalCost,
                    w.RecordedBy,
                    w.Notes,
                    ExpiryDate = w.BatchId != null ? _db.RawMaterialBatches
                        .Where(b => b.BatchId == w.BatchId)
                        .Select(b => b.ExpiryDate.ToString())
                        .FirstOrDefault() : null
                })
                .ToListAsync();
            int wasteCount = wasteLogs.Count;

            // ✅ RETURN STATEMENT - NASA LABAS NG LAHAT NG LOOP
            return Json(new
            {
                lowStockCount = lowStockCount,
                expiringCount = expiringCount,
                expiredCount = expiredCount,
                wasteCount = wasteCount,
                lowStockItems = lowStockItems,
                expiringItems = expiringItems,
                expiredItems = expiredItems,
                wasteLogs = wasteLogs
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddWasteLog([FromBody] WasteLogViewModel model)
        {
            try
            {
                var material = await _db.RawMaterials.FindAsync(model.RawMaterialId);
                if (material == null)
                    return Json(new { success = false, message = "Material not found" });

                // Convert to base unit (grams/mL) for checking
                decimal quantityInBaseUnit = model.Quantity;
                if (material.Unit == "kg" && material.BaseUnit == "g")
                    quantityInBaseUnit = model.Quantity * 1000;
                else if (material.Unit == "L" && material.BaseUnit == "mL")
                    quantityInBaseUnit = model.Quantity * 1000;

                if (material.Stock < quantityInBaseUnit)
                    return Json(new { success = false, message = $"Insufficient stock. Only {material.Stock / 1000} {material.Unit} available." });

                // ✅ GET ALL BATCHES (FEFO: earliest expiry first)
                var batches = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == model.RawMaterialId && b.QuantityRemaining > 0)
                    .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                    .ThenBy(b => b.ReceivedDate)
                    .ToListAsync();

                decimal totalCost = 0;
                decimal remainingToDeduct = quantityInBaseUnit;
                DateTime? earliestExpiry = null;
                int? primaryBatchId = null;
                bool hasBatches = batches.Any();

                // ✅ KUNG MAY BATCHES, DEDUCT FROM THEM
                if (hasBatches)
                {
                    foreach (var batch in batches)
                    {
                        if (remainingToDeduct <= 0) break;

                        decimal deductQty = Math.Min(remainingToDeduct, batch.QuantityRemaining);

                        // ✅ COMPUTE COST - use the batch's unit price
                        // IMPORTANT: deductQty is in base unit (grams/mL)
                        // Convert to display unit for costing
                        decimal deductQtyInDisplayUnit = deductQty;
                        if (material.Unit == "kg" && material.BaseUnit == "g")
                            deductQtyInDisplayUnit = deductQty / 1000;  // grams → kg
                        else if (material.Unit == "L" && material.BaseUnit == "mL")
                            deductQtyInDisplayUnit = deductQty / 1000;  // mL → L

                        decimal batchCost = deductQtyInDisplayUnit * batch.UnitPrice;
                        totalCost += batchCost;
                        remainingToDeduct -= deductQty;

                        // ✅ GET EARLIEST EXPIRY DATE
                        if (batch.ExpiryDate.HasValue)
                        {
                            if (!earliestExpiry.HasValue || batch.ExpiryDate.Value < earliestExpiry.Value)
                            {
                                earliestExpiry = batch.ExpiryDate;
                                primaryBatchId = batch.BatchId;
                            }
                        }

                        batch.QuantityRemaining -= deductQty;
                    }
                }

                // ✅ KUNG WALANG BATCHES O KULANG, DEDUCT DIRECTLY FROM MAIN STOCK
                if (remainingToDeduct > 0)
                {
                    // Compute cost based on current price
                    decimal currentPrice = material.Price;
                    decimal remainingInDisplayUnit = remainingToDeduct;
                    if (material.Unit == "kg" && material.BaseUnit == "g")
                        remainingInDisplayUnit = remainingToDeduct / 1000;
                    else if (material.Unit == "L" && material.BaseUnit == "mL")
                        remainingInDisplayUnit = remainingToDeduct / 1000;

                    totalCost += remainingInDisplayUnit * currentPrice;
                }

                // ✅ UPDATE MAIN STOCK
                material.Stock -= quantityInBaseUnit;
                await _db.SaveChangesAsync();

                // ✅ CREATE WASTE LOG - STORE IN BASE UNIT (grams/mL)
                var wasteLog = new WasteLog
                {
                    RawMaterialId = model.RawMaterialId,
                    Quantity = quantityInBaseUnit,  // Store in base unit (grams/mL)
                    Reason = Enum.Parse<WasteLog.WasteReason>(model.Reason),
                    Date = DateTime.Now,
                    RecordedBy = model.RecordedBy ?? "Owner",
                    Notes = model.Notes,
                    TotalCost = totalCost,           // ✅ MAY VALUE NA
                    BatchId = primaryBatchId,        // ✅ MAY BATCH ID KUNG MERON
                    ExpiryDate = earliestExpiry      // ✅ MAY EXPIRY DATE KUNG MERON
                };

                _db.WasteLogs.Add(wasteLog);
                await _db.SaveChangesAsync();

                // Convert back to display unit for response
                decimal displayQuantity = model.Quantity;
                string displayUnit = material.Unit;

                return Json(new
                {
                    success = true,
                    message = $"Waste logged successfully: {displayQuantity:F2} {displayUnit}",
                    totalCost = totalCost
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper class
        private class BatchDeductionInfo
        {
            public int BatchId { get; set; }
            public decimal QuantityDeducted { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public decimal UnitPrice { get; set; }
        }

        // ViewModel for WasteLog
        public class WasteLogViewModel
        {
            public int RawMaterialId { get; set; }
            public decimal Quantity { get; set; }
            public string Reason { get; set; }
            public string Notes { get; set; }
            public string RecordedBy { get; set; }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProduceProduct([FromBody] ProduceRequest request)
        {
            try
            {
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.Product)
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product size not found" });

                if (request.Quantity <= 0)
                    return Json(new { success = false, message = "Invalid quantity" });

                // Helper: convert quantity from one unit to the material's base unit
                decimal ConvertToBaseUnit(decimal qty, string fromUnit, string toBaseUnit)
                {
                    if (fromUnit == toBaseUnit) return qty;
                    return (fromUnit, toBaseUnit) switch
                    {
                        ("kg", "g") => qty * 1000,
                        ("g", "kg") => qty / 1000,
                        ("L", "mL") => qty * 1000,
                        ("mL", "L") => qty / 1000,
                        _ => qty
                    };
                }

                // ===== STEP 1: CHECK AVAILABILITY =====
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    var material = await _db.RawMaterials.FindAsync(recipeItem.RawMaterialId);
                    if (material == null) continue;

                    // Total needed for this production run, converted to base unit
                    decimal totalNeeded = recipeItem.Quantity * request.Quantity;
                    decimal neededInBaseUnit = ConvertToBaseUnit(totalNeeded, recipeItem.UnitUsed, material.BaseUnit);

                    decimal availableStock = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipeItem.RawMaterialId
                            && b.QuantityRemaining > 0
                            && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                        .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

                    System.Diagnostics.Debug.WriteLine($"[CHECK] {material.Name}: need {neededInBaseUnit} {material.BaseUnit}, available {availableStock} {material.BaseUnit}");

                    if (availableStock < neededInBaseUnit)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Insufficient {material.Name}. Need {neededInBaseUnit:F2} {material.BaseUnit}, only {availableStock:F2} {material.BaseUnit} available."
                        });
                    }
                }

                // ===== STEP 2: DEDUCT RAW MATERIALS =====
                string referenceNo = request.ReferenceNo ?? $"PROD-{DateTime.Now:yyyyMMddHHmmss}";

                foreach (var recipeItem in productSize.RecipeItems)
                {
                    var material = await _db.RawMaterials.FindAsync(recipeItem.RawMaterialId);
                    if (material == null) continue;

                    // Pass recipe unit and quantity directly — FIFOService.DeductStock handles conversion
                    decimal totalNeeded = recipeItem.Quantity * request.Quantity;

                    bool success = _fifoService.DeductStock(
    recipeItem.RawMaterialId,
    totalNeeded,
    recipeItem.UnitUsed,
    productSize.ProductId,
    $"{referenceNo}-{productSize.ProductSizeId}",
    orderId: null,             
    productSizeId: productSize.ProductSizeId  
);

                    if (!success)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Failed to deduct {material.Name}. Insufficient batch stock."
                        });
                    }
                }

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Successfully produced {request.Quantity} pcs of {productSize.Product.Name} - {productSize.SizeName}",
                    referenceNo = referenceNo
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Request model
        public class ProduceRequest
        {
            public int ProductSizeId { get; set; }
            public int Quantity { get; set; }
            public string? ReferenceNo { get; set; }
        }

        // Idagdag sa OwnerController.cs

        [HttpPost]
        public async Task<IActionResult> TogglePOSVisibility([FromBody] ToggleVisibilityRequest request)
        {
            try
            {
                var productSize = await _db.ProductSizes
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product size not found" });

                productSize.IsAvailableInPOS = request.IsAvailable;
                await _db.SaveChangesAsync();

                string status = request.IsAvailable ? "displayed in POS menu" : "hidden from POS";
                return Json(new { success = true, message = $"Product is now {status}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class ToggleVisibilityRequest
        {
            public int ProductSizeId { get; set; }
            public bool IsAvailable { get; set; }
        }
        // Sa OwnerController.cs - Dashboard o kung saan naglo-load ng products

        // Kunin ang average o latest price ng raw materials para i-compute ang suggested selling price
        private async Task<decimal> GetSuggestedSellingPrice(int productSizeId)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == productSizeId);

            if (productSize == null) return 0;

            decimal totalCost = 0;

            foreach (var recipeItem in productSize.RecipeItems)
            {
                // Kunin ang latest batch price para sa suggestion
                var latestPrice = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipeItem.RawMaterialId)
                    .OrderByDescending(b => b.ReceivedDate)
                    .Select(b => b.UnitPrice)
                    .FirstOrDefaultAsync();

                totalCost += recipeItem.Quantity * latestPrice;
            }

            // Apply margin
            decimal suggestedPrice = totalCost / (1 - productSize.Margin);
            return Math.Round(suggestedPrice, 2);
        }
        [HttpGet]
        public async Task<IActionResult> GetReportsData(DateTime? filterDate = null)
        {
            var today = DateTime.Today;
            var queryDate = filterDate ?? today;

            // ========== SALES PERFORMANCE ==========
            var last7Days = Enumerable.Range(0, 7).Select(i => today.AddDays(-6 + i)).ToList();
            var salesLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();
            var salesRevenue = new List<decimal>();
            var salesOrders = new List<int>();

            foreach (var day in last7Days)
            {
                // ✅ Include PENDING_BLENDING and COMPLETED
                var dailyRevenue = await _db.Orders
                    .Where(o => o.Date.Date == day && (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING"))
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

                var dailyRefunds = await _db.RefundTransactions
                    .Where(r => r.CreatedAt.Date == day)
                    .SumAsync(r => (decimal?)r.Amount) ?? 0;

                salesRevenue.Add(dailyRevenue - dailyRefunds);

                // ✅ Include PENDING_BLENDING and COMPLETED
                var dailyOrders = await _db.Orders
                    .CountAsync(o => o.Date.Date == day && (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING"));
                salesOrders.Add(dailyOrders);
            }

            // ========== CATEGORY DISTRIBUTION ==========
            var categoryDistribution = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.Status == "COMPLETED" || oi.Order.Status == "PENDING_BLENDING")
                .GroupBy(oi => oi.Product.Category ?? "Uncategorized")
                .Select(g => new
                {
                    Category = g.Key,
                    Revenue = g.Sum(oi => oi.Price * oi.Quantity)
                })
                .ToListAsync();

            // ========== TOP SELLING PRODUCTS ==========
            var topSelling = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Include(oi => oi.ProductSize)
                .Where(oi => oi.Order.Status == "COMPLETED" || oi.Order.Status == "PENDING_BLENDING")
                .GroupBy(oi => new {
                    oi.Product.Name,
                    SizeName = oi.ProductSize != null ? oi.ProductSize.SizeName : "Regular"
                })
                .Select(g => new
                {
                    Name = g.Key.Name,
                    SizeName = g.Key.SizeName,
                    UnitsSold = g.Sum(oi => oi.Quantity),
                    Revenue = g.Sum(oi => oi.Price * oi.Quantity)
                })
                .OrderByDescending(p => p.UnitsSold)
                .Take(5)
                .ToListAsync();

            // ========== DISCOUNT RECORDS ==========
            var discountRecords = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Where(o => (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING") && o.DiscountPercent > 0)
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    Date = o.Date,
                    OrderNumber = o.OrderNumber,
                    ProductName = o.Items.FirstOrDefault().Product.Name,
                    OriginalTotal = o.TotalAmount + o.DiscountAmount,
                    DiscountPercent = o.DiscountPercent * 100,
                    DiscountType = o.DiscountType ?? "None",
                    DiscountedTotal = o.TotalAmount,
                    Savings = o.DiscountAmount
                })
                .ToListAsync();

            // ========== WASTE LOGS ==========
            var wasteLogs = await _db.WasteLogs
                .Include(w => w.RawMaterial)
                .OrderByDescending(w => w.Date)
                .Select(w => new
                {
                    w.WasteLogId,
                    w.Date,
                    MaterialName = w.RawMaterial.Name,
                    Quantity = (w.RawMaterial.Unit == "kg" && w.RawMaterial.BaseUnit == "g")
                        ? w.Quantity / 1000
                        : ((w.RawMaterial.Unit == "L" && w.RawMaterial.BaseUnit == "mL")
                            ? w.Quantity / 1000
                            : w.Quantity),
                    Unit = w.RawMaterial.Unit,
                    Reason = w.Reason.ToString(),
                    TotalCost = w.TotalCost,
                    w.RecordedBy,
                    w.Notes,
                    ExpiryDate = w.ExpiryDate != null ? w.ExpiryDate.Value.ToString("yyyy-MM-dd") : "N/A"
                })
                .ToListAsync();

            var totalWasteCost = wasteLogs.Sum(w => w.TotalCost);

            // ========== TOTAL REVENUE ==========
            var totalRevenue = await _db.Orders
                .Where(o => o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            var allRefunds = await _db.RefundTransactions.SumAsync(r => (decimal?)r.Amount) ?? 0;
            var netRevenue = totalRevenue - allRefunds;

            // ========== REVENUE BY CATEGORY ==========
            var categoryRevenue = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.Status == "COMPLETED" || oi.Order.Status == "PENDING_BLENDING")
                .GroupBy(oi => oi.Product.Category ?? "Uncategorized")
                .Select(g => new
                {
                    Category = g.Key,
                    Revenue = g.Sum(oi => oi.Price * oi.Quantity),
                    Orders = g.Select(oi => oi.OrderId).Distinct().Count()
                })
                .ToListAsync();

            // ========== RECEIPT HISTORY ==========
            var receipts = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .Where(o => o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING")
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    orderId = o.OrderId,
                    receiptNumber = o.ReceiptNumber ?? $"ORD-{o.OrderId}",
                    date = o.Date.ToString("yyyy-MM-dd HH:mm:ss"),
                    itemCount = o.Items.Sum(oi => oi.Quantity),
                    items = o.Items.Select(oi => new
                    {
                        name = oi.Product.Name,
                        size = oi.ProductSize.SizeName,
                        quantity = oi.Quantity,
                        price = oi.Price,
                        subtotal = oi.Price * oi.Quantity
                    }).ToList(),
                    subtotal = o.Items.Sum(oi => oi.Price * oi.Quantity),
                    discountType = o.DiscountType ?? "None",
                    discountPercent = o.DiscountPercent,
                    discountAmount = o.DiscountAmount,
                    total = o.TotalAmount,
                    paymentMethod = o.PaymentMethod ?? "Cash"
                })
                .ToListAsync();

            ViewBag.TotalRevenue = netRevenue;
            ViewBag.EstimatedProfit = netRevenue * 0.3m;
            ViewBag.TotalWasteCost = totalWasteCost;

            return Json(new
            {
                salesPerformance = new { labels = salesLabels, revenue = salesRevenue, orders = salesOrders },
                categoryDistribution = categoryDistribution,
                topSellingProducts = topSelling,
                wasteLogs = wasteLogs,
                discountRecords = discountRecords,
                categoryRevenue = categoryRevenue,
                receipts = receipts
            });
        }
        public async Task<ActionResult> Reports()
        {
            // ========== TOTAL REVENUE ==========
            var totalRevenue = await _db.Orders
                .Where(o => o.Status != "Cancelled")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
            ViewBag.TotalRevenue = totalRevenue;

            // ========== ESTIMATED PROFIT (30% margin) ==========
            ViewBag.EstimatedProfit = totalRevenue * 0.3m;

            // ========== TOTAL WASTE COST ==========
            var wasteLogs = await _db.WasteLogs.ToListAsync();
            var totalWasteCost = wasteLogs.Sum(w => w.TotalCost);
            ViewBag.TotalWasteCost = totalWasteCost;

            // ========== SALES PERFORMANCE CHART DATA ==========
            var today = DateTime.Today;
            var last7Days = Enumerable.Range(0, 7)
                .Select(i => today.AddDays(-6 + i))
                .ToList();

            var salesLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();
            var salesRevenue = new List<decimal>();
            var salesOrders = new List<int>();

            foreach (var day in last7Days)
            {
                var dailyRevenue = await _db.Orders
                    .Where(o => o.Date.Date == day && o.Status != "Cancelled")
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
                salesRevenue.Add(dailyRevenue);

                var dailyOrders = await _db.Orders
                    .CountAsync(o => o.Date.Date == day && o.Status != "Cancelled");
                salesOrders.Add(dailyOrders);
            }

            ViewBag.SalesLabels = salesLabels;
            ViewBag.SalesRevenue = salesRevenue;
            ViewBag.SalesOrders = salesOrders;

            // ========== CATEGORY DISTRIBUTION ==========
            var categoryDistribution = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.Status != "Cancelled")
                .GroupBy(oi => oi.Product.Category ?? "Uncategorized")
                .Select(g => new
                {
                    Category = g.Key,
                    Revenue = g.Sum(oi => oi.Price * oi.Quantity)
                })
                .ToListAsync();

            ViewBag.CategoryDistribution = categoryDistribution;

            return View();
        }

        // ========== SUPPLIER ACTIONS ==========

        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            var suppliers = await _db.Suppliers.ToListAsync();
            return Json(suppliers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier supplier)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(supplier.Name))
                    return Json(new { success = false, message = "Supplier name is required" });

                supplier.CreatedAt = DateTime.Now;
                _db.Suppliers.Add(supplier);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Supplier added successfully", supplier });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSupplier([FromBody] Supplier supplier)
        {
            try
            {
                var existing = await _db.Suppliers.FindAsync(supplier.SupplierId);
                if (existing == null)
                    return Json(new { success = false, message = "Supplier not found" });

                existing.Name = supplier.Name;
                existing.ContactPerson = supplier.ContactPerson;
                existing.Phone = supplier.Phone;
                existing.Email = supplier.Email;
                existing.Address = supplier.Address;
                existing.Status = supplier.Status;
                existing.UpdatedAt = DateTime.Now;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Supplier updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            try
            {
                var supplier = await _db.Suppliers.FindAsync(id);
                if (supplier == null)
                    return Json(new { success = false, message = "Supplier not found" });

                // Check if may deliveries
                var hasDeliveries = await _db.Receivings.AnyAsync(r => r.SupplierId == id);
                if (hasDeliveries)
                {
                    // Soft delete - set to inactive
                    supplier.Status = "Inactive";
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, message = "Supplier set to inactive (has existing deliveries)" });
                }

                _db.Suppliers.Remove(supplier);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Supplier deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========== RECEIVING ACTIONS ==========

        [HttpGet]
        public async Task<IActionResult> GetReceivingFormData()
        {
            var suppliers = await _db.Suppliers.Where(s => s.Status == "Active").Select(s => new { s.SupplierId, s.Name }).ToListAsync();

            var rawMaterials = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    r.Unit,
                    // ✅ CONVERT STOCK
                    StockInDisplayUnit = r.BaseUnit == "g" && r.Unit == "kg" ? r.Stock / 1000 :
                                        (r.BaseUnit == "mL" && r.Unit == "L" ? r.Stock / 1000 : r.Stock)
                })
                .ToListAsync();

            return Json(new { suppliers, rawMaterials });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReceiveDelivery([FromBody] ReceivingViewModel model)
        {
            try
            {
                // Validate each item
                foreach (var item in model.Items)
                {
                    if (item.AcceptedQty + item.RejectedQty != item.ReceivedQty)
                    {
                        return Json(new { success = false, message = $"For {item.RawMaterialName}: Accepted + Rejected must equal Received" });
                    }
                    if (item.AcceptedQty < 0 || item.RejectedQty < 0)
                    {
                        return Json(new { success = false, message = $"Quantities cannot be negative" });
                    }

                    if (item.RejectedQty > 0 && string.IsNullOrEmpty(item.Disposition))
                    {
                        return Json(new { success = false, message = $"For {item.RawMaterialName}: Please select disposition (Return or Waste) for rejected items" });
                    }
                }

                // Create receiving header
                var receiving = new Receiving
                {
                    SupplierId = model.SupplierId,
                    DRNumber = model.DRNumber,
                    InvoiceNumber = model.InvoiceNumber,
                    DateReceived = model.DateReceived,
                    Remarks = model.Remarks,
                    CreatedBy = "Owner",
                    CreatedAt = DateTime.Now
                };

                _db.Receivings.Add(receiving);
                await _db.SaveChangesAsync();

                // Process each item
                foreach (var item in model.Items)
                {
                    var receivingItem = new ReceivingItem
                    {
                        ReceivingId = receiving.ReceivingId,
                        RawMaterialId = item.RawMaterialId,
                        OrderedQty = item.OrderedQty,
                        ReceivedQty = item.ReceivedQty,
                        AcceptedQty = item.AcceptedQty,
                        RejectedQty = item.RejectedQty,
                        RejectionReason = item.RejectedQty > 0 ? item.RejectionReason : null,
                        UnitPrice = item.UnitPrice,
                        ExpiryDate = item.ExpiryDate,
                        ReturnStatus = null,
                        ReturnId = null
                    };
                    _db.ReceivingItems.Add(receivingItem);
                    await _db.SaveChangesAsync();

                    // ✅ CREATE STOCK BATCH for accepted quantity WITH CONVERSION
                    if (item.AcceptedQty > 0)
                    {
                        var rawMat = await _db.RawMaterials.FindAsync(item.RawMaterialId);

                        decimal quantityInBaseUnit = item.AcceptedQty;

                        if (rawMat.BaseUnit == "g" && rawMat.Unit == "kg")
                        {
                            quantityInBaseUnit = item.AcceptedQty * 1000;  // 20 kg → 20,000 g
                        }
                        else if (rawMat.BaseUnit == "mL" && rawMat.Unit == "L")
                        {
                            quantityInBaseUnit = item.AcceptedQty * 1000;  // 20 L → 20,000 mL
                        }

                        var batch = new RawMaterialBatch
                        {
                            RawMaterialId = item.RawMaterialId,
                            QuantityReceived = quantityInBaseUnit,
                            QuantityRemaining = quantityInBaseUnit,
                            ReceivedDate = DateTime.Now,
                            ExpiryDate = item.ExpiryDate,
                            UnitPrice = item.UnitPrice
                        };
                        _db.RawMaterialBatches.Add(batch);

                        // ✅ I-SAVE MUNA PARA MAGKAROON NG BATCH ID
                        await _db.SaveChangesAsync();

                        // ✅ CHECK KUNG EXPIRED ANG BATCH NA ITO
                        if (item.ExpiryDate.HasValue && item.ExpiryDate.Value.Date < DateTime.Today)
                        {
                            System.Diagnostics.Debug.WriteLine($"=== EXPIRED BATCH DETECTED! ===");
                            System.Diagnostics.Debug.WriteLine($"Material: {rawMat.Name}");
                            System.Diagnostics.Debug.WriteLine($"ExpiryDate: {item.ExpiryDate.Value:yyyy-MM-dd}");
                            System.Diagnostics.Debug.WriteLine($"Today: {DateTime.Today:yyyy-MM-dd}");
                            System.Diagnostics.Debug.WriteLine($"AcceptedQty: {item.AcceptedQty}");
                            System.Diagnostics.Debug.WriteLine($"UnitPrice: {item.UnitPrice}");
                            // Compute total cost (accepted quantity in display unit * unit price)
                            decimal totalCost = item.AcceptedQty * item.UnitPrice;
                            System.Diagnostics.Debug.WriteLine($"TotalCost: {totalCost}");

                            // Convert to base unit for storage
                            decimal wastedQtyInBaseUnit = quantityInBaseUnit;

                            var wasteLog = new WasteLog
                            {
                                RawMaterialId = item.RawMaterialId,
                                Quantity = wastedQtyInBaseUnit,  // ✅ BASE UNIT (grams/mL)
                                Reason = WasteLog.WasteReason.Expired,
                                Date = DateTime.Now,
                                RecordedBy = "Receiving",
                                Notes = $"Auto-generated from expired batch received on DR#{model.DRNumber}. Expiry: {item.ExpiryDate.Value:yyyy-MM-dd}",
                                TotalCost = totalCost,
                                ExpiryDate = item.ExpiryDate,
                                BatchId = batch.BatchId
                            };
                            _db.WasteLogs.Add(wasteLog);

                            // ✅ SET REMAINING TO 0 DAHIL EXPIRED
                            batch.QuantityRemaining = 0;
                            System.Diagnostics.Debug.WriteLine($"WasteLog added. Batch.QuantityRemaining set to 0");

                            await _db.SaveChangesAsync();
                            System.Diagnostics.Debug.WriteLine($"Changes saved successfully!");
                        }
                    }

                    // ✅ HANDLE REJECTED ITEMS (may conversion na)
                    if (item.RejectedQty > 0)
                    {
                        var rawMat = await _db.RawMaterials.FindAsync(item.RawMaterialId);

                        // Convert rejected quantity to base unit
                        decimal rejectedQtyInBaseUnit = item.RejectedQty;
                        if (rawMat.BaseUnit == "g" && rawMat.Unit == "kg")
                        {
                            rejectedQtyInBaseUnit = item.RejectedQty * 1000;
                        }
                        else if (rawMat.BaseUnit == "mL" && rawMat.Unit == "L")
                        {
                            rejectedQtyInBaseUnit = item.RejectedQty * 1000;
                        }

                        if (item.Disposition == "Waste")
                        {
                            decimal totalCost = item.RejectedQty * item.UnitPrice;

                            var wasteLog = new WasteLog
                            {
                                RawMaterialId = item.RawMaterialId,
                                Quantity = rejectedQtyInBaseUnit,  // ✅ BASE UNIT (grams/mL)
                                Reason = WasteLog.WasteReason.QualityIssue,
                                Date = DateTime.Now,
                                RecordedBy = "Receiving",
                                Notes = $"Rejected from DR#{model.DRNumber}: {item.RejectionReason}",
                                TotalCost = totalCost,
                                ExpiryDate = item.ExpiryDate,
                                BatchId = null
                            };
                            _db.WasteLogs.Add(wasteLog);
                        }
                        else if (item.Disposition == "Return")
                        {
                            var rejectedForReturn = new RejectedForReturn
                            {
                                ReceivingId = receiving.ReceivingId,
                                RawMaterialId = item.RawMaterialId,
                                Quantity = item.RejectedQty,  // Display unit okay for return
                                Reason = item.RejectionReason,
                                UnitPrice = item.UnitPrice,
                                Status = "Pending Return"
                            };
                            _db.RejectedForReturns.Add(rejectedForReturn);
                        }
                    }
                }

                await _db.SaveChangesAsync();

                // Update main stock for affected materials
                var affectedMaterials = model.Items.Where(i => i.AcceptedQty > 0).Select(i => i.RawMaterialId).Distinct();
                foreach (var materialId in affectedMaterials)
                {
                    await UpdateStockFromBatches(materialId);
                }

                return Json(new { success = true, message = $"Delivery {model.DRNumber} received successfully!", receivingId = receiving.ReceivingId });
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += " - Inner: " + ex.InnerException.Message;
                    if (ex.InnerException.InnerException != null)
                    {
                        errorMessage += " - Inner2: " + ex.InnerException.InnerException.Message;
                    }
                }
                return Json(new { success = false, message = errorMessage });
            }
        }
        private async Task UpdateStockFromBatches(int rawMaterialId)
        {
            var totalStockInBaseUnit = await _db.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var material = await _db.RawMaterials.FindAsync(rawMaterialId);
            if (material != null)
            {
                material.Stock = totalStockInBaseUnit;  // This stores in base unit (grams)
                await _db.SaveChangesAsync();
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetReceivingHistory()
        {
            var receivings = await _db.Receivings
                .Include(r => r.Supplier)
                .Include(r => r.ReceivingItems)
                .ThenInclude(ri => ri.RawMaterial)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.ReceivingId,
                    r.DRNumber,
                    r.DateReceived,
                    SupplierName = r.Supplier.Name,
                    TotalItems = r.ReceivingItems.Count,
                    TotalAccepted = r.ReceivingItems.Sum(ri => ri.AcceptedQty),
                    TotalRejected = r.ReceivingItems.Sum(ri => ri.RejectedQty),
                    Items = r.ReceivingItems.Select(ri => new
                    {
                        ri.RawMaterial.Name,
                        ri.ReceivedQty,
                        ri.AcceptedQty,
                        ri.RejectedQty,
                        ri.RejectionReason,
                        ri.ExpiryDate
                    })
                })
                .ToListAsync();

            return Json(receivings);
        }
        [HttpGet]
        public async Task<IActionResult> GetReturnDetails(int requestId)
        {
            var request = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Include(r => r.Approver)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null)
                return Json(new { success = false, message = "Request not found" });

            decimal amount = 0;
            if (request.RequestType == "REFUND")
            {
                // ✅ Kunin ang refund amount mula sa Refunds table
                amount = await _db.RefundTransactions
    .Where(r => r.OriginalOrderId == request.OrderId)
    .SumAsync(r => r.Amount);
            }
            else
            {
                amount = request.Order?.TotalAmount ?? 0;
            }

            return Json(new
            {
                success = true,
                id = request.Id,
                orderNumber = request.Order?.OrderNumber,
                requestType = request.RequestType,
                reason = request.Reason,
                amount = amount,  // ✅ Tamang amount na
                status = request.Status,
                requesterName = request.Requester?.Username,
                requestedAt = request.RequestedAt,
                approverName = request.Approver?.Username,
                approvedAt = request.ApprovedAt
            });
        }

        // ========== RETURNS ACTIONS ==========
        [HttpGet]
        public async Task<IActionResult> GetReturns()
        {
            // 1. GET APPROVED VOIDS
            var approvedVoids = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Include(r => r.Approver)
                .Where(r => r.RequestType == "VOID" && r.Status == "APPROVED")
                .Select(r => new
                {
                    Id = r.Id,
                    OrderNumber = r.Order.OrderNumber,
                    RequestType = "VOID",
                    Reason = r.Reason,
                    Amount = r.Order.TotalAmount,
                    Status = "APPROVED",
                    RecordedAt = r.ApprovedAt,
                    RequesterName = r.Requester.Username,
                    Source = "APPROVED"
                })
                .ToListAsync();

            // 2. GET DIRECT VOIDS
            var directVoids = await _db.VoidLogs
                .Include(v => v.Transaction)
                .Include(v => v.User)
                .Where(v => v.VoidType == "item" || v.VoidType == "transaction")
                .Where(v => v.PreparedFlag == false)
                .Select(v => new
                {
                    Id = v.VoidLogId,
                    OrderNumber = v.Transaction.OrderNumber,
                    RequestType = "VOID",
                    Reason = v.Reason,
                    Amount = v.Transaction.TotalAmount,
                    Status = "COMPLETED",
                    RecordedAt = v.Timestamp,
                    RequesterName = v.User.Username,
                    Source = "DIRECT"
                })
                .ToListAsync();

            // 3. GET APPROVED REFUNDS - ✅ FIXED: Kunin ang tamang amount mula sa Refunds table
            var approvedRefunds = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Include(r => r.Approver)
                .Where(r => r.RequestType == "REFUND" && r.Status == "APPROVED")
                .Select(r => new
                {
                    Id = r.Id,
                    OrderNumber = r.Order.OrderNumber,
                    RequestType = "REFUND",
                    Reason = r.Reason,
                    Amount = _db.RefundTransactions.Where(refund => refund.OriginalOrderId == r.OrderId).Sum(refund => refund.Amount),
                    Status = "APPROVED",
                    RecordedAt = r.ApprovedAt,
                    RequesterName = r.Requester.Username,
                    Source = "APPROVED"
                })
                .ToListAsync();

            // 4. COMBINE ALL
            var allReturns = new List<object>();
            allReturns.AddRange(approvedVoids);
            allReturns.AddRange(directVoids);
            allReturns.AddRange(approvedRefunds);

            var sortedReturns = allReturns
                .OrderByDescending(r => ((dynamic)r).RecordedAt)
                .ToList();

            return Json(sortedReturns);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateReturnStatus([FromBody] UpdateReturnStatusRequest request)
        {
            try
            {
                var returnItem = await _db.RejectedForReturns.FindAsync(request.Id);
                if (returnItem == null)
                    return Json(new { success = false, message = "Return record not found" });

                returnItem.Status = request.Status;
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Return marked as {request.Status}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReturn([FromBody] DeleteReturnRequest request)
        {
            try
            {
                var returnItem = await _db.RejectedForReturns.FindAsync(request.Id);
                if (returnItem == null)
                    return Json(new { success = false, message = "Return record not found" });

                _db.RejectedForReturns.Remove(returnItem);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Return record deleted" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Idagdag sa OwnerController.cs

        [HttpGet]
        public async Task<IActionResult> GetPendingRequests()
        {
            var pendingRequests = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Where(r => r.Status == "PENDING")
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => new
                {
                    r.Id,
                    r.OrderId,
                    OrderNumber = r.Order.OrderNumber,
                    OrderTotal = r.Order.TotalAmount,  // ← Amount column
                    r.RequestType,
                    r.Reason,
                    RequesterName = r.Requester.Username,  // ← Requested By column
                    RequestedAt = r.RequestedAt.ToString("yyyy-MM-dd, h:mm:ss tt")  // ← Date column
                                                                                    // ✅ ALISIN ANG ItemCount - hindi ginagamit sa table
                })
                .ToListAsync();

            // ✅ GROUP BY OrderId para iwas duplicate
            var uniqueRequests = pendingRequests
                .GroupBy(r => r.OrderId)
                .Select(g => g.OrderByDescending(r => r.RequestedAt).First())
                .ToList();

            return Json(uniqueRequests);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveRequest([FromBody] ApproveRequestRequest request)
        {
            var userId = GetCurrentUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user?.Role != "Owner")
                return Json(new { success = false, message = "Only Owner can approve" });

            var pendingRequest = await _db.PendingRequests
                .Include(r => r.Order)
                .ThenInclude(o => o.Items)
                .FirstOrDefaultAsync(r => r.Id == request.RequestId);

            if (pendingRequest == null)
                return Json(new { success = false, message = "Request not found" });

            if (pendingRequest.Status != "PENDING")
                return Json(new { success = false, message = "Request already processed" });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                if (pendingRequest.RequestType == "VOID")
                {
                    // ========== PROCESS VOID ==========
                    var order = pendingRequest.Order;
                    var itemIds = string.IsNullOrEmpty(pendingRequest.ItemIds)
                        ? new List<int>()
                        : pendingRequest.ItemIds.Split(',').Select(int.Parse).ToList();

                    foreach (var item in order.Items.Where(i => !i.IsVoided))
                    {
                        if (itemIds.Count == 0 || itemIds.Contains(item.OrderItemId))
                        {
                            item.IsVoided = true;
                            item.VoidReason = pendingRequest.Reason;

                            var voidLog = new VoidLog
                            {
                                TransactionId = order.OrderId,
                                ItemId = item.OrderItemId,
                                VoidType = itemIds.Count == 0 ? "transaction" : "item",
                                Reason = pendingRequest.Reason,
                                PreparedFlag = true,
                                PreparedStatus = "PREPARED",
                                UserId = pendingRequest.RequestedBy,
                                ApprovedBy = userId,
                                Timestamp = DateTime.Now
                            };
                            _db.VoidLogs.Add(voidLog);
                        }
                    }

                    order.Status = "VOIDED";
                    order.VoidReason = pendingRequest.Reason;
                    order.VoidedBy = userId;
                    order.VoidedAt = DateTime.Now;
                    order.TotalAmount = 0;  // ✅ VOID = zero
                }
                else if (pendingRequest.RequestType == "REFUND")
                {
                    // ========== PROCESS REFUND ==========
                    var order = pendingRequest.Order;

                    // Ensure OriginalTotalAmount is set
                    if (order.OriginalTotalAmount == 0)
                    {
                        order.OriginalTotalAmount = order.TotalAmount;
                    }

                    var itemIds = pendingRequest.ItemIds.Split(',').Select(int.Parse).ToList();
                    decimal refundAmount = 0;

                    foreach (var itemId in itemIds)
                    {
                        var item = order.Items.FirstOrDefault(i => i.OrderItemId == itemId && !i.IsRefunded);
                        if (item != null)
                        {
                            refundAmount += item.Price * item.Quantity;
                            item.IsRefunded = true;
                            item.RefundReason = pendingRequest.Reason;
                        }
                    }

                    if (refundAmount > 0)
                    {
                        // Create OrderAdjustment
                        var adjustment = new OrderAdjustment
                        {
                            OriginalOrderId = order.OrderId,
                            AdjustmentType = "REFUND",
                            Amount = refundAmount,
                            Reason = pendingRequest.Reason,
                            ReferenceNumber = $"RFD-{DateTime.Now:yyyyMMddHHmmss}",
                            Status = "APPROVED",
                            ApprovedBy = userId,
                            ApprovedAt = DateTime.Now,
                            CreatedAt = DateTime.Now,
                            CreatedBy = pendingRequest.RequestedBy
                        };
                        _db.OrderAdjustments.Add(adjustment);
                        await _db.SaveChangesAsync();

                        // Create RefundTransaction
                        var refund = new RefundTransaction
                        {
                            AdjustmentId = adjustment.AdjustmentId,
                            OriginalOrderId = order.OrderId,
                            Amount = refundAmount,
                            Reason = pendingRequest.Reason,
                            ProcessedBy = userId,
                            RefundReference = adjustment.ReferenceNumber,
                            CreatedAt = DateTime.Now
                        };
                        _db.RefundTransactions.Add(refund);

                        // ✅ REFUND = subtract from total
                        order.TotalAmount -= refundAmount;
                    }

                    // Update status
                    if (order.Items.All(i => i.IsRefunded || i.IsVoided))
                    {
                        order.Status = "REFUNDED";
                    }
                    else
                    {
                        order.Status = "PARTIALLY_REFUNDED";
                    }
                }
                else if (pendingRequest.RequestType == "EXCHANGE_REFUND")
                {
                    pendingRequest.Status = "APPROVED";
                    pendingRequest.ApprovedBy = userId;
                    pendingRequest.ApprovedAt = DateTime.Now;
                }

                pendingRequest.Status = "APPROVED";
                pendingRequest.ApprovedBy = userId;
                pendingRequest.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = $"Request approved successfully" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RejectRequest([FromBody] RejectRequestRequest request)
        {
            var userId = GetCurrentUserId();
            var user = await _db.Users.FindAsync(userId);

            if (user?.Role != "Owner")
                return Json(new { success = false, message = "Only Owner can reject" });

            var pendingRequest = await _db.PendingRequests
                .Include(r => r.Order)  // ✅ IDAGDAG ITO para makuha ang Order
                .FirstOrDefaultAsync(r => r.Id == request.RequestId);

            if (pendingRequest == null)
                return Json(new { success = false, message = "Request not found" });

            pendingRequest.Status = "REJECTED";
            pendingRequest.ApprovedBy = userId;
            pendingRequest.ApprovedAt = DateTime.Now;

            // ✅ IDAGDAG ITO: I-BALIK ANG ORDER STATUS SA COMPLETED
            if (pendingRequest.Order != null && pendingRequest.Order.Status == "PENDING_REFUND")
            {
                pendingRequest.Order.Status = "COMPLETED";
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Request rejected" });
        }

        // ========== SUPPLIER RETURNS ACTIONS ==========

        [HttpGet]
        public async Task<IActionResult> GetReceivingDetails(int id)
        {
            var receiving = await _db.Receivings
                .Include(r => r.Supplier)
                .Include(r => r.ReceivingItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(r => r.ReceivingId == id);

            if (receiving == null)
                return Json(new { success = false });

            var result = new
            {
                receivingId = receiving.ReceivingId,
                drNumber = receiving.DRNumber,
                supplierName = receiving.Supplier?.Name,
                dateReceived = receiving.DateReceived,
                items = receiving.ReceivingItems.Select(ri => new
                {
                    receivingItemId = ri.ReceivingItemId,
                    name = ri.RawMaterial?.Name,
                    acceptedQty = ri.AcceptedQty,
                    acceptedQtyWithUnit = $"{ri.AcceptedQty} {ri.RawMaterial?.Unit}",
                    rejectedQty = ri.RejectedQty,
                    rejectedQtyWithUnit = $"{ri.RejectedQty} {ri.RawMaterial?.Unit}",
                    unit = ri.RawMaterial?.Unit,
                    unitPrice = ri.UnitPrice,
                    returnStatus = ri.ReturnStatus,
                    returnId = ri.ReturnId
                })
            };

            return Json(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSupplierReturn([FromBody] CreateSupplierReturnRequest request)
        {
            try
            {
                var receivingItem = await _db.ReceivingItems
                    .Include(ri => ri.Receiving)
                    .Include(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(ri => ri.ReceivingItemId == request.ReceivingItemId);

                if (receivingItem == null)
                    return Json(new { success = false, message = "Item not found" });

                if (request.Quantity > receivingItem.RejectedQty)
                    return Json(new { success = false, message = "Cannot return more than rejected quantity" });

                var supplierReturn = new SupplierReturn
                {
                    ReturnNumber = GenerateReturnNumber(),
                    ReceivingId = receivingItem.ReceivingId,
                    ReceivingItemId = request.ReceivingItemId,
                    SupplierId = receivingItem.Receiving.SupplierId,
                    RawMaterialId = receivingItem.RawMaterialId,
                    Quantity = request.Quantity,
                    Reason = request.Reason ?? "No reason provided",
                    Status = "Pending Return",
                    ReturnDate = DateTime.Now,
                    CreatedBy = GetCurrentUserId(),
                    Notes = request.Notes ?? ""
                };

                _db.SupplierReturns.Add(supplierReturn);

                receivingItem.ReturnStatus = "PENDING";
                receivingItem.ReturnId = supplierReturn.ReturnId;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Return #{supplierReturn.ReturnNumber} created. Pending return to supplier." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetSupplierReturns()
        {
            var returns = await _db.SupplierReturns
                .Include(r => r.Receiving)
                .Include(r => r.Supplier)
                .Include(r => r.RawMaterial)
                .OrderByDescending(r => r.ReturnDate)
                .Select(r => new
                {
                    r.ReturnId,
                    r.ReturnNumber,
                    r.ReturnDate,
                    DRNumber = r.Receiving.DRNumber,
                    SupplierName = r.Supplier.Name,
                    ItemName = r.RawMaterial.Name,
                    r.Quantity,
                    Unit = r.RawMaterial.Unit,
                    r.Reason,
                    r.Status,
                    r.UnitPrice
                })
                .ToListAsync();

            var summary = new
            {
                pendingCount = returns.Count(r => r.Status == "Pending Return"),
                completedCount = returns.Count(r => r.Status == "Returned"),
                cancelledCount = returns.Count(r => r.Status == "Cancelled")
            };

            return Json(new { success = true, returns = returns, summary = summary });
        }

        [HttpGet]
        public async Task<IActionResult> GetSupplierReturnDetail(int returnId)
        {
            var supplierReturn = await _db.SupplierReturns
                .Include(r => r.ReceivingItem)
                    .ThenInclude(i => i.Receiving)
                        .ThenInclude(rec => rec.Supplier)
                .Include(r => r.ReceivingItem.RawMaterial)
                .FirstOrDefaultAsync(r => r.ReturnId == returnId);

            if (supplierReturn == null)
                return Json(new { success = false, message = "Return not found" });

            var result = new
            {
                success = true,
                @return = new
                {
                    supplierReturn.ReturnId,
                    supplierReturn.ReturnNumber,
                    DRNumber = supplierReturn.ReceivingItem?.Receiving?.DRNumber,
                    SupplierName = supplierReturn.ReceivingItem?.Receiving?.Supplier?.Name,
                    ItemName = supplierReturn.ReceivingItem?.RawMaterial?.Name,
                    supplierReturn.Quantity,
                    Unit = supplierReturn.ReceivingItem?.RawMaterial?.Unit,
                    supplierReturn.Reason,
                    supplierReturn.ReturnDate
                }
            };

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkSupplierReturnCompleted([FromBody] MarkSupplierReturnRequest request)
        {
            try
            {
                var supplierReturn = await _db.SupplierReturns
                    .FirstOrDefaultAsync(r => r.ReturnId == request.ReturnId);

                if (supplierReturn == null)
                    return Json(new { success = false, message = "Return not found" });

                if (supplierReturn.Status != "Pending Return")
                    return Json(new { success = false, message = "Return already processed" });

                supplierReturn.Status = "Returned";
                supplierReturn.ActualReturnDate = DateTime.Now;

                // Update the receiving item
                var receivingItem = await _db.ReceivingItems
                    .FirstOrDefaultAsync(ri => ri.ReceivingItemId == supplierReturn.ReceivingItemId);

                if (receivingItem != null)
                {
                    receivingItem.ReturnStatus = "RETURNED";
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Return marked as completed" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelSupplierReturn([FromBody] CancelSupplierReturnRequest request)
        {
            try
            {
                var supplierReturn = await _db.SupplierReturns
                    .FirstOrDefaultAsync(r => r.ReturnId == request.ReturnId);

                if (supplierReturn == null)
                    return Json(new { success = false, message = "Return not found" });

                if (supplierReturn.Status != "Pending Return")
                    return Json(new { success = false, message = "Cannot cancel - return already processed" });

                supplierReturn.Status = "Cancelled";

                // Update the receiving item - remove return status
                var receivingItem = await _db.ReceivingItems
                    .FirstOrDefaultAsync(ri => ri.ReceivingItemId == supplierReturn.ReceivingItemId);

                if (receivingItem != null)
                {
                    receivingItem.ReturnStatus = null;
                    receivingItem.ReturnId = null;
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Return cancelled" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetApprovedReturns()
        {
            // 1. GET APPROVED VOIDS from PendingRequests
            var approvedVoids = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Include(r => r.Approver)
                .Where(r => r.RequestType == "VOID" && r.Status == "APPROVED")
                .Select(r => new
                {
                    Id = r.Id,
                    OrderNumber = r.Order.OrderNumber,
                    RequestType = "VOID",
                    Reason = r.Reason,
                    Amount = r.Order.TotalAmount,
                    Status = r.Status,
                    RecordedAt = r.ApprovedAt,
                    RequesterName = r.Requester.Username,
                    Source = "APPROVED",
                    IsPendingRequest = true  // ✅ Flag para malaman kung saan hahanapin
                })
                .ToListAsync();

            // 2. GET DIRECT VOIDS - ✅ FIX: HUWAG isama sa listahan na nagre-require ng PendingRequest
            var directVoids = await _db.VoidLogs
                .Include(v => v.Transaction)
                .Include(v => v.User)
                .Where(v => v.VoidType == "item" || v.VoidType == "transaction")
                .Where(v => v.PreparedFlag == false)
                .Select(v => new
                {
                    Id = v.VoidLogId,
                    OrderNumber = v.Transaction.OrderNumber,
                    RequestType = "VOID",
                    Reason = v.Reason,
                    Amount = v.Transaction.TotalAmount,
                    Status = "COMPLETED",
                    RecordedAt = v.Timestamp,
                    RequesterName = v.User.Username,
                    Source = "DIRECT",
                    IsPendingRequest = false  // ✅ Direct void - hindi ito PendingRequest
                })
                .ToListAsync();

            // 3. GET APPROVED REFUNDS from PendingRequests
            var approvedRefunds = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Include(r => r.Approver)
                .Where(r => r.RequestType == "REFUND" && r.Status == "APPROVED")
                .Select(r => new
                {
                    Id = r.Id,
                    OrderNumber = r.Order.OrderNumber,
                    RequestType = "REFUND",
                    Reason = r.Reason,
                    Amount = _db.RefundTransactions.Where(refund => refund.OriginalOrderId == r.OrderId).Sum(refund => refund.Amount),
                    Status = r.Status,
                    RecordedAt = r.ApprovedAt,
                    RequesterName = r.Requester.Username,
                    Source = "APPROVED",
                    IsPendingRequest = true
                })
                .ToListAsync();

            // 4. COMBINE ALL
            var allReturns = new List<object>();
            allReturns.AddRange(approvedVoids);
            allReturns.AddRange(directVoids);
            allReturns.AddRange(approvedRefunds);

            var sortedReturns = allReturns
                .OrderByDescending(r => ((dynamic)r).RecordedAt)
                .ToList();

            return Json(sortedReturns);
        }
        private string GenerateReturnNumber()
        {
            string prefix = "RET";
            int year = DateTime.Now.Year;
            int month = DateTime.Now.Month;
            int count = _db.SupplierReturns.Count(r => r.ReturnDate.Year == year && r.ReturnDate.Month == month) + 1;
            return $"{prefix}-{year}{month:D2}-{count:D4}";
        }

        // Idagdag sa OwnerController.cs
        [HttpGet]
        public async Task<IActionResult> GetRawMaterialsForProduct()
        {
            var rawMaterials = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    id = r.RawMaterialId,
                    name = r.Name,
                    unit = r.Unit,
                    price = r.Price,
                    // ✅ TAMA: OrderBy (ascending) - PINAKAMATANDANG BATCH
                    latestBatchPrice = _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId && b.QuantityRemaining > 0)
                        .OrderBy(b => b.ExpiryDate)      // ✅ UNAHIN ANG MAAGANG EXPIRY (FEFO)
                        .ThenBy(b => b.ReceivedDate)     // ✅ PAG SAME EXPIRY, UNAHIN ANG MATANDA (FIFO)
                        .Select(b => b.UnitPrice)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Json(rawMaterials);
        }

        // Price rounding helper methods - FIXED ENDING 9 ONLY
        private decimal RoundToPriceEnding(decimal price)
        {
            // I-round sa whole number
            decimal rounded = Math.Round(price, 0, MidpointRounding.AwayFromZero);

            // Minimum price check (₱9 para sa maliliit)
            if (rounded > 0 && rounded < 10)
            {
                return 9; // ₱9 minimum
            }

            if (rounded >= 10)
            {
                // Hanapin ang susunod na numero na ending sa 9
                // Halimbawa: 10-19 → 19, 20-29 → 29, 30-39 → 39
                decimal basePrice = Math.Floor(rounded / 10) * 10;
                decimal result = basePrice + 9;

                // Round up only (hindi pwedeng bumaba)
                if (result < rounded)
                {
                    result = basePrice + 10 + 9;
                }

                return result;
            }

            return rounded;
        }

        // ========== EMPLOYEE ATTENDANCE & PERFORMANCE FOR OWNER ==========

        // GET: Owner/GetAllAttendance (for Owner dashboard - today's attendance)
        [HttpGet]
        public async Task<IActionResult> GetAllAttendance(DateTime? date)
        {
            try
            {
                var targetDate = date ?? DateTime.Today;

                var attendances = await _db.EmployeeAttendances
                    .Include(a => a.Employee)
                    .Where(a => a.Date == targetDate)
                    .Select(a => new
                    {
                        id = a.Id,
                        employeeId = a.UserId,
                        employeeName = a.Employee != null ? a.Employee.FullName : "Unknown",
                        date = a.Date,
                        checkInTime = a.CheckInTime.HasValue ? a.CheckInTime.Value.ToString(@"hh\:mm tt") : null,
                        checkOutTime = a.CheckOutTime.HasValue ? a.CheckOutTime.Value.ToString(@"hh\:mm tt") : null,
                        hoursWorked = a.HoursWorked,
                        status = a.Status.ToString(),
                        isPresent = a.Status == AttendanceStatus.Present,
                        isLate = a.Status == AttendanceStatus.Late,
                        isAbsent = a.Status == AttendanceStatus.Absent
                    })
                    .ToListAsync();

                // Get all active employees
                var allEmployees = await _db.Users
                    .Where(u => u.Role.ToLower() == "employee" && u.Status == "Active")
                    .Select(u => new { u.Id, u.FullName })
                    .ToListAsync();

                // Find employees who don't have attendance for today
                var presentEmployeeIds = attendances.Select(a => a.employeeId).ToList();
                var missingEmployees = allEmployees
                    .Where(e => !presentEmployeeIds.Contains(e.Id))
                    .Select(e => new
                    {
                        employeeId = e.Id,
                        employeeName = e.FullName ?? e.Id.ToString(),
                        status = "Absent",
                        isAbsent = true
                    })
                    .ToList();

                return Json(new
                {
                    success = true,
                    date = targetDate,
                    attendances = attendances,
                    missingEmployees = missingEmployees,
                    summary = new
                    {
                        totalEmployees = allEmployees.Count,
                        present = attendances.Count(a => a.status.ToLower() == "present"),
                        absent = attendances.Count(a => a.status.ToLower() == "absent") + missingEmployees.Count,
                        late = attendances.Count(a => a.status.ToLower() == "late")
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Owner/GetEmployeePerformance (for Owner dashboard - monthly summary)
        [HttpGet]
        public async Task<IActionResult> GetEmployeePerformance(int? year, int? month)
        {
            try
            {
                var targetYear = year ?? DateTime.Today.Year;
                var targetMonth = month ?? DateTime.Today.Month;
                var startDate = new DateTime(targetYear, targetMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var performances = await _db.EmployeePerformances
                    .Include(p => p.Employee)
                    .Where(p => p.Date >= startDate && p.Date <= endDate)
                    .GroupBy(p => p.UserId)
                    .Select(g => new
                    {
                        employeeId = g.Key,
                        employeeName = g.First().Employee != null ? g.First().Employee.FullName : "Unknown",
                        totalSales = g.Sum(p => p.TotalSales),
                        ordersProcessed = g.Sum(p => p.OrdersProcessed),
                        averageRating = g.Average(p => p.Rating),
                        daysWorked = g.Count()
                    })
                    .OrderByDescending(p => p.totalSales)
                    .ToListAsync();

                // Get attendance data for the month
                var attendances = await _db.EmployeeAttendances
                    .Where(a => a.Date >= startDate && a.Date <= endDate)
                    .GroupBy(a => a.UserId)
                    .Select(g => new
                    {
                        employeeId = g.Key,
                        totalHours = g.Sum(a => a.HoursWorked),
                        daysPresent = g.Count(a => a.Status == AttendanceStatus.Present),
                        daysLate = g.Count(a => a.Status == AttendanceStatus.Late),
                        daysAbsent = g.Count(a => a.Status == AttendanceStatus.Absent)
                    })
                    .ToListAsync();

                // ✅ FIXED: Use a dictionary for faster lookup
                var attendanceDict = attendances.ToDictionary(a => a.employeeId, a => a);

                var result = performances.Select(p => new
                {
                    p.employeeId,
                    p.employeeName,
                    p.totalSales,
                    p.ordersProcessed,
                    p.averageRating,
                    attendance = attendanceDict.ContainsKey(p.employeeId)
                        ? attendanceDict[p.employeeId]
                        : new { employeeId = 0, totalHours = 0m, daysPresent = 0, daysLate = 0, daysAbsent = 0 }
                });

                return Json(new
                {
                    success = true,
                    year = targetYear,
                    month = targetMonth,
                    performances = result,
                    summary = new
                    {
                        totalSales = performances.Sum(p => p.totalSales),
                        totalOrders = performances.Sum(p => p.ordersProcessed),
                        totalEmployees = performances.Count
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Owner/GetEmployeeAttendanceHistory (for specific employee)
        [HttpGet]
        public async Task<IActionResult> GetEmployeeAttendanceHistory(int employeeId, int? year, int? month)
        {
            try
            {
                var targetYear = year ?? DateTime.Today.Year;
                var targetMonth = month ?? DateTime.Today.Month;

                var startDate = new DateTime(targetYear, targetMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var employee = await _db.Users.FindAsync(employeeId);

                var attendances = await _db.EmployeeAttendances
                    .Where(a => a.UserId == employeeId && a.Date >= startDate && a.Date <= endDate)
                    .OrderBy(a => a.Date)
                    .Select(a => new
                    {
                        a.Date,
                        checkInTime = a.CheckInTime.HasValue ? a.CheckInTime.Value.ToString(@"hh\:mm tt") : null,
                        checkOutTime = a.CheckOutTime.HasValue ? a.CheckOutTime.Value.ToString(@"hh\:mm tt") : null,
                        a.HoursWorked,
                        status = a.Status.ToString()
                    })
                    .ToListAsync();

                var summary = new
                {
                    present = attendances.Count(a => a.status.ToLower() == "present"),
                    absent = attendances.Count(a => a.status.ToLower() == "absent"),
                    late = attendances.Count(a => a.status.ToLower() == "late"),
                    totalHours = attendances.Sum(a => a.HoursWorked)
                };

                return Json(new
                {
                    success = true,
                    employeeName = employee?.FullName,
                    year = targetYear,
                    month = targetMonth,
                    attendances = attendances,
                    summary = summary
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Owner/GetEmployeePerformanceDetails (for specific employee)
        [HttpGet]
        public async Task<IActionResult> GetEmployeePerformanceDetails(int employeeId, int? year, int? month)
        {
            try
            {
                var targetYear = year ?? DateTime.Today.Year;
                var targetMonth = month ?? DateTime.Today.Month;

                var startDate = new DateTime(targetYear, targetMonth, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var employee = await _db.Users.FindAsync(employeeId);

                var performances = await _db.EmployeePerformances
                    .Where(p => p.UserId == employeeId && p.Date >= startDate && p.Date <= endDate)
                    .OrderBy(p => p.Date)
                    .Select(p => new
                    {
                        p.Date,
                        p.TotalSales,
                        p.OrdersProcessed,
                        p.Rating,
                        p.Notes
                    })
                    .ToListAsync();

                var summary = new
                {
                    totalSales = performances.Sum(p => p.TotalSales),
                    totalOrders = performances.Sum(p => p.OrdersProcessed),
                    averageRating = performances.Any() ? performances.Average(p => p.Rating) : 0,
                    daysWorked = performances.Count
                };

                return Json(new
                {
                    success = true,
                    employeeName = employee?.FullName,
                    year = targetYear,
                    month = targetMonth,
                    performances = performances,
                    summary = summary
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeOnlineStatus()
        {
            var today = DateTime.Today;

            // Get all employees
            var employees = await _db.Users
                .Where(u => u.Role.ToLower() == "employee" || u.Role.ToLower() == "staff")
                .Select(u => new { u.Id, u.FullName, u.IsLoggedIn, u.LastLoginTime, u.LastLogoutTime })
                .ToListAsync();

            // Get attendance for today
            var attendances = await _db.EmployeeAttendances
                .Where(a => a.Date == today)
                .ToListAsync();

            var onlineList = new List<object>();
            var offlineList = new List<object>();

            foreach (var emp in employees)
            {
                var attendance = attendances.FirstOrDefault(a => a.UserId == emp.Id);

                string checkInTimeStr = null;
                string checkOutTimeStr = null;
                decimal hoursWorked = 0;
                bool isOnline = false;

                if (attendance != null)
                {
                    if (attendance.CheckInTime.HasValue)
                    {
                        var checkIn = attendance.CheckInTime.Value;
                        checkInTimeStr = $"{checkIn.Hours:D2}:{checkIn.Minutes:D2}";
                    }

                    if (attendance.CheckOutTime.HasValue)
                    {
                        var checkOut = attendance.CheckOutTime.Value;
                        checkOutTimeStr = $"{checkOut.Hours:D2}:{checkOut.Minutes:D2}";
                    }

                    hoursWorked = attendance.HoursWorked;

                    // Online kung naka-time in ngayong araw at hindi naka-time out
                    isOnline = attendance.CheckInTime.HasValue && !attendance.CheckOutTime.HasValue;
                }

                // Format login/logout times
                string lastLoginStr = null;
                string lastLogoutStr = null;

                if (emp.LastLoginTime.HasValue)
                {
                    lastLoginStr = emp.LastLoginTime.Value.ToString("hh:mm tt");
                }

                if (emp.LastLogoutTime.HasValue)
                {
                    lastLogoutStr = emp.LastLogoutTime.Value.ToString("hh:mm tt");
                }

                if (isOnline)
                {
                    onlineList.Add(new
                    {
                        employeeId = emp.Id,
                        employeeName = emp.FullName ?? emp.Id.ToString(),
                        status = "Online",
                        checkInTime = checkInTimeStr,
                        hoursWorked = hoursWorked,
                        lastLoginTime = lastLoginStr
                    });
                }
                else
                {
                    offlineList.Add(new
                    {
                        employeeId = emp.Id,
                        employeeName = emp.FullName ?? emp.Id.ToString(),
                        status = "Offline",
                        checkInTime = checkInTimeStr,
                        checkOutTime = checkOutTimeStr,
                        hoursWorked = hoursWorked,
                        lastLoginTime = lastLoginStr,
                        lastLogoutTime = lastLogoutStr
                    });
                }
            }

            return Json(new
            {
                online = onlineList,
                offline = offlineList
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnToSupplier([FromBody] CreateSupplierReturnRequest request)
        {
            try
            {
                if (request == null || request.ReceivingItemId == 0)
                    return Json(new { success = false, message = "Invalid request" });

                var receivingItem = await _db.ReceivingItems
                    .Include(ri => ri.Receiving)
                    .Include(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(ri => ri.ReceivingItemId == request.ReceivingItemId);

                if (receivingItem == null)
                    return Json(new { success = false, message = "Receiving item not found" });

                if (request.Quantity <= 0)
                    return Json(new { success = false, message = "Quantity must be greater than 0" });

                if (request.Quantity > receivingItem.RejectedQty)
                    return Json(new { success = false, message = $"Cannot return more than {receivingItem.RejectedQty} {receivingItem.RawMaterial?.Unit}" });

                // Check if may existing pending return
                var existingReturn = await _db.SupplierReturns
                    .FirstOrDefaultAsync(r => r.ReceivingItemId == request.ReceivingItemId && r.Status == "Pending Return");

                if (existingReturn != null)
                    return Json(new { success = false, message = "A return request is already pending for this item" });

                var supplierReturn = new SupplierReturn
                {
                    ReturnNumber = GenerateReturnNumber(),
                    ReceivingId = receivingItem.ReceivingId,
                    ReceivingItemId = request.ReceivingItemId,
                    SupplierId = receivingItem.Receiving.SupplierId,
                    RawMaterialId = receivingItem.RawMaterialId,
                    Quantity = request.Quantity,
                    UnitPrice = receivingItem.UnitPrice,
                    Reason = request.Reason ?? "No reason provided",
                    Status = "Pending Return",
                    ReturnDate = DateTime.Now,
                    CreatedBy = GetCurrentUserId(),
                    Notes = request.Notes ?? ""
                };

                _db.SupplierReturns.Add(supplierReturn);

                // Update receiving item
                receivingItem.ReturnStatus = "PENDING";
                receivingItem.ReturnId = supplierReturn.ReturnId;

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Return #{supplierReturn.ReturnNumber} created successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Owner/GetApprovedRefunds
        [HttpGet]
        public async Task<IActionResult> GetApprovedRefunds()
        {
            var approvedRefunds = await _db.PendingRequests
                .Include(r => r.Order)
                .Include(r => r.Requester)
                .Where(r => r.RequestType == "REFUND" && r.Status == "APPROVED")
                .Select(r => new
                {
                    Id = r.Id,
                    OrderNumber = r.Order.OrderNumber,
                    Reason = r.Reason,
                    Amount = _db.RefundTransactions.Where(refund => refund.OriginalOrderId == r.OrderId).Sum(refund => refund.Amount),
                    RecordedAt = r.ApprovedAt,
                    RequesterName = r.Requester.Username
                })
                .ToListAsync();

            return Json(approvedRefunds);
        }

        [HttpGet]
        public async Task<IActionResult> GetInventoryData()
        {
            var rawMaterialsData = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    r.Category,
                    r.MinQty,
                    r.Price,
                    r.Unit,
                    r.SKU,
                    r.BaseUnit,
                    r.ExpirationDate,
                    TotalStockInBaseUnit = _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId)
                        .Sum(b => (decimal?)b.QuantityRemaining) ?? 0,
                    MinQtyInBaseUnit = r.MinQtyInBaseUnit
                })
                .ToListAsync();

            var rawMaterialsList = new List<object>();
            foreach (var r in rawMaterialsData)
            {
                decimal displayStock = r.TotalStockInBaseUnit;
                decimal displayMinQty = r.MinQtyInBaseUnit;

                if ((r.BaseUnit == "g" && r.Unit == "kg") || (r.BaseUnit == "mL" && r.Unit == "L"))
                {
                    displayStock = r.TotalStockInBaseUnit / 1000;
                    displayMinQty = r.MinQtyInBaseUnit / 1000;
                }

                rawMaterialsList.Add(new
                {
                    r.RawMaterialId,
                    Name = r.Name,
                    Category = r.Category.ToString(),
                    Stock = displayStock,
                    MinQty = displayMinQty,
                    Unit = r.Unit,
                    SKU = r.SKU,
                    Status = displayStock <= displayMinQty ? (displayStock == 0 ? "No Stock" : "Low Stock") : "In Stock"
                });
            }

            return Json(new { success = true, rawMaterials = rawMaterialsList });
        }

        // GET: Owner/GetVoidHistory
        [HttpGet]
        public async Task<IActionResult> GetVoidHistory()
        {
            var voids = await _db.VoidLogs
                .Include(v => v.Transaction)
                .Include(v => v.User)
                .Where(v => v.VoidType == "item" || v.VoidType == "transaction")
                .OrderByDescending(v => v.Timestamp)
                .Select(v => new
                {
                    Id = v.VoidLogId,
                    OrderNumber = v.Transaction.OrderNumber,
                    Reason = v.Reason,
                    // ✅ Use OriginalTotalAmount instead of TotalAmount (which becomes 0 after void)
                    Amount = v.Transaction.OriginalTotalAmount > 0
                        ? v.Transaction.OriginalTotalAmount
                        : v.Transaction.TotalAmount,
                    RecordedAt = v.Timestamp,
                    RequesterName = v.User.Username,
                    OrderId = v.Transaction.OrderId  // ✅ ADD THIS for View Receipt button
                })
                .ToListAsync();

            return Json(voids);
        }


        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("Owner_UserId") ?? 0;
        }

        [HttpPost]
        public async Task<IActionResult> RefreshAllProductPrices()
        {
            try
            {
                var allProductSizes = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .ToListAsync();

                int updatedCount = 0;

                foreach (var productSize in allProductSizes)
                {
                    // ✅ SKIP MANUAL PRICES - ITO ANG PINAKAIMPORTANTE!
                    if (productSize.IsManualPrice)
                    {
                        continue;  // Huwag galawin ang manual price
                    }

                    decimal totalCost = 0;
                    foreach (var ri in productSize.RecipeItems)
                    {
                        decimal activePrice = await GetFIFOUnitPrice(ri.RawMaterialId);
                        decimal quantityForCost = ri.Quantity;

                        if (ri.RawMaterial?.BaseUnit == "g" || ri.RawMaterial?.BaseUnit == "mL")
                        {
                            quantityForCost = ri.Quantity / 1000;
                        }

                        totalCost += quantityForCost * activePrice;
                    }

                    if (productSize.Margin > 0 && productSize.Margin < 1)
                    {
                        decimal computedPrice = totalCost / (1 - productSize.Margin);
                        decimal newPrice = RoundToPriceEnding(computedPrice);

                        if (productSize.Price != newPrice)
                        {
                            productSize.Price = newPrice;
                            updatedCount++;
                        }
                    }
                }

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"Updated {updatedCount} product prices to ending 9", updatedCount = updatedCount });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task RecomputeProductSizePrice(int productSizeId)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == productSizeId);

            if (productSize == null) return;

            decimal totalCost = 0;
            foreach (var ri in productSize.RecipeItems)
            {
                decimal activePrice = await GetFIFOUnitPrice(ri.RawMaterialId);
                decimal quantityForCost = ri.Quantity;

                if (ri.RawMaterial?.BaseUnit == "g" || ri.RawMaterial?.BaseUnit == "mL")
                {
                    quantityForCost = ri.Quantity / 1000;
                }

                totalCost += quantityForCost * activePrice;
            }

            if (productSize.Margin > 0 && productSize.Margin < 1)
            {
                decimal computedPrice = totalCost / (1 - productSize.Margin);
                decimal newPrice = RoundToPriceEnding(computedPrice);
                productSize.Price = newPrice;
                await _db.SaveChangesAsync();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProductById(int productId)
        {
            try
            {
                var product = await _db.Products
                    .Where(p => p.ProductId == productId && !p.IsDeleted)
                    .Select(p => new
                    {
                        p.ProductId,
                        p.Name,
                        p.SKU,
                        p.Category,
                        p.Description,
                        p.ImagePath,
                        Sizes = p.Sizes.Select(s => new
                        {
                            s.ProductSizeId,
                            s.SizeName,
                            s.SKU,
                            s.Price,
                            s.IsAvailableInPOS
                        })
                    })
                    .FirstOrDefaultAsync();

                if (product == null)
                    return Json(new { success = false, message = "Product not found" });

                return Json(new { success = true, product = product });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProductWithSizes(int productId)
        {
            var product = await _db.Products
                .Where(p => p.ProductId == productId && !p.IsDeleted)
                .Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.Category,
                    p.Description,
                    p.ImagePath,
                    p.SKU,
                    Sizes = p.Sizes.Where(s => !s.IsDeleted).Select(s => new
                    {
                        s.ProductSizeId,
                        s.SizeName,
                        s.SKU,
                        s.Price,
                        s.IsAvailableInPOS,
                        s.Margin
                    })
                })
                .FirstOrDefaultAsync();

            if (product == null)
                return Json(new { success = false, message = "Product not found" });

            return Json(new { success = true, product = product });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteProductSize([FromBody] DeleteProductSizeRequest request)
        {
            try
            {
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == request.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Size not found" });

                // Delete associated recipe items first
                if (productSize.RecipeItems != null && productSize.RecipeItems.Any())
                {
                    _db.RecipeItems.RemoveRange(productSize.RecipeItems);
                }

                // Delete the product size
                _db.ProductSizes.Remove(productSize);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Size variant deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProduct([FromBody] UpdateProductRequest request)
        {
            try
            {
                var product = await _db.Products
                    .Include(p => p.Sizes)
                    .FirstOrDefaultAsync(p => p.ProductId == request.ProductId);

                if (product == null)
                    return Json(new { success = false, message = "Product not found" });

                // Update basic info
                product.Name = request.Name;
                product.Category = request.Category ?? "";
                product.Description = request.Description ?? "";
                product.ImagePath = request.ImagePath ?? "";

                // Delete sizes that are marked for deletion
                if (request.DeleteSizeIds != null && request.DeleteSizeIds.Any())
                {
                    var sizesToDelete = product.Sizes.Where(s => request.DeleteSizeIds.Contains(s.ProductSizeId)).ToList();
                    foreach (var size in sizesToDelete)
                    {
                        // Delete recipe items first
                        var recipeItems = _db.RecipeItems.Where(ri => ri.ProductSizeId == size.ProductSizeId);
                        _db.RecipeItems.RemoveRange(recipeItems);

                        // Then delete the size
                        product.Sizes.Remove(size);
                    }
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Product updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        public class UpdateProductRequest
        {
            public int ProductId { get; set; }
            public string Name { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string ImagePath { get; set; }
            public List<int> DeleteSizeIds { get; set; } = new List<int>();
        }

        public class DeleteProductSizeRequest
        {
            public int ProductSizeId { get; set; }
        }

        public class AddSizeRequest
        {
            public int ProductId { get; set; }
            public string SizeName { get; set; }
            public decimal Price { get; set; }
            public decimal Margin { get; set; }
            public List<IngredientItem> Ingredients { get; set; }
        }

        public class IngredientItem
        {
            public int RawMaterialId { get; set; }
            public decimal Quantity { get; set; }
            public string UnitUsed { get; set; }
        }

        public class ApproveRequestRequest
        {
            public int RequestId { get; set; }
        }

        public class RejectRequestRequest
        {
            public int RequestId { get; set; }
        }

        // Request Models for Returns
        public class UpdateReturnStatusRequest
        {
            public int Id { get; set; }
            public string Status { get; set; }
        }

        public class DeleteReturnRequest
        {
            public int Id { get; set; }
        }

        // ========== RECEIVING VIEW MODELS ==========
        public class ReceivingViewModel
        {
            public int SupplierId { get; set; }
            public string DRNumber { get; set; }
            public string InvoiceNumber { get; set; }
            public DateTime DateReceived { get; set; } = DateTime.Now;
            public string Remarks { get; set; }
            public List<ReceivingItemViewModel> Items { get; set; } = new List<ReceivingItemViewModel>();
        }

        public class ReceivingItemViewModel
        {
            public int RawMaterialId { get; set; }
            public string RawMaterialName { get; set; }
            public decimal? OrderedQty { get; set; }
            public decimal ReceivedQty { get; set; }
            public decimal AcceptedQty { get; set; }
            public decimal RejectedQty { get; set; }
            public string Disposition { get; set; }
            public string RejectionReason { get; set; }
            public decimal UnitPrice { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        // ========== SUPPLIER RETURN REQUEST MODELS ==========

        public class CreateSupplierReturnRequest
        {
            public int ReceivingItemId { get; set; }
            public decimal Quantity { get; set; }
            public string Reason { get; set; }
            public string Notes { get; set; }
        }

        public class MarkSupplierReturnRequest
        {
            public int ReturnId { get; set; }
        }

        public class CancelSupplierReturnRequest
        {
            public int ReturnId { get; set; }
        }


    }
} 