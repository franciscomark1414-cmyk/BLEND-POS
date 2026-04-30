using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
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
                .ToListAsync();

            foreach (var productSize in allProductSizes)
            {
                decimal totalCost = 0;
                foreach (var ri in productSize.RecipeItems)
                {
                    decimal activePrice = await GetFIFOUnitPrice(ri.RawMaterialId);
                    totalCost += ri.Quantity * activePrice;
                }

                if (productSize.Margin > 0 && productSize.Margin < 1)
                {
                    decimal newPrice = Math.Round(totalCost / (1 - productSize.Margin), 2);
                    productSize.Price = newPrice;
                }
            }

            await _db.SaveChangesAsync();
        }

        public async Task<ActionResult> Dashboard()
        {// Get current date

            await RefreshProductPricesInternal();

            DateTime today = DateTime.Today;

            // ========== TODAY'S ORDERS ==========
            var todaysOrders = _db.Orders.Where(o => o.Date.Date == today && o.Status != "Cancelled").ToList();
            ViewBag.TodaysSales = todaysOrders.Sum(o => o.TotalAmount);  // TODAY'S REVENUE
            ViewBag.TotalOrdersToday = todaysOrders.Count();

            // ========== ALL-TIME TOTAL REVENUE (FIXED!) ==========
            var allTimeRevenue = _db.Orders
                .Where(o => o.Status != "Cancelled")
                .Sum(o => (decimal?)o.TotalAmount) ?? 0;
            ViewBag.AllTimeRevenue = allTimeRevenue;  // ← BAGO: ito ang ALL TIME

            // Para sa 30% profit estimate
            ViewBag.EstimatedProfit = allTimeRevenue * 0.3m;

            // ========== CRITICAL STOCK ALERT ==========
            // Critical stock = Stock <= MinQty
            int criticalStockCount = _db.RawMaterials
                .Where(r => r.IsDeleted == false && r.Stock <= r.MinQty)
                .Count();
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

            // ========== WASTE LOGS (today) ============
            int wasteCount = _db.WasteLogs.Count();
            ViewBag.WasteCount = wasteCount;

            // Total other alerts (for badge)
            ViewBag.TotalOtherAlerts = expiringCount + expiredCount + wasteCount;

            // Keep existing low stock count for compatibility
            ViewBag.LowStockCount = criticalStockCount;

            // ========== SALES TREND (Last 7 Days) ==========
            var last7Days = Enumerable.Range(0, 7)
                .Select(i => today.AddDays(-6 + i))
                .ToList();

            var salesLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();
            var salesValues = new List<decimal>();

            foreach (var day in last7Days)
            {
                var dailySales = _db.Orders
                    .Where(o => o.Date.Date == day.Date && o.Status != "Cancelled")
                    .Sum(o => (decimal?)o.TotalAmount) ?? 0;
                salesValues.Add(dailySales);
            }

            ViewBag.SalesLabels = salesLabels;
            ViewBag.SalesValues = salesValues;

            // ========== BEST SELLING PRODUCTS (FIXED) ==========
            var bestSelling = new List<BestSellingProductVM>();

            try
            {
                // Get order items first, then do grouping in memory
                var orderItems = _db.OrderItems
                    .Include(oi => oi.Order)
                    .Include(oi => oi.Product)
                    .Where(oi => oi.Order.Status != "Cancelled")
                    .ToList(); // Execute query first

                bestSelling = orderItems
    .GroupBy(oi => new {
        ProductName = oi.Product?.Name ?? "Unknown",
        SizeName = oi.ProductSize?.SizeName ?? "Regular"  // ← ISAMA ANG SIZE
    })
    .Select(g => new BestSellingProductVM
    {
        Name = g.Key.ProductName,
        SizeName = g.Key.SizeName,
        TotalSold = g.Sum(oi => oi.Quantity),
        Price = 0
    })
    .Where(p => p.TotalSold > 0)
    .OrderByDescending(p => p.TotalSold)
    .Take(5)
    .Select((p, index) => new BestSellingProductVM
    {
        Rank = index + 1,
        Name = p.Name,
        SizeName = p.SizeName,
        TotalSold = p.TotalSold,
        Price = 0
    })
    .ToList();
            }
            catch (Exception ex)
            {
                // Log error if needed
                bestSelling = new List<BestSellingProductVM>();
            }

            // ========== RAW MATERIALS MASTER RECORD (para sa Raw Materials module) ==========
            // Walang Stock at MinQty - master record lang
            ViewBag.RawMaterialsMaster = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    r.Category,
                    r.Unit,
                    r.SKU,
                    Status = r.Stock <= r.MinQty ? "Low Stock" : "In Stock"
                })
                .ToListAsync();

            // Raw materials for inventory module
            ViewBag.RawMaterials = _db.RawMaterials
    .Where(r => !r.IsDeleted)
    .Select(r => new RawMaterial
    {
        RawMaterialId = r.RawMaterialId,
        Name = r.Name,
        Category = r.Category,
        MinQty = r.MinQty,
        Price = r.Price,
        Unit = r.Unit,
        SKU = r.SKU,
        ExpirationDate = r.ExpirationDate,

        // 🔥 IMPORTANT: recompute stock from batches
        Stock = _db.RawMaterialBatches
            .Where(b => b.RawMaterialId == r.RawMaterialId)
            .Sum(b => (decimal?)b.QuantityRemaining) ?? 0

    })
    .ToList();
            var products = await _db.Products
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
                IsAvailableInPOS = s.IsAvailableInPOS
            }).ToList()
        })
        .ToListAsync();

            var dashboardViewModel = new DashboardViewModel
            {
                BestSellingProducts = bestSelling,
                Products = products  // ← GAMITIN ANG products NA MAY SIZES
            };


            // ========== ENSURE ALL ViewBag PROPERTIES HAVE VALUES ==========
            ViewBag.TotalRevenue = ViewBag.TotalRevenue ?? 0;
            ViewBag.EstimatedProfit = ViewBag.EstimatedProfit ?? 0;
            ViewBag.TotalWasteCost = ViewBag.TotalWasteCost ?? 0;
            ViewBag.CriticalStockCount = ViewBag.CriticalStockCount ?? 0;
            ViewBag.ExpiringCount = ViewBag.ExpiringCount ?? 0;
            ViewBag.ExpiredCount = ViewBag.ExpiredCount ?? 0;
            ViewBag.WasteCount = ViewBag.WasteCount ?? 0;
            ViewBag.TotalOtherAlerts = ViewBag.TotalOtherAlerts ?? 0;
            ViewBag.LowStockCount = ViewBag.LowStockCount ?? 0;


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
            var model = new ProductViewModel
            {
                RawMaterials = _db.RawMaterials.Where(r => !r.IsDeleted).ToList(),
                Sizes = new List<ProductSizeViewModel>
        {
            new ProductSizeViewModel { SizeName = "Small", RecipeItems = new List<RecipeItemViewModel>() },
            new ProductSizeViewModel { SizeName = "Medium", RecipeItems = new List<RecipeItemViewModel>() },
            new ProductSizeViewModel { SizeName = "Large", RecipeItems = new List<RecipeItemViewModel>() }  // Fixed: SizeName not Sizename
        }
            };

            return View(model);  // Fixed: Added model parameter
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProduct(ProductViewModel model)
        {
            try
            {
                // Remove RawMaterialName from validation
                ModelState.Remove("Sizes[*].RecipeItems[*].RawMaterialName");

                // Validate that at least one size exists
                if (model.Sizes == null || !model.Sizes.Any())
                {
                    return Json(new { success = false, message = "Please add at least one size" });
                }

                // Validate each size has at least one recipe item
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

                // ========== 1. GENERATE PRODUCT SKU (AUTO-NUMBER) ==========
                string userPrefix = model.SKU?.Trim().ToUpper() ?? "PRD";

                // Bilangin ang existing products na may parehong prefix
                int existingCount = await _db.Products.CountAsync(p => p.SKU.StartsWith(userPrefix));
                string productNumber = (existingCount + 1).ToString("D3");
                string generatedProductSKU = $"{userPrefix}{productNumber}";

                // Create new product (gamitin ang generated SKU, hindi ang user input)
                var product = new Product
                {
                    Name = model.Name.Trim(),
                    SKU = generatedProductSKU,  // ← NAKA-AUTO-GENERATE NA
                    Category = model.Category?.Trim() ?? "",
                    Description = model.Description?.Trim() ?? "",
                    ImagePath = model.ImagePath ?? "",
                    Sizes = new List<ProductSize>()
                };

                foreach (var sizeVM in model.Sizes)
                {
                    // Skip if size name is empty
                    if (string.IsNullOrWhiteSpace(sizeVM.SizeName))
                        continue;

                    decimal margin = model.SelectedProfitMargin switch
                    {
                        "30%" => 0.3m,
                        "40%" => 0.4m,
                        "50%" => 0.5m,
                        _ => 0.3m
                    };

                    // ========== 2. GENERATE SIZE SKU ==========
                    string sizeSKU = GenerateSizeSKU(generatedProductSKU, sizeVM.SizeName);

                    var productSize = new ProductSize
                    {
                        SizeName = sizeVM.SizeName.Trim(),
                        SKU = sizeSKU ?? "",  // ← IDAGDAG ITO
                        Margin = margin,  // ← IDAGDAG ITO (kung meron sa model mo)
                        RecipeItems = new List<RecipeItem>()
                    };
                   

                    // Calculate total cost from recipe items
                    var totalCost = 0m;
                    var validRecipeCount = 0;

                    foreach (var recipeVM in sizeVM.RecipeItems)
                    {
                        if (recipeVM.RawMaterialId > 0 && recipeVM.Quantity > 0)
                        {
                            var latestPrice = await _db.RawMaterialBatches
                                .Where(b => b.RawMaterialId == recipeVM.RawMaterialId)
                                .OrderByDescending(b => b.ReceivedDate)
                                .Select(b => b.UnitPrice)
                                .FirstOrDefaultAsync();

                            if (latestPrice >= 0)
                            {
                                var cost = recipeVM.Quantity * latestPrice;
                                totalCost += cost;
                                validRecipeCount++;

                                productSize.RecipeItems.Add(new RecipeItem
                                {
                                    RawMaterialId = recipeVM.RawMaterialId,
                                    Quantity = recipeVM.Quantity
                                });
                            }
                        }
                    }

                    // Check if recipe has at least one ingredient
                    if (validRecipeCount == 0)
                    {
                        return Json(new { success = false, message = $"Please add at least one ingredient for {sizeVM.SizeName}" });
                    }

                    // If price is provided manually, use it; otherwise calculate from cost
                    if (sizeVM.Price > 0)
                    {
                        productSize.Price = sizeVM.Price;
                    }
                    else
                    {
                        productSize.Price = Math.Round(totalCost / (1 - margin), 2);
                    }

                    product.Sizes.Add(productSize);
                }

                // Check if any sizes were added
                if (!product.Sizes.Any())
                {
                    return Json(new { success = false, message = "Please add at least one valid size" });
                }

               
                _db.Products.Add(product);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Product added successfully! SKU: {generatedProductSKU}", redirect = "/Owner/Dashboard" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving product: " + ex.Message });
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
        public async Task<IActionResult> GetReceipts(string date = null)
        {
            DateTime? filterDate = null;
            if (!string.IsNullOrEmpty(date))
                filterDate = DateTime.Parse(date);

            var query = _db.Orders
                .Include(o => o.Items)  // ← CHANGE: OrderItems -> Items
                .Where(o => o.Status == "Completed");

            if (filterDate.HasValue)
                query = query.Where(o => o.Date.Date == filterDate.Value.Date);

            var receipts = await query
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    orderId = o.OrderId,
                    receiptNumber = o.ReceiptNumber ?? $"ORD-{o.OrderId}",
                    date = o.Date.ToString("yyyy-MM-dd HH:mm:ss"),
                    itemCount = o.Items.Sum(oi => oi.Quantity),  // ← CHANGE
                    subtotal = o.Items.Sum(oi => oi.Price * oi.Quantity),  // ← CHANGE
                    discountType = o.DiscountType ?? "None",
                    discountPercent = o.DiscountPercent,
                    discountAmount = o.DiscountAmount,
                    total = o.TotalAmount,
                    paymentMethod = o.PaymentMethod ?? "Cash"
                })
                .ToListAsync();

            return Json(new { success = true, receipts = receipts });
        }
        [HttpGet]
        public async Task<IActionResult> GetReceiptDetail(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)  // ← CHANGE: OrderItems -> Items
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)  // ← CHANGE
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return Json(new { success = false, message = "Receipt not found" });

            return Json(new
            {
                success = true,
                receipt = new
                {
                    receiptNumber = order.ReceiptNumber ?? $"ORD-{order.OrderId}",
                    date = order.Date,
                    items = order.Items.Select(oi => new  // ← CHANGE
                    {
                        name = oi.Product.Name,
                        size = oi.ProductSize.SizeName,
                        quantity = oi.Quantity,
                        price = oi.Price,
                        subtotal = oi.Price * oi.Quantity
                    }),
                    subtotal = order.Items.Sum(oi => oi.Price * oi.Quantity),  // ← CHANGE
                    discountType = order.DiscountType ?? "None",
                    discountPercent = order.DiscountPercent,
                    discountAmount = order.DiscountAmount,
                    total = order.TotalAmount,
                    paymentMethod = order.PaymentMethod ?? "Cash"
                }
            });
        }
        // Helper method para kumuha ng FIFO unit price
        // Palitan ang GetFIFOUnitPrice ng ganito:
        private async Task<decimal> GetFIFOUnitPrice(int rawMaterialId)
        {
            return await _fifoService.GetActiveBatchPrice(rawMaterialId);
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

                // ✅ I-check kung may recipe items
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
                    decimal total = recipeItem.Quantity * fifoPrice;
                    totalCost += total;

                    ingredientsWithPrices.Add(new
                    {
                        rawMaterialId = recipeItem.RawMaterialId,
                        quantity = recipeItem.Quantity,
                        name = recipeItem.RawMaterial.Name,
                        unit = recipeItem.RawMaterial.Unit,
                        price = fifoPrice,
                        total = total
                    });
                }

                // Compute profit
                decimal profit = productSize.Price - totalCost;

                // Compute available stock
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
                // ✅ GAMITIN ANG FIFO PRICE (katulad ng GetRecipe)
                decimal fifoPrice = await GetFIFOUnitPrice(recipeItem.RawMaterialId);

                // Fallback kung walang active batch
                if (fifoPrice == 0 && recipeItem.RawMaterial != null)
                {
                    fifoPrice = recipeItem.RawMaterial.Price;
                }

                var total = recipeItem.Quantity * fifoPrice;
                totalCost += total;

                ingredientsWithPrices.Add(new
                {
                    rawMaterialId = recipeItem.RawMaterialId,
                    quantity = recipeItem.Quantity,
                    name = recipeItem.RawMaterial?.Name ?? "Unknown",
                    unit = recipeItem.RawMaterial?.Unit ?? "",
                    price = fifoPrice,   // ✅ FIFO price na
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
            try
            {
                // I-load ang product size kasama ang recipe items at raw materials
                var productSize = await _db.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .Include(ps => ps.Product)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == model.ProductSizeId);

                if (productSize == null)
                    return Json(new { success = false, message = "Product size not found" });

                // ✅ CHECK KUNG MAY STOCK BAGO I-EDIT

                // I-save ang margin
                productSize.Margin = model.Margin;

                // I-delete ang existing recipe items
                if (productSize.RecipeItems != null && productSize.RecipeItems.Any())
                {
                    _db.RecipeItems.RemoveRange(productSize.RecipeItems);
                    await _db.SaveChangesAsync();
                }

                // I-clear ang collection
                productSize.RecipeItems = new List<RecipeItem>();

                // I-add ang bagong recipe items
                foreach (var ingredient in model.Ingredients)
                {
                    productSize.RecipeItems.Add(new RecipeItem
                    {
                        RawMaterialId = ingredient.RawMaterialId,
                        Quantity = ingredient.Quantity,
                        ProductSizeId = productSize.ProductSizeId
                    });
                }

                await _db.SaveChangesAsync();

                // ========== BAGONG CODE: Compute total cost gamit ang LATEST BATCH PRICES ==========
                decimal totalCost = 0;
                foreach (var ingredient in model.Ingredients)
                {
                    // 🔥 FIX: Gamitin ang FIFO cost (pinakamatandang batch na may stock)
                    decimal fifoPrice = await GetFIFOUnitPrice(ingredient.RawMaterialId);
                    totalCost += ingredient.Quantity * fifoPrice;
                }

                // Compute new selling price using the saved margin
                decimal newSellingPrice = Math.Round(totalCost / (1 - productSize.Margin), 2);

                productSize.Price = newSellingPrice;

                await _db.SaveChangesAsync();

                return Json(new { success = true, newPrice = newSellingPrice, message = "Recipe saved successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArchiveProduct(int id)
        {
            var product = await _db.Products.FindAsync(id);
            if (product == null)
                return Json(new { success = false });

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetInventoryAlertsData()
        {
            var today = DateTime.Today;  // ✅ Isang beses lang ideclare

            var allMaterials = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .ToListAsync();

            // ========== LOW STOCK ITEMS ==========
            var lowStockItems = allMaterials
                .Where(r => r.Stock <= r.MinQty)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    Category = r.Category.ToString(),
                    r.Stock,
                    r.MinQty,
                    r.Unit,
                    StockWithUnit = r.StockWithUnit
                })
                .ToList();
            int lowStockCount = lowStockItems.Count;

            // ========== FIXED: EXPIRING SOON (from BATCHES within 7 days ONLY) ==========
            var expiringDateThreshold = today.AddDays(7);

            var expiringBatches = await _db.RawMaterialBatches
                .Include(b => b.RawMaterial)
                .Where(b => b.ExpiryDate.HasValue &&
                            b.ExpiryDate.Value >= today &&
                            b.ExpiryDate.Value <= expiringDateThreshold &&
                            b.QuantityRemaining > 0 &&
                            !b.RawMaterial.IsDeleted)
                .ToListAsync();

            // I-group at i-sum ang quantity ng MGA BATCH LANG NA WITHIN 7 DAYS
            var expiringItems = expiringBatches
                .GroupBy(b => b.RawMaterial)
                .Select(g => new
                {
                    RawMaterialId = g.Key.RawMaterialId,
                    Name = g.Key.Name,
                    Category = g.Key.Category.ToString(),
                    ExpiringStockQuantity = g.Sum(b => b.QuantityRemaining),
                    ExpiringStockWithUnit = $"{g.Sum(b => b.QuantityRemaining):F2} {g.Key.Unit}",
                    EarliestExpiryDate = g.Min(b => b.ExpiryDate).Value.ToString("yyyy-MM-dd"),
                    DaysLeft = (g.Min(b => b.ExpiryDate).Value - today).Days,
                    Unit = g.Key.Unit,
                    BatchCount = g.Count()
                })
                .ToList();
            int expiringCount = expiringItems.Count;

            // ========== FIXED: EXPIRED (from BATCHES) ==========
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
                    ExpiredStockWithUnit = $"{g.Sum(b => b.QuantityRemaining):F2} {g.Key.Unit}",
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
                    w.Quantity,
                    Unit = w.RawMaterial.Unit,
                    Reason = w.Reason.ToString(),
                    TotalCost = w.TotalCost,
                    w.RecordedBy,
                    w.Notes
                })
                .ToListAsync();
            int wasteCount = wasteLogs.Count;

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

                // Check if enough stock
                if (material.Stock < model.Quantity)
                    return Json(new { success = false, message = $"Insufficient stock. Only {material.Stock} {material.Unit} available." });

                // Create waste log
                var wasteLog = new WasteLog
                {
                    RawMaterialId = model.RawMaterialId,
                    Quantity = model.Quantity,
                    Reason = Enum.Parse<WasteReason>(model.Reason),
                    Date = DateTime.Today,
                    RecordedBy = model.RecordedBy ?? "Owner",
                    Notes = model.Notes
                };

                // Reduce stock
                _fifoService.DeductStock(
                 model.RawMaterialId,
                 model.Quantity,
                 0, // no product
                 "WASTE"
                 );

                _db.WasteLogs.Add(wasteLog);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Waste logged successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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

                // Check kung may sapat na raw materials
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;
                    var material = await _db.RawMaterials.FindAsync(recipeItem.RawMaterialId);

                    if (material.Stock < neededQty)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Insufficient {material.Name}. Need {neededQty} {material.Unit}, only {material.Stock} available."
                        });
                    }
                }

                string referenceNo = request.ReferenceNo ?? $"PROD-{DateTime.Now:yyyyMMddHHmmss}";

                // Deduct raw materials gamit ang FIFO
                foreach (var recipeItem in productSize.RecipeItems)
                {
                    decimal neededQty = recipeItem.Quantity * request.Quantity;

                    bool success = _fifoService.DeductStock(
                        recipeItem.RawMaterialId,
                        neededQty,
                        productSize.ProductId,
                        $"{referenceNo}-{productSize.ProductSizeId}"
                    );

                    if (!success)
                    {
                        return Json(new
                        {
                            success = false,
                            message = $"Failed to deduct {recipeItem.RawMaterial?.Name ?? "raw material"}. Insufficient batch stock."
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
                var dailyRevenue = await _db.Orders
                    .Where(o => o.Date.Date == day && o.Status != "Cancelled")
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;
                salesRevenue.Add(dailyRevenue);

                var dailyOrders = await _db.Orders
                    .CountAsync(o => o.Date.Date == day && o.Status != "Cancelled");
                salesOrders.Add(dailyOrders);
            }

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

            // ========== TOP SELLING PRODUCTS ==========
            var topSelling = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Include(oi => oi.ProductSize)
                .Where(oi => oi.Order.Status != "Cancelled")
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
                .OrderByDescending(p => p.UnitsSold)// Revenue
                .Take(5)
                .ToListAsync();

            // ========== WASTE LOGS (FIXED - compute TotalCost in memory) ==========
            var wasteLogsRaw = await _db.WasteLogs
                .Include(w => w.RawMaterial)
                .OrderByDescending(w => w.Date)
                .Select(w => new
                {
                    w.WasteLogId,
                    MaterialName = w.RawMaterial.Name,
                    w.Quantity,
                    Unit = w.RawMaterial.Unit,
                    Reason = w.Reason.ToString(),
                    RawMaterialPrice = w.RawMaterial.Price,
                    Date = w.Date.ToString("yyyy-MM-dd"),
                    w.RecordedBy,
                    w.Notes
                })
                .ToListAsync();

            // Compute TotalCost in memory
            var wasteLogs = wasteLogsRaw.Select(w => new
            {
                w.WasteLogId,
                w.MaterialName,
                w.Quantity,
                w.Unit,
                w.Reason,
                TotalCost = w.Quantity * w.RawMaterialPrice,
                w.Date,
                w.RecordedBy,
                w.Notes
            }).ToList();

            var totalWasteCost = wasteLogs.Sum(w => w.TotalCost);

            // ========== TOTAL REVENUE ==========
            var totalRevenue = await _db.Orders
                .Where(o => o.Status != "Cancelled")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            // ========== REVENUE BY CATEGORY ==========
            var categoryRevenue = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.Status != "Cancelled")
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
                .Include(o => o.Items)  // ← CHANGE: OrderItems -> Items
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)  // ← CHANGE
                    .ThenInclude(oi => oi.ProductSize)
                .Where(o => o.Status == "Completed")
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    orderId = o.OrderId,
                    receiptNumber = o.ReceiptNumber ?? $"ORD-{o.OrderId}",
                    date = o.Date.ToString("yyyy-MM-dd HH:mm:ss"),
                    itemCount = o.Items.Sum(oi => oi.Quantity),  // ← CHANGE
                    items = o.Items.Select(oi => new  // ← CHANGE
                    {
                        name = oi.Product.Name,
                        size = oi.ProductSize.SizeName,
                        quantity = oi.Quantity,
                        price = oi.Price,
                        subtotal = oi.Price * oi.Quantity
                    }).ToList(),
                    subtotal = o.Items.Sum(oi => oi.Price * oi.Quantity),  // ← CHANGE
                    discountType = o.DiscountType ?? "None",
                    discountPercent = o.DiscountPercent,
                    discountAmount = o.DiscountAmount,
                    total = o.TotalAmount,
                    paymentMethod = o.PaymentMethod ?? "Cash"
                })
                .ToListAsync();

            // ========== SET ViewBag VALUES ==========
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.EstimatedProfit = totalRevenue * 0.3m;
            ViewBag.TotalWasteCost = totalWasteCost;

            return Json(new
            {
                salesPerformance = new { labels = salesLabels, revenue = salesRevenue, orders = salesOrders },
                categoryDistribution = categoryDistribution,
                topSellingProducts = topSelling,
                wasteLogs = wasteLogs,
                discountRecords = new List<object>(),
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
                    decimal totalCost = 0;
                    foreach (var ri in productSize.RecipeItems)
                    {
                        decimal activePrice = await GetFIFOUnitPrice(ri.RawMaterialId);
                        totalCost += ri.Quantity * activePrice;
                    }

                    decimal newPrice = Math.Round(totalCost / (1 - productSize.Margin), 2);

                    if (productSize.Price != newPrice)
                    {
                        productSize.Price = newPrice;
                        updatedCount++;
                    }
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"Updated {updatedCount} product prices", updatedCount = updatedCount });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
} 