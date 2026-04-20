using Infoman.Data;
using Infoman.Models;
using Infoman.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

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

        // CREATE - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RawMaterial material)
        {
            // Remove validation errors for ExpirationDate if it's empty
            ModelState.Remove("ExpirationDate");

            if (ModelState.IsValid)
            {
                // Handle empty expiration date
                if (material.ExpirationDate == DateTime.MinValue)
                {
                    material.ExpirationDate = null;
                }

                material.IsDeleted = false;
                _context.Add(material);
                await _context.SaveChangesAsync();
                return Json(new { success = true, material });
            }

            return Json(new { success = false, errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage) });
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
            try
            {
                var rawMaterial = await _context.RawMaterials.FindAsync(id);
                if (rawMaterial == null)
                    return Json(new { success = false, message = "Raw material not found" });

                // Check kung nagbago ang presyo
                bool priceChanged = rawMaterial.Price != model.Price;

                // I-update ang raw material (EXCEPT STOCK)
                rawMaterial.Name = model.Name;
                rawMaterial.Category = model.Category;
                // HUWAG GAMITIN ANG model.Stock - ITO ANG PROBLEMA!
                // rawMaterial.Stock = model.Stock;  // ← COMMENT OUT
                rawMaterial.MinQty = model.MinQty;
                rawMaterial.Unit = model.Unit;
                rawMaterial.Price = model.Price;
                rawMaterial.ExpirationDate = model.ExpirationDate;

                // ✅ FORCE RECOMPUTE ng stock mula sa batches
                var totalFromBatches = await _context.RawMaterialBatches
                    .Where(b => b.RawMaterialId == id)
                    .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

                rawMaterial.Stock = totalFromBatches;

                // I-save muna ito
                await _context.SaveChangesAsync();

                // 🔥 KUNG NAGBAGO ANG PRESYO, I-UPDATE ANG LAHAT NG PRODUCT PRICES
                if (priceChanged)
                {
                    await UpdateAllProductPricesAffectedByRawMaterial(id);
                }

                return Json(new { success = true, message = "Raw material updated successfully", newStock = totalFromBatches });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
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

                // ✅ CHECK KUNG GINAGAMIT SA KAHIT ANONG RECIPE
                var recipeItems = await _context.RecipeItems
                    .Include(ri => ri.ProductSize)
                        .ThenInclude(ps => ps.Product)
                    .Where(ri => ri.RawMaterialId == id)
                    .ToListAsync();

                if (recipeItems.Any())
                {
                    // Kunin ang mga unique na product na apektado
                    var affectedProducts = recipeItems
                        .Select(ri => new
                        {
                            ProductName = ri.ProductSize.Product.Name,
                            SizeName = ri.ProductSize.SizeName,
                            Stock = ri.ProductSize.Stock,
                            ProductSizeId = ri.ProductSize.ProductSizeId
                        })
                        .Distinct()
                        .ToList();

                    // I-format ang message
                    var productList = string.Join("\n", affectedProducts.Select(p => $"  • {p.ProductName} - {p.SizeName} (Stock: {p.Stock} pcs)"));

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
        public JsonResult DeductStockForProduction(int rawMaterialId, decimal quantity, int productId, string referenceNo)
        {
            try
            {
                bool success = _fifoService.DeductStock(rawMaterialId, quantity, productId, referenceNo);

                if (success)
                {
                    return Json(new { success = true, message = "Stock deducted using FIFO" });
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
        // In RawMaterialController.cs - Update BatchHistory action
        [HttpGet]
        public async Task<IActionResult> BatchHistory(int rawMaterialId)
        {
            var history = await _fifoService.GetBatchHistoryAsync(rawMaterialId);
            return Ok(history);
        }

        // BAGONG ACTION: Magdagdag ng bagong delivery
        // Add this class inside RawMaterialController.cs
        public class AddDeliveryRequest
        {
            public int RawMaterialId { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public DateTime? ExpiryDate { get; set; }
        }

        // Then update your AddDelivery method:
        [HttpPost]
        public async Task<JsonResult> AddDelivery([FromBody] AddDeliveryRequest request)
        {
            try
            {
                // Add debug logging
                System.Diagnostics.Debug.WriteLine($"=== ADD DELIVERY CALLED ===");
                System.Diagnostics.Debug.WriteLine($"RawMaterialId: {request?.RawMaterialId}");
                System.Diagnostics.Debug.WriteLine($"Quantity: {request?.Quantity}");
                System.Diagnostics.Debug.WriteLine($"UnitPrice: {request?.UnitPrice}");
                System.Diagnostics.Debug.WriteLine($"ExpiryDate: {request?.ExpiryDate}");

                if (request == null)
                {
                    return Json(new { success = false, message = "Invalid request data" });
                }

                // I-check kung existing ang raw material
                var rawMaterial = await _context.RawMaterials.FindAsync(request.RawMaterialId);
                if (rawMaterial == null)
                {
                    return Json(new { success = false, message = $"Raw material with ID {request.RawMaterialId} not found" });
                }

                // I-create ang batch
                var batch = new RawMaterialBatch
                {
                    RawMaterialId = request.RawMaterialId,
                    QuantityReceived = request.Quantity,
                    QuantityRemaining = request.Quantity,
                    ReceivedDate = DateTime.Now,
                    ExpiryDate = request.ExpiryDate,
                    UnitPrice = request.UnitPrice
                };

                _context.RawMaterialBatches.Add(batch);
                await _context.SaveChangesAsync();

                // I-update ang main stock
                await UpdateMainStockColumn(request.RawMaterialId);

                // 🔥 BAGO: I-update ang lahat ng product prices na gumagamit ng raw material na ito
                await UpdateProductPricesForRawMaterial(request.RawMaterialId);

                return Json(new { success = true, message = "Delivery added successfully! Batch ID: " + batch.BatchId });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    innerException = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        // Helper method para mag-update ng main stock column
        private async Task UpdateMainStockColumn(int rawMaterialId)
        {
            var totalRemaining = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var rawMaterial = await _context.RawMaterials.FindAsync(rawMaterialId);
            if (rawMaterial != null)
            {
                rawMaterial.Stock = totalRemaining;
            }
            await _context.SaveChangesAsync();
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
            var products = await _context.RecipeItems
                .Include(ri => ri.ProductSize)
                    .ThenInclude(ps => ps.Product)
                .Where(ri => ri.RawMaterialId == id)
                .Select(ri => new
                {
                    productId = ri.ProductSize.Product.ProductId,
                    productName = ri.ProductSize.Product.Name,
                    sizeName = ri.ProductSize.SizeName,
                    stock = ri.ProductSize.Stock,
                    recipeQuantity = ri.Quantity,
                    price = ri.ProductSize.Price
                })
                .Distinct()
                .ToListAsync();

            return Json(products);
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
                    r.Category
                })
                .FirstOrDefaultAsync();

            if (material == null)
                return Json(new { success = false, message = "Material not found" });

            return Json(new
            {
                success = true,
                name = material.Name,
                stock = material.Stock,
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
                    latestBatchPrice = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == r.RawMaterialId && b.QuantityRemaining > 0)
                        .OrderByDescending(b => b.ReceivedDate)
                        .Select(b => b.UnitPrice)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return Json(materials);
        }

    }
}