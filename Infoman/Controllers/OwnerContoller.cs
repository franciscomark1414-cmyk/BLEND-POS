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

        public ActionResult Dashboard()
        {// Get current date
            DateTime today = DateTime.Today;

            // ========== TODAY'S SALES & REVENUE ==========
            var todaysOrders = _db.Orders.Where(o => o.Date.Date == today && o.Status != "Cancelled").ToList();
            ViewBag.TodaysSales = todaysOrders.Count();
            ViewBag.TotalOrdersToday = todaysOrders.Count();
            ViewBag.TotalRevenue = todaysOrders.Sum(o => o.TotalAmount);

            // ========== CRITICAL STOCK ALERT ==========
            // Critical stock = Stock <= MinQty
            int criticalStockCount = _db.RawMaterials
                .Where(r => r.IsDeleted == false && r.Stock <= r.MinQty)
                .Count();
            ViewBag.CriticalStockCount = criticalStockCount;

            // ========== EXPIRING SOON (within 7 days) ==========
            int expiringCount = _db.RawMaterials
                .Where(r => r.IsDeleted == false &&
                            r.ExpirationDate != null &&
                            r.ExpirationDate.Value.Date <= today.AddDays(7) &&
                            r.ExpirationDate.Value.Date > today &&
                            r.Stock > 0)
                .Count();
            ViewBag.ExpiringCount = expiringCount;

            // ========== EXPIRED ==========
            int expiredCount = _db.RawMaterials
                .Where(r => r.IsDeleted == false &&
                            r.ExpirationDate != null &&
                            r.ExpirationDate.Value.Date < today &&
                            r.Stock > 0)
                .Count();
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

                // Group in memory (client-side)
                bestSelling = orderItems
                    .GroupBy(oi => oi.Product?.Name ?? "Unknown")
                    .Select(g => new BestSellingProductVM
                    {
                        Name = g.Key,
                        TotalSold = g.Sum(oi => oi.Quantity),
                        Price = g.FirstOrDefault()?.Price ?? 0
                    })
                    .OrderByDescending(p => p.TotalSold)
                    .Take(5)
                    .Select((p, index) => new BestSellingProductVM
                    {
                        Rank = index + 1,
                        Name = p.Name,
                        TotalSold = p.TotalSold,
                        Price = p.Price
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                // Log error if needed
                bestSelling = new List<BestSellingProductVM>();
            }

            var dashboardViewModel = new DashboardViewModel
            {
                BestSellingProducts = bestSelling
            };


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
        ExpirationDate = r.ExpirationDate,

        // 🔥 IMPORTANT: recompute stock from batches
        Stock = _db.RawMaterialBatches
            .Where(b => b.RawMaterialId == r.RawMaterialId)
            .Sum(b => (decimal?)b.QuantityRemaining) ?? 0
    })
    .ToList();
            var products = _db.Products
        .Include(p => p.Sizes)
        .ThenInclude(ps => ps.RecipeItems)
        .OrderBy(p => p.Name)
        .ToList();
            ViewBag.Products = products;

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
                    TotalStock = p.Sizes.Sum(s => s.Stock),
                    Sizes = p.Sizes.Select(s => new
                    {
                        s.SizeName,
                        s.Price,
                        s.Stock
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

                // Create new product
                var product = new Product
                {
                    Name = model.Name.Trim(),
                    SKU = model.SKU.Trim().ToUpper(),
                    Category = model.Category?.Trim() ?? "",
                    Description = model.Description?.Trim() ?? "",
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

                    var productSize = new ProductSize
                    {
                        SizeName = sizeVM.SizeName.Trim(),
                        Stock = sizeVM.Stock,
                        RecipeItems = new List<RecipeItem>()
                    };

                    // Calculate total cost from recipe items
                    var totalCost = 0m;
                    var validRecipeCount = 0;

                    foreach (var recipeVM in sizeVM.RecipeItems)
                    {
                        if (recipeVM.RawMaterialId > 0 && recipeVM.Quantity > 0)
                        {
                            var material = await _db.RawMaterials
                                .FirstOrDefaultAsync(x => x.RawMaterialId == recipeVM.RawMaterialId);

                            if (material != null)
                            {
                                var cost = recipeVM.Quantity * material.Price;
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

                product.Stock = product.Sizes.Sum(s => s.Stock);

                _db.Products.Add(product);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Product added successfully!", redirect = "/Owner/Dashboard" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving product: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetRecipe(int id)
        {
            var recipe = _db.ProductSizes
                .Where(ps => ps.ProductSizeId == id)
                .Select(ps => new
                {
                    productName = ps.Product.Name,
                    productSKU = ps.Product.SKU,
                    productCategory = ps.Product.Category,
                    sizeName = ps.SizeName,
                    sellingPrice = ps.Price,
                    totalCost = ps.RecipeItems.Sum(r => r.Quantity * r.RawMaterial.Price),
                    margin = ps.Margin,
                    stock = ps.Stock,
                    profit = ps.Price - ps.RecipeItems.Sum(r => r.Quantity * r.RawMaterial.Price),
                    hasStock = ps.Stock > 0,  // ✅ Flag para sa warning
                    ingredients = ps.RecipeItems.Select(r => new
                    {
                        rawMaterialId = r.RawMaterialId,
                        quantity = r.Quantity,
                        name = r.RawMaterial.Name,
                        unit = r.RawMaterial.Unit,
                        price = r.RawMaterial.Price,
                        total = r.Quantity * r.RawMaterial.Price
                    }).ToList()
                })
                .FirstOrDefault();

            if (recipe == null)
                return Json(new { error = "Recipe not found" });

            // ✅ PARA SA VIEW: I-return pa rin ang data, may warning lang
            // PARA SA EDIT: Iba ang tawag (kailangan ng separate action or parameter)

            return Json(recipe);
        }

        [HttpGet]
        public IActionResult CanEditRecipe(int id)
        {
            var productSize = _db.ProductSizes
                .FirstOrDefault(ps => ps.ProductSizeId == id);

            if (productSize == null)
                return Json(new { canEdit = false, message = "Product not found" });

            if (productSize.Stock > 0)
            {
                return Json(new
                {
                    canEdit = false,
                    message = $"⚠️ Cannot edit recipe!\n\nThis product has {productSize.Stock} pcs in stock.\n\nPlease sell or waste remaining stock before editing the recipe.",
                    stock = productSize.Stock
                });
            }

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
                if (productSize.Stock > 0)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Cannot save recipe changes. This product has {productSize.Stock} pcs in stock. Please sell or waste remaining stock first.",
                        hasStock = true,
                        stock = productSize.Stock
                    });
                }

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

                // I-recompute ang total cost
                await _db.Entry(productSize).Collection(ps => ps.RecipeItems).LoadAsync();
                foreach (var item in productSize.RecipeItems)
                {
                    await _db.Entry(item).Reference(r => r.RawMaterial).LoadAsync();
                }

                // Compute total cost
                decimal totalCost = productSize.RecipeItems.Sum(r => r.Quantity * r.RawMaterial.Price);

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
            var allMaterials = await _db.RawMaterials
                .Where(r => !r.IsDeleted)
                .ToListAsync();

            // Low Stock Items
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

            // Expiring Soon (within next 7 days, future only)
            var expiringItems = allMaterials
                .Where(r => r.ExpirationDate.HasValue &&
                            r.ExpirationDate.Value >= DateTime.Today &&
                            r.ExpirationDate.Value <= DateTime.Today.AddDays(7))
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    Category = r.Category.ToString(),
                    StockWithUnit = r.StockWithUnit,
                    ExpiryDate = r.ExpirationDate.Value.ToString("yyyy-MM-dd"),
                    DaysLeft = (r.ExpirationDate.Value - DateTime.Today).Days,
                    r.Unit
                })
                .ToList();

            // Expired (past expiry date)
            var expiredItems = allMaterials
                .Where(r => r.ExpirationDate.HasValue &&
                            r.ExpirationDate.Value < DateTime.Today)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    Category = r.Category.ToString(),
                    StockWithUnit = r.StockWithUnit,
                    ExpiryDate = r.ExpirationDate.Value.ToString("yyyy-MM-dd"),
                    DaysOverdue = (DateTime.Today - r.ExpirationDate.Value).Days,
                    r.Unit
                })
                .ToList();

            // Waste Logs
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

            return Json(new
            {
                lowStockCount = lowStockItems.Count,
                expiringCount = expiringItems.Count,
                expiredCount = expiredItems.Count,
                wasteCount = wasteLogs.Count,
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

                // Add sa product stock
                productSize.Stock += request.Quantity;
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"Successfully produced {request.Quantity} pcs of {productSize.Product.Name} - {productSize.SizeName}",
                    newStock = productSize.Stock,
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
    }
}