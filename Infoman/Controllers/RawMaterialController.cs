using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class RawMaterialController : Controller
    {
        private readonly ConnectionDatabase _context;

        public RawMaterialController(ConnectionDatabase context)
        {
            _context = context;
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

        // EDIT - POST (FIXED)
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

                // I-update ang raw material
                rawMaterial.Name = model.Name;
                rawMaterial.Category = model.Category;
                rawMaterial.Stock = model.Stock;
                rawMaterial.MinQty = model.MinQty;
                rawMaterial.Unit = model.Unit;
                rawMaterial.Price = model.Price;
                rawMaterial.ExpirationDate = model.ExpirationDate;

                await _context.SaveChangesAsync();

                // 🔥 KUNG NAGBAGO ANG PRESYO, I-UPDATE ANG LAHAT NG PRODUCT PRICES
                if (priceChanged)
                {
                    await UpdateAllProductPricesAffectedByRawMaterial(id);
                }

                return Json(new { success = true, message = "Raw material updated successfully" });
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

                material.IsDeleted = true;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Material deleted successfully" });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Delete error: {ex.Message}");
                return Json(new { success = false, message = "An error occurred while deleting" });
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
    }
}