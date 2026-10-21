using Infoman.Data;
using Infoman.Filters;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Controllers
{
    // Owner lang ang may access (gamit ang filter na naka-register na sa Program.cs)
    [ServiceFilter(typeof(OwnerAuthorizationFilter))]
    public class CategoryController : Controller
    {
        private readonly ConnectionDatabase _db;

        public CategoryController(ConnectionDatabase db)
        {
            _db = db;
        }

        // ============ READ ============

        // GET /Category/GetAll?type=RawMaterial  (type optional) - para sa Settings table
        [HttpGet]
        public async Task<IActionResult> GetAll(string? type = null)
        {
            var query = _db.Categories.AsNoTracking().AsQueryable();

            if (TryParseType(type, out var parsed))
                query = query.Where(c => c.Type == parsed);

            var cats = await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();

            // Bilang ng gumagamit sa bawat category (1 query bawat table, hindi per-category)
            var rawCounts = await _db.RawMaterials
                .Where(r => r.CategoryId != null)
                .GroupBy(r => r.CategoryId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id!.Value, x => x.Count);

            var productCounts = await _db.Products
                .Where(p => !p.IsDeleted)
                .GroupBy(p => p.Category)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count, StringComparer.OrdinalIgnoreCase);

            var result = cats.Select(c => new
            {
                id = c.CategoryId,
                name = c.Name,
                type = c.Type.ToString(),
                isActive = c.IsActive,
                createdAt = c.CreatedAt,
                usageCount = c.Type == CategoryType.RawMaterial
                    ? rawCounts.GetValueOrDefault(c.CategoryId)
                    : productCounts.GetValueOrDefault(c.Name)
            });

            return Json(new { success = true, categories = result });
        }

        // GET /Category/GetOptions?type=Product - ACTIVE lang, para sa mga dropdown
        [HttpGet]
        public async Task<IActionResult> GetOptions(string type)
        {
            if (!TryParseType(type, out var parsed))
                return Json(new List<object>());

            var list = await _db.Categories
                .AsNoTracking()
                .Where(c => c.Type == parsed && c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new { id = c.CategoryId, name = c.Name })
                .ToListAsync();

            return Json(list);
        }

        // ============ CREATE ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CategorySaveRequest request)
        {
            var (error, name, type) = Validate(request);
            if (error != null)
                return Json(new { success = false, message = error });

            if (await _db.Categories.AnyAsync(c => c.Type == type && c.Name == name))
                return Json(new { success = false, message = $"A {type} category named \"{name}\" already exists." });

            var cat = new Category
            {
                Name = name,
                Type = type,
                IsActive = request.IsActive,
                CreatedAt = DateTime.Now
            };

            try
            {
                _db.Categories.Add(cat);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Json(new { success = false, message = "Could not save the category. It may already exist." });
            }

            return Json(new { success = true, message = "Category added successfully!", id = cat.CategoryId });
        }

        // ============ UPDATE ============

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update([FromBody] CategorySaveRequest request)
        {
            var (error, name, type) = Validate(request);
            if (error != null)
                return Json(new { success = false, message = error });

            var cat = await _db.Categories.FindAsync(request.CategoryId);
            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            // Duplicate check (hindi isinama ang sarili)
            if (await _db.Categories.AnyAsync(c => c.Type == type && c.Name == name && c.CategoryId != cat.CategoryId))
                return Json(new { success = false, message = $"A {type} category named \"{name}\" already exists." });

            // Bawal palitan ang Type kung may gumagamit na (masisira ang mga dropdown/records)
            if (cat.Type != type)
            {
                int usage = await CountUsageAsync(cat);
                if (usage > 0)
                    return Json(new { success = false, message = $"Cannot change the type of \"{cat.Name}\" because it is used by {usage} item(s)." });
            }

            string oldName = cat.Name;

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                cat.Name = name;
                cat.Type = type;
                cat.IsActive = request.IsActive;
                await _db.SaveChangesAsync();

                // Product.Category ay string pa rin, kaya i-update din ang mga product kapag na-rename
                // (kasama ang archived, para consistent pa rin)
                if (type == CategoryType.Product && !string.Equals(oldName, name, StringComparison.Ordinal))
                {
                    await _db.Products
                        .Where(p => p.Category == oldName)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Category, name));
                }

                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Could not update the category." });
            }

            return Json(new { success = true, message = "Category updated successfully!" });
        }

        // ============ DELETE (with in-use validation) ============

        // POST /Category/Delete?id=5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            int count = await CountUsageAsync(cat);
            if (count > 0)
            {
                string label = cat.Type == CategoryType.RawMaterial ? "raw material(s)" : "product(s)";
                return Json(new
                {
                    success = false,
                    inUse = true,
                    count,
                    message = $"Cannot delete \"{cat.Name}\" because it is currently used by {count} {label}.\n\nReassign or remove those items first."
                });
            }

            try
            {
                _db.Categories.Remove(cat);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Safety net kung may nag-assign ng category habang nagde-delete
                return Json(new { success = false, inUse = true, message = "This category was just assigned to an item and cannot be deleted." });
            }

            return Json(new { success = true, message = "Category deleted successfully." });
        }

        // ============ HELPERS ============

        private async Task<int> CountUsageAsync(Category cat)
        {
            int id = cat.CategoryId;
            string name = cat.Name;

            if (cat.Type == CategoryType.RawMaterial)
                // Kasama ang soft-deleted na raw materials, dahil puwede pa silang ma-Restore at may FK
                return await _db.RawMaterials.CountAsync(r => r.CategoryId == id);

            // Product: active products lang ang binibilang
            return await _db.Products.CountAsync(p => !p.IsDeleted && p.Category == name);
        }

        private static bool TryParseType(string? value, out CategoryType type)
        {
            return Enum.TryParse(value, true, out type) && Enum.IsDefined(type);
        }

        private static (string? Error, string Name, CategoryType Type) Validate(CategorySaveRequest? r)
        {
            if (r == null) return ("Invalid request.", "", default);

            var name = (r.Name ?? "").Trim();
            if (name.Length == 0) return ("Category name is required.", name, default);
            if (name.Length > 100) return ("Category name must be 100 characters or less.", name, default);
            if (!TryParseType(r.Type, out var type)) return ("Please select a valid category type.", name, default);

            return (null, name, type);
        }
    }

    public class CategorySaveRequest
    {
        public int CategoryId { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }     // "RawMaterial" or "Product"
        public bool IsActive { get; set; } = true;
    }
}