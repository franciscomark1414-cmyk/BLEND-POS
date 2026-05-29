using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Linq;
using System.Threading.Tasks;
using static Infoman.Models.WasteLog;

namespace Infoman.Controllers
{
    public class RawMaterialController : Controller
    {
        private readonly ConnectionDatabase _context;
        private readonly FIFOService _fifoService;

        public RawMaterialController(ConnectionDatabase context)
        {
            _context = context;
            _fifoService = new FIFOService(_context);

        }

        // READ - List all raw materials (excluding deleted)
        public async Task<IActionResult> Index()
        {
            var materials = await _context.RawMaterials
                .Where(r => !r.IsDeleted)
                .ToListAsync();
            return View(materials);
        }

        // CREATE - GET
        public IActionResult Create()
        {
            return View();
        }
        private string GenerateRawMaterialSKU(string name)
        {
            string prefix = "RM";
            string code = name.Length >= 3 ? name.Substring(0, 3).ToUpper() : name.ToUpper();
            return $"{prefix}-{code}";
        }

        // CREATE - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RawMaterial material)
        {
            System.Diagnostics.Debug.WriteLine($"=== CREATE RAW MATERIAL ===");
            System.Diagnostics.Debug.WriteLine($"MinQty from form: {material.MinQty}");
            System.Diagnostics.Debug.WriteLine($"Unit: {material.Unit}");

            ModelState.Remove("ExpirationDate");

            if (material.MinQty < 0)
            {
                return Json(new { success = false, message = "Minimum quantity cannot be negative. Please enter 0 or a positive number." });
            }

            if (ModelState.IsValid)
            {
                SetConversionFields(material);

                // ✅ SAVE the original display unit value in MinQty
                decimal displayMinQty = material.MinQty;  // User input (e.g., 10 kg)

                // ✅ Convert to base unit for MinQtyInBaseUnit
                decimal minQtyInBaseUnit = ConvertToBaseUnit(displayMinQty, material.Unit, material.BaseUnit);

                // ✅ STORE: MinQty keeps display unit, MinQtyInBaseUnit stores base unit
                material.MinQty = displayMinQty;              // ✅ Keep as display unit (10)
                material.MinQtyInBaseUnit = minQtyInBaseUnit; // ✅ Store base unit (10000)

                System.Diagnostics.Debug.WriteLine($"Display MinQty: {material.MinQty} {material.Unit}");
                System.Diagnostics.Debug.WriteLine($"Base MinQty: {material.MinQtyInBaseUnit} {material.BaseUnit}");

                if (material.ExpirationDate == DateTime.MinValue)
                    material.ExpirationDate = null;

                if (string.IsNullOrEmpty(material.SKU))
                    material.SKU = GenerateRawMaterialSKU(material.Name);

                material.IsDeleted = false;
                material.Stock = 0;

                _context.Add(material);
                await _context.SaveChangesAsync();
                return Json(new { success = true, material });
            }

            return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
        }

        // ✅ HELPER METHOD PARA SA CONVERSION
        private void SetConversionFields(RawMaterial material)
        {
            switch (material.Unit)
            {
                case "kg":
                    material.BaseUnit = "g";
                    material.ConversionFactor = 1000;
                    break;
                case "g":
                    material.BaseUnit = "g";
                    material.ConversionFactor = 1;
                    break;
                case "L":
                    material.BaseUnit = "mL";
                    material.ConversionFactor = 1000;
                    break;
                case "mL":
                    material.BaseUnit = "mL";
                    material.ConversionFactor = 1;
                    break;
                case "pcs":
                    material.BaseUnit = "pcs";
                    material.ConversionFactor = 1;
                    break;
                default:
                    material.BaseUnit = material.Unit;
                    material.ConversionFactor = 1;
                    break;
            }
        }
        // EDIT - GET
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var material = await _context.RawMaterials
                .Where(r => !r.IsDeleted)
                .FirstOrDefaultAsync(m => m.RawMaterialId == id);

            if (material == null) return NotFound();

            return View(material);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, RawMaterial model)
        {
            var rawMaterial = await _context.RawMaterials.FindAsync(id);
            if (rawMaterial == null)
                return Json(new { success = false, message = "Raw material not found" });

            if (model.MinQty < 0)
            {
                return Json(new { success = false, message = "Minimum quantity cannot be negative. Please enter 0 or a positive number." });
            }

            rawMaterial.Name = model.Name;
            rawMaterial.Category = model.Category;
            rawMaterial.MinQty = model.MinQty;
            rawMaterial.Unit = model.Unit;
            rawMaterial.Price = model.Price;
            rawMaterial.ExpirationDate = model.ExpirationDate;

            SetConversionFields(rawMaterial);

            // ✅ I-CONVERT ANG MINQTY SA BASE UNIT
            rawMaterial.MinQtyInBaseUnit = ConvertToBaseUnit(model.MinQty, model.Unit, rawMaterial.BaseUnit);

            var totalFromBatches = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == id)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            rawMaterial.Stock = totalFromBatches;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // Helper method para mag-update ng product prices
        private async Task UpdateAllProductPricesAffectedByRawMaterial(int rawMaterialId)
        {
            // Hanapin lahat ng RecipeItems na gumagamit ng raw material na ito
            var affectedRecipeItems = await _context.RecipeItems
                .Include(r => r.ProductSize)
                .Where(r => r.RawMaterialId == rawMaterialId)
                .ToListAsync();

            // Kunin ang unique ProductSize IDs
            var affectedProductSizeIds = affectedRecipeItems
                .Select(r => r.ProductSizeId)
                .Distinct()
                .ToList();

            foreach (var psId in affectedProductSizeIds)
            {
                // ✅ I-correct ang spelling: ProductSizes, hindi ProductsSizes
                // ✅ I-correct ang spelling: RecipeItems, hindi Recipients
                var productSize = await _context.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == psId);

                if (productSize != null)
                {
                    // ✅ Tamang formula: Quantity * Price ng bawat raw material
                    decimal totalCost = productSize.RecipeItems.Sum(ri => ri.Quantity * ri.RawMaterial.Price);

                    // ✅ Kunin ang margin na naka-store sa product size
                    decimal margin = productSize.Margin; // Default 0.3 kung wala pa

                    // ✅ Tamang formula: totalCost / (1 - margin)
                    decimal newPrice = Math.Round(totalCost / (1 - margin), 2);

                    productSize.Price = newPrice;
                }
            }

            await _context.SaveChangesAsync();
        }
        // DELETE - GET
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var material = await _context.RawMaterials
                .Where(r => !r.IsDeleted)
                .FirstOrDefaultAsync(m => m.RawMaterialId == id);

            if (material == null) return NotFound();

            return View(material);
        }

        // DELETE - POST (Soft Delete)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var material = await _context.RawMaterials.FindAsync(id);
                if (material == null)
                {
                    return Json(new { success = false, message = "Material not found" });
                }

                if (material.IsDeleted)
                {
                    return Json(new { success = false, message = "Material is already deleted" });
                }

                // BAGONG CODE (may filter sa IsDeleted)
                var recipeItems = await _context.RecipeItems
                    .Include(ri => ri.ProductSize)
                        .ThenInclude(ps => ps.Product)
                    .Where(ri => ri.RawMaterialId == id
                        && !ri.IsDeleted  // ✅ I-ignore ang mga deleted recipe items
                        && ri.ProductSize != null
                        && !ri.ProductSize.IsDeleted  // ✅ I-ignore ang mga deleted product sizes
                        && ri.ProductSize.Product != null
                        && !ri.ProductSize.Product.IsDeleted)  // ✅ I-ignore ang mga deleted products
                    .ToListAsync();

                if (recipeItems.Any())
                {
                    var today = DateTime.Today;
                    var affectedProducts = new List<object>();

                    foreach (var ri in recipeItems)
                    {
                        // ✅ COMPUTE ACTUAL AVAILABLE STOCK FROM RAW MATERIALS
                        int maxOrders = int.MaxValue;

                        if (ri.ProductSize.RecipeItems != null && ri.ProductSize.RecipeItems.Any())
                        {
                            foreach (var recipe in ri.ProductSize.RecipeItems)
                            {
                                var availableQty = await _context.RawMaterialBatches
                                    .Where(b => b.RawMaterialId == recipe.RawMaterialId
                                        && b.QuantityRemaining > 0
                                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                                    .SumAsync(b => b.QuantityRemaining);

                                int possibleOrders = (int)(availableQty / recipe.Quantity);
                                if (possibleOrders < maxOrders)
                                    maxOrders = possibleOrders;
                            }
                        }

                        int availableStock = (maxOrders == int.MaxValue || maxOrders < 0) ? 0 : maxOrders;

                        affectedProducts.Add(new
                        {
                            ProductName = ri.ProductSize.Product.Name,
                            SizeName = ri.ProductSize.SizeName,
                            Stock = availableStock,  // ← COMPUTED STOCK (galing sa raw materials)
                            ProductSizeId = ri.ProductSize.ProductSizeId
                        });
                    }

                    // Remove duplicates (same product + size combination)
                    affectedProducts = affectedProducts
                        .GroupBy(p => new { ((dynamic)p).ProductName, ((dynamic)p).SizeName })
                        .Select(g => g.First())
                        .ToList();

                    // I-format ang message
                    var productList = string.Join("\n", affectedProducts.Select(p => $"  • {((dynamic)p).ProductName} - {((dynamic)p).SizeName} (Can make: {((dynamic)p).Stock} orders)"));

                    return Json(new
                    {
                        success = false,
                        message = $"❌ Cannot delete '{material.Name}'!\n\nThis raw material is used in the following products:\n{productList}\n\nPlease remove this ingredient from recipes first before deleting.",
                        affectedProducts = affectedProducts,
                        hasAffectedProducts = true
                    });
                }

                // Kung hindi ginagamit, safe i-delete
                material.IsDeleted = true;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Material deleted successfully" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while deleting: " + ex.Message });
            }
        }
        // RESTORE - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            var material = await _context.RawMaterials.FindAsync(id);
            if (material != null && material.IsDeleted)
            {
                material.IsDeleted = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Material restored successfully" });
            }
            return Json(new { success = false, message = "Material not found or already active" });
        }

        [HttpGet]
        public IActionResult GetPrice(int id)
        {
            var material = _context.RawMaterials
                .Where(r => !r.IsDeleted)
                .FirstOrDefault(r => r.RawMaterialId == id);

            if (material == null)
                return Json(new { price = 0 });

            return Json(new { price = material.Price });
        }
        [HttpPost]
        public async Task<IActionResult> DeductStockForProduction(int rawMaterialId, decimal quantity, string unitUsed, int productId, string referenceNo)
        {
            try
            {
                bool success = _fifoService.DeductStock(rawMaterialId, quantity, unitUsed, productId, referenceNo);

                if (success)
                {
                    return Json(new { success = true, message = "Stock deducted successfully" });
                }
                else
                {
                    return Json(new { success = false, message = "Insufficient stock" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> BatchHistory(int rawMaterialId)
        {
            var rawMaterial = await _context.RawMaterials.FindAsync(rawMaterialId);
            if (rawMaterial == null)
                return Json(new { error = "Material not found" });

            var batches = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .OrderBy(b => b.ReceivedDate)
                .Select(b => new
                {
                    b.BatchId,
                    b.ReceivedDate,
                    QuantityReceived = b.QuantityReceived,
                    QuantityRemaining = b.QuantityRemaining,
                    b.ExpiryDate,
                    b.UnitPrice,
                    Usage = b.ProductionUsages.Select(u => new
                    {
                        u.QuantityUsed,
                        u.DateUsed,
                        u.ReferenceNo,
                        OrderNumber = u.Order != null ? $"#{u.Order.OrderId}" :
                                    (u.ReferenceNo != null && u.ReferenceNo.StartsWith("POS-") ? u.ReferenceNo : null),
                        OrderId = u.OrderId,
                        ProductName = u.ProductSize != null ? $"{u.ProductSize.Product.Name} - {u.ProductSize.SizeName}" : null
                    })
                })
                .ToListAsync();

            // ✅ I-CONVERT ang mga quantity para sa display
            decimal conversionFactor = 1;
            if (rawMaterial.BaseUnit == "g" && rawMaterial.Unit == "kg")
            {
                conversionFactor = 1000;  // 100,000 g → 100 kg
            }
            else if (rawMaterial.BaseUnit == "mL" && rawMaterial.Unit == "L")
            {
                conversionFactor = 1000;  // 100,000 mL → 100 L
            }

            // I-convert ang bawat batch
            var convertedBatches = batches.Select(b => new
            {
                b.BatchId,
                b.ReceivedDate,
                QuantityReceived = b.QuantityReceived / conversionFactor,
                QuantityRemaining = b.QuantityRemaining / conversionFactor,
                QuantityRemainingInBaseUnit = b.QuantityRemaining,
                b.ExpiryDate,
                b.UnitPrice,
                b.Usage
            }).ToList();

            return Json(new { batches = convertedBatches, unit = rawMaterial.Unit });
        }

        // BAGONG ACTION: Magdagdag ng bagong delivery
        // Add this class inside RawMaterialController.cs
        public class AddDeliveryRequest
        {
            public int RawMaterialId { get; set; }
            public decimal Quantity { get; set; }
            public string Unit { get; set; }
            public decimal UnitPrice { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

            [HttpPost]
            public async Task<JsonResult> AddDelivery([FromBody] AddDeliveryRequest request)
            {
                var rawMaterial = await _context.RawMaterials.FindAsync(request.RawMaterialId);
                if (rawMaterial == null)
                    return Json(new { success = false, message = "Raw material not found" });

                // ✅ CHECK IF EXPIRY DATE IS EXPIRED
                bool isExpired = false;
                string warningMessage = null;

                if (request.ExpiryDate.HasValue)
                {
                    if (request.ExpiryDate.Value.Date < DateTime.Today)
                    {
                        isExpired = true;
                        warningMessage = $"WARNING: The expiry date ({request.ExpiryDate.Value:yyyy-MM-dd}) is already EXPIRED!";
                    }
                    else if (request.ExpiryDate.Value.Date <= DateTime.Today.AddDays(7))
                    {
                        warningMessage = $"NOTE: The expiry date ({request.ExpiryDate.Value:yyyy-MM-dd}) is within 7 days. This material will expire soon.";
                    }
                }

                // ✅ I-DEBUG ITO (ilagay sa simula)
                System.Diagnostics.Debug.WriteLine($"=== ADD DELIVERY DEBUG ===");
                System.Diagnostics.Debug.WriteLine($"RawMaterial: {rawMaterial?.Name}");
                System.Diagnostics.Debug.WriteLine($"RawMaterial.Unit: {rawMaterial?.Unit}");
                System.Diagnostics.Debug.WriteLine($"RawMaterial.BaseUnit: {rawMaterial?.BaseUnit}");
                System.Diagnostics.Debug.WriteLine($"Request.Quantity: {request.Quantity}");
                System.Diagnostics.Debug.WriteLine($"Request.Unit: {request.Unit}");
                System.Diagnostics.Debug.WriteLine($"ExpiryDate: {request.ExpiryDate}");
                System.Diagnostics.Debug.WriteLine($"IsExpired: {isExpired}");
                System.Diagnostics.Debug.WriteLine($"WarningMessage: {warningMessage}");

                decimal quantityInBaseUnit = request.Quantity;

                // Conversion
                if (rawMaterial.BaseUnit == "g" && request.Unit == "kg")
                {
                    quantityInBaseUnit = request.Quantity * 1000;
                    System.Diagnostics.Debug.WriteLine($"Converted: {request.Quantity} kg → {quantityInBaseUnit} g");
                }
                else if (rawMaterial.BaseUnit == "mL" && request.Unit == "L")
                {
                    quantityInBaseUnit = request.Quantity * 1000;
                    System.Diagnostics.Debug.WriteLine($"Converted: {request.Quantity} L → {quantityInBaseUnit} mL");
                }

                var batch = new RawMaterialBatch
                {
                    RawMaterialId = request.RawMaterialId,
                    QuantityReceived = quantityInBaseUnit,
                    QuantityRemaining = quantityInBaseUnit,
                    ReceivedDate = DateTime.Now,
                    ExpiryDate = request.ExpiryDate,
                    UnitPrice = request.UnitPrice
                };

                _context.RawMaterialBatches.Add(batch);
                await _context.SaveChangesAsync();

                // ✅ IF EXPIRED, AUTO-CREATE WASTE LOG
                // ✅ IF EXPIRED, AUTO-CREATE WASTE LOG
                if (isExpired)
                {
                    // Compute total cost first
                    decimal totalCost = request.Quantity * request.UnitPrice;

                    var wasteLog = new WasteLog
                    {
                        RawMaterialId = request.RawMaterialId,
                        Quantity = quantityInBaseUnit,
                        Reason = WasteReason.Expired,
                        Date = DateTime.Now,
                        RecordedBy = "System",
                        Notes = $"Auto-generated from expired batch received on {DateTime.Now:yyyy-MM-dd HH:mm}",
                        TotalCost = totalCost,
                        ExpiryDate = request.ExpiryDate,
                        BatchId = batch.BatchId
                    };
                    _context.WasteLogs.Add(wasteLog);

                    // Set remaining to 0 since it's expired
                    batch.QuantityRemaining = 0;

                    await _context.SaveChangesAsync();
                }

                // I-update ang main Stock column
                await UpdateMainStockColumn(request.RawMaterialId);
                await UpdateProductPricesForRawMaterial(request.RawMaterialId);

                string successMessage = isExpired
                    ? $"Delivery added but marked as EXPIRED and logged as waste. {warningMessage}"
                    : (warningMessage != null ? $"Delivery added successfully. {warningMessage}" : "Delivery added successfully!");

                return Json(new { success = true, message = successMessage, isExpired = isExpired, warning = warningMessage });
            }

        private async Task UpdateMainStockColumn(int rawMaterialId)
        {
            var totalStockInBaseUnit = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var material = await _context.RawMaterials.FindAsync(rawMaterialId);
            if (material != null)
            {
                material.Stock = totalStockInBaseUnit;  // ✅ I-save sa base unit (grams/mL)
                await _context.SaveChangesAsync();
            }
        }

        // Helper method para mag-update ng product prices
        private async Task UpdateProductPricesForRawMaterial(int rawMaterialId)
        {
            // Hanapin ang lahat ng recipe items na gumagamit ng raw material na ito
            var affectedRecipeItems = await _context.RecipeItems
                .Include(ri => ri.ProductSize)
                    .ThenInclude(ps => ps.RecipeItems)
                .Where(ri => ri.RawMaterialId == rawMaterialId)
                .ToListAsync();

            var affectedProductSizeIds = affectedRecipeItems
                .Select(ri => ri.ProductSizeId)
                .Distinct()
                .ToList();

            foreach (var psId in affectedProductSizeIds)
            {
                var productSize = await _context.ProductSizes
                    .Include(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(ps => ps.ProductSizeId == psId);

                if (productSize != null)
                {
                    // Compute total cost gamit ang ACTIVE BATCH PRICE (FEFO)
                    decimal totalCost = 0;
                    foreach (var ri in productSize.RecipeItems)
                    {
                        decimal activePrice = await GetActiveBatchPrice(ri.RawMaterialId);
                        totalCost += ri.Quantity * activePrice;
                    }

                    // Compute new selling price gamit ang existing margin
                    decimal newPrice = Math.Round(totalCost / (1 - productSize.Margin), 2);
                    productSize.Price = newPrice;
                }
            }

            await _context.SaveChangesAsync();
        }

        // Helper para kumuha ng active batch price
        private async Task<decimal> GetActiveBatchPrice(int rawMaterialId)
        {
            var activeBatch = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId
                    && b.QuantityRemaining > 0
                    && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)
                .ThenBy(b => b.ReceivedDate)
                .FirstOrDefaultAsync();

            if (activeBatch != null)
                return activeBatch.UnitPrice;

            var material = await _context.RawMaterials.FindAsync(rawMaterialId);
            return material?.Price ?? 0;
        }
        [HttpGet]
        public async Task<IActionResult> GetProductsUsingRawMaterial(int id)
        {
            var recipeItems = await _context.RecipeItems
                .Include(ri => ri.ProductSize)
                    .ThenInclude(ps => ps.Product)
                .Where(ri => ri.RawMaterialId == id)
                .ToListAsync();

            var products = new List<object>();

            foreach (var ri in recipeItems)
            {
                var productSize = ri.ProductSize;

                products.Add(new
                {
                    productId = productSize.Product.ProductId,
                    productName = productSize.Product.Name,
                    sizeName = productSize.SizeName,
                    recipeQuantity = ri.Quantity,
                    price = productSize.Price
                });
            }

            // Remove duplicates
            var uniqueProducts = products
                .GroupBy(p => new {
                    productId = ((dynamic)p).productId,
                    sizeName = ((dynamic)p).sizeName
                })
                .Select(g => g.First())
                .ToList();

            return Json(uniqueProducts);
        }

        [HttpGet]
        public async Task<IActionResult> GetMaterialDetails(int id)
        {
            var material = await _context.RawMaterials
                .Where(r => !r.IsDeleted && r.RawMaterialId == id)
                .Select(r => new
                {
                    r.RawMaterialId,
                    r.Name,
                    r.Stock,
                    r.Unit,
                    r.Price,
                    r.MinQty,
                    r.Category,
                    r.BaseUnit,
                    r.ConversionFactor
                })
                .FirstOrDefaultAsync();

            if (material == null)
                return Json(new { success = false, message = "Material not found" });

            // ✅ CONVERT STOCK FROM BASE UNIT TO DISPLAY UNIT
            decimal displayStock = material.Stock;
            if (material.BaseUnit == "g" && material.Unit == "kg")
            {
                displayStock = material.Stock / 1000;  // 20000 g → 20 kg
            }
            else if (material.BaseUnit == "mL" && material.Unit == "L")
            {
                displayStock = material.Stock / 1000;  // 20000 mL → 20 L
            }

            return Json(new
            {
                success = true,
                name = material.Name,
                stock = displayStock,        // ✅ CONVERTED NA
                unit = material.Unit,
                price = material.Price
            });
        }

        [HttpGet]
        public async Task<IActionResult> DebugBatches()
        {
            var batches = await _context.RawMaterialBatches
                .Select(b => new
                {
                    b.BatchId,
                    b.RawMaterialId,
                    b.UnitPrice,
                    b.QuantityRemaining,
                    b.ReceivedDate
                })
                .ToListAsync();

            return Json(batches);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllWithLatestPrice()
        {
            var materials = await _context.RawMaterials
                .Where(r => !r.IsDeleted)
                .Select(r => new
                {
                    id = r.RawMaterialId,
                    name = r.Name,
                    unit = r.Unit,
                    defaultPrice = r.Price,
                    // ✅ KUHAIN ANG ACTUAL STOCK MULA SA BATCHES, HINDI SA r.Stock
                    totalStockInBaseUnit = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId)
                        .Sum(b => (decimal?)b.QuantityRemaining) ?? 0,
                    latestBatchPrice = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId && b.QuantityRemaining > 0)
                        .OrderBy(b => b.ExpiryDate)
                        .ThenBy(b => b.ReceivedDate)
                        .Select(b => b.UnitPrice)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // Convert after getting sum from batches
            var result = materials.Select(m => new
            {
                id = m.id,
                name = m.name,
                unit = m.unit,
                defaultPrice = m.defaultPrice,
                stockInDisplayUnit = (m.unit == "kg" || m.unit == "L")
                    ? m.totalStockInBaseUnit / 1000
                    : m.totalStockInBaseUnit,
                latestBatchPrice = m.latestBatchPrice
            }).ToList();

            return Json(result);
        }

        // GET: RawMaterial/GetAdjustmentHistory
        [HttpGet]
        public async Task<IActionResult> GetAdjustmentHistory(string date = null)
        {
            var query = _context.StockAdjustmentLogs
                .Include(l => l.Batch)
                    .ThenInclude(b => b.RawMaterial)
                .Include(l => l.AdjustedByUser)
                .OrderByDescending(l => l.AdjustedAt)
                .AsQueryable();

            if (!string.IsNullOrEmpty(date))
            {
                var filterDate = DateTime.Parse(date);
                query = query.Where(l => l.AdjustedAt.Date == filterDate.Date);
            }

            var adjustments = await query.Select(l => new
            {
                l.AdjustmentId,
                l.BatchId,
                MaterialName = l.Batch.RawMaterial.Name,
                l.OldQuantity,
                l.NewQuantity,
                Unit = l.Batch.RawMaterial.Unit,
                l.Reason,
                l.Notes,
                AdjustedByName = l.AdjustedByUser != null ? l.AdjustedByUser.Username : "System",
                l.AdjustedAt
            }).ToListAsync();

            return Json(new { success = true, adjustments = adjustments });
        }

        // POST: RawMaterial/AdjustBatchStock
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustBatchStock([FromBody] AdjustBatchRequest request)
        {
            try
            {
                // ✅ CHECK KUNG OWNER LANG
                var ownerRole = HttpContext.Session.GetString("Owner_UserRole");
                var ownerId = HttpContext.Session.GetInt32("Owner_UserId");

                if (ownerRole != "Owner" || !ownerId.HasValue || ownerId.Value == 0)
                {
                    return Json(new { success = false, message = "Only Owner can adjust batch stock" });
                }

                var batch = await _context.RawMaterialBatches
                    .Include(b => b.RawMaterial)
                    .FirstOrDefaultAsync(b => b.BatchId == request.BatchId);

                if (batch == null)
                    return Json(new { success = false, message = "Batch not found" });

                decimal oldQuantityInBaseUnit = batch.QuantityRemaining;

                // ✅ CONVERT input from DISPLAY UNIT to BASE UNIT
                decimal newQuantityInBaseUnit = request.NewQuantity;

                if (batch.RawMaterial.Unit == "kg" && batch.RawMaterial.BaseUnit == "g")
                {
                    newQuantityInBaseUnit = request.NewQuantity * 1000;  // 40 kg → 40000 g
                }
                else if (batch.RawMaterial.Unit == "L" && batch.RawMaterial.BaseUnit == "mL")
                {
                    newQuantityInBaseUnit = request.NewQuantity * 1000;  // 40 L → 40000 mL
                }
                // ✅ Kung grams/mL/pcs, no conversion needed

                decimal differenceInBaseUnit = oldQuantityInBaseUnit - newQuantityInBaseUnit;

                System.Diagnostics.Debug.WriteLine($"=== STOCK ADJUSTMENT CONVERSION ===");
                System.Diagnostics.Debug.WriteLine($"Input: {request.NewQuantity} {batch.RawMaterial.Unit}");
                System.Diagnostics.Debug.WriteLine($"Old (base unit): {oldQuantityInBaseUnit} {batch.RawMaterial.BaseUnit}");
                System.Diagnostics.Debug.WriteLine($"New (base unit): {newQuantityInBaseUnit} {batch.RawMaterial.BaseUnit}");
                System.Diagnostics.Debug.WriteLine($"Difference: {differenceInBaseUnit} {batch.RawMaterial.BaseUnit}");
                System.Diagnostics.Debug.WriteLine($"Batch UnitPrice: {batch.UnitPrice} per {batch.RawMaterial.Unit}");

                // ✅ CREATE ADJUSTMENT LOG
                var adjustmentLog = new StockAdjustmentLog
                {
                    BatchId = request.BatchId,
                    OldQuantity = oldQuantityInBaseUnit,
                    NewQuantity = newQuantityInBaseUnit,
                    Reason = request.Reason,
                    AdjustedBy = ownerId.Value,
                    AdjustedAt = DateTime.Now,
                    Notes = request.Notes ?? ""
                };
                _context.StockAdjustmentLogs.Add(adjustmentLog);

                // ✅ KUNG NABAWASAN (may waste)
                if (differenceInBaseUnit > 0)
                {
                    bool shouldCreateWasteLog = !request.Reason.Contains("Typo") &&
                                                !request.Reason.Contains("Correction") &&
                                                !request.Reason.Contains("Excess");

                    if (shouldCreateWasteLog)
                    {
                        // ✅ CONVERT wasted quantity to DISPLAY UNIT for costing
                        decimal wastedQtyInDisplayUnit = differenceInBaseUnit;
                        if (batch.RawMaterial.Unit == "kg" && batch.RawMaterial.BaseUnit == "g")
                        {
                            wastedQtyInDisplayUnit = differenceInBaseUnit / 1000;  // grams → kg
                        }
                        else if (batch.RawMaterial.Unit == "L" && batch.RawMaterial.BaseUnit == "mL")
                        {
                            wastedQtyInDisplayUnit = differenceInBaseUnit / 1000;  // mL → L
                        }

                        // ✅ COMPUTE TOTAL COST using batch unit price
                        decimal totalCost = wastedQtyInDisplayUnit * batch.UnitPrice;

                        System.Diagnostics.Debug.WriteLine($"Wasted (display): {wastedQtyInDisplayUnit} {batch.RawMaterial.Unit}");
                        System.Diagnostics.Debug.WriteLine($"Total Cost: {totalCost}");

                        var wasteLog = new WasteLog
                        {
                            RawMaterialId = batch.RawMaterialId,
                            Quantity = differenceInBaseUnit,  // ✅ STORE IN BASE UNIT (grams/mL)
                            Reason = GetWasteReasonFromAdjustment(request.Reason),
                            Date = DateTime.Now,  // ✅ Use DateTime.Now, not Date.Today
                            RecordedBy = "Owner",
                            Notes = $"Manual adjustment from batch #{batch.BatchId}: {request.Reason}",
                            TotalCost = totalCost,  // ✅ MAY VALUE NA!
                            BatchId = batch.BatchId,
                            ExpiryDate = batch.ExpiryDate
                        };
                        _context.WasteLogs.Add(wasteLog);
                    }
                }

                // ✅ UPDATE BATCH
                batch.QuantityRemaining = newQuantityInBaseUnit;

                // ✅ UPDATE MAIN STOCK
                await UpdateMainStockColumn(batch.RawMaterialId);
                await _context.SaveChangesAsync();

                // ✅ CONVERT PARA SA MESSAGE
                decimal displayDifference = differenceInBaseUnit;
                if (batch.RawMaterial.Unit == "kg" && batch.RawMaterial.BaseUnit == "g")
                {
                    displayDifference = differenceInBaseUnit / 1000;
                }
                else if (batch.RawMaterial.Unit == "L" && batch.RawMaterial.BaseUnit == "mL")
                {
                    displayDifference = differenceInBaseUnit / 1000;
                }

                string message = (differenceInBaseUnit > 0 && !request.Reason.Contains("Typo")) ?
                    $"Stock adjusted. {displayDifference:F2} {batch.RawMaterial.Unit} logged as waste." :
                    "Stock adjusted successfully.";

                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper method to map reason to WasteReason enum
        private WasteLog.WasteReason GetWasteReasonFromAdjustment(string reason)
        {
            if (reason.Contains("Spoilage")) return WasteLog.WasteReason.Other;
            if (reason.Contains("Damage")) return WasteLog.WasteReason.Damaged;
            if (reason.Contains("Quality")) return WasteLog.WasteReason.QualityIssue;
            if (reason.Contains("Expired")) return WasteLog.WasteReason.Expired;
            if (reason.Contains("Missing") || reason.Contains("Theft")) return WasteLog.WasteReason.Other;
            return WasteLog.WasteReason.Other;
        }

        private int GetCurrentUserId()
        {
            return HttpContext.Session.GetInt32("UserId") ?? 0;
        }

        // Request Model
        public class AdjustBatchRequest
        {
            public int BatchId { get; set; }
            public decimal NewQuantity { get; set; }
            public string Reason { get; set; }
            public string Notes { get; set; }
        }

        private decimal ConvertToBaseUnit(decimal quantity, string fromUnit, string toBaseUnit)
        {
            if (fromUnit == toBaseUnit) return quantity;

            return (fromUnit, toBaseUnit) switch
            {
                ("kg", "g") => quantity * 1000,
                ("g", "kg") => quantity / 1000,
                ("L", "mL") => quantity * 1000,
                ("mL", "L") => quantity / 1000,
                _ => quantity
            };
        }

    }
} 