using Infoman.Data;
using Infoman.Filters;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Controllers
{
    [ServiceFilter(typeof(OwnerAuthorizationFilter))]
    public class CategoryController : Controller
    {
        private readonly ConnectionDatabase _db;

        public CategoryController(ConnectionDatabase db)
        {
            _db = db;
        }

        // GET /Category/GetAll
        [HttpGet]
        public async Task<IActionResult> GetAll(string? type = null)
        {
            var query = _db.Categories.AsNoTracking().AsQueryable();

            if (TryParseType(type, out var parsed))
                query = query.Where(c => c.Type == parsed);

            var cats = await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();

            // ✅ BILANGIN ANG GAMIT SA BAWAT TABLE
            var rawCounts = await _db.RawMaterials
                .Where(r => r.CategoryId != null && !r.IsDeleted)
                .GroupBy(r => r.CategoryId)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id!.Value, x => x.Count);

            var productCounts = await _db.Products
                .Where(p => !p.IsDeleted)
                .GroupBy(p => p.Category)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name, x => x.Count, StringComparer.OrdinalIgnoreCase);

            var addOnCounts = await _db.AddOns
                .Where(a => !a.IsDeleted && a.Category != null)
                .GroupBy(a => a.Category)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Name!, x => x.Count, StringComparer.OrdinalIgnoreCase);

            var result = cats.Select(c => new
            {
                id = c.CategoryId,
                name = c.Name,
                type = c.Type.ToString(),
                isActive = c.IsActive,
                createdAt = c.CreatedAt,
                usageCount = c.Type switch
                {
                    CategoryType.RawMaterial => rawCounts.GetValueOrDefault(c.CategoryId),
                    CategoryType.Product => productCounts.GetValueOrDefault(c.Name),
                    CategoryType.AddOn => addOnCounts.GetValueOrDefault(c.Name),
                    _ => 0
                }
            });

            return Json(new { success = true, categories = result });
        }

        // GET /Category/GetOptions
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

        // POST /Category/Create
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

        // POST /Category/Update
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

            if (await _db.Categories.AnyAsync(c => c.Type == type && c.Name == name && c.CategoryId != cat.CategoryId))
                return Json(new { success = false, message = $"A {type} category named \"{name}\" already exists." });

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

                if (type == CategoryType.Product && !string.Equals(oldName, name, StringComparison.Ordinal))
                {
                    await _db.Products
                        .Where(p => p.Category == oldName)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Category, name));
                }
                else if (type == CategoryType.AddOn && !string.Equals(oldName, name, StringComparison.Ordinal))
                {
                    await _db.AddOns
                        .Where(a => a.Category == oldName)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Category, name));
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

        // POST /Category/Delete
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
                string label = cat.Type switch
                {
                    CategoryType.RawMaterial => "raw material(s)",
                    CategoryType.Product => "product(s)",
                    CategoryType.AddOn => "add-on(s)",
                    _ => "item(s)"
                };

                return Json(new
                {
                    success = false,
                    inUse = true,
                    count,
                    message = $"Cannot delete \"{cat.Name}\" because it is currently used by {count} {label}.\n\nReassign or remove those items first."
                });
            }

            if (cat.Type == CategoryType.RawMaterial)
            {
                await _db.RawMaterials
                    .Where(r => r.CategoryId == id && r.IsDeleted)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.CategoryId, (int?)null));
            }

            try
            {
                _db.Categories.Remove(cat);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
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
                return await _db.RawMaterials.CountAsync(r => r.CategoryId == id && !r.IsDeleted);

            if (cat.Type == CategoryType.AddOn)
                return await _db.AddOns.CountAsync(a => !a.IsDeleted && a.Category == name);

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
        public string? Type { get; set; }
        public bool IsActive { get; set; } = true;
    }
}