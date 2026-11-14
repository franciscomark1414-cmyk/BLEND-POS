using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class AddOnService
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;

        public AddOnService(ConnectionDatabase db)
        {
            _db = db;
            _fifoService = new FIFOService(db);
        }

        // ============================================================
        // 1. GET ALL (para sa Owner Settings tab)
        // ============================================================
        public async Task<List<AddOn>> GetAllAsync(bool includeInactive = true)
        {
            var query = _db.AddOns
                .Include(a => a.RecipeItems)
                    .ThenInclude(r => r.RawMaterial)
                .Where(a => !a.IsDeleted);

            if (!includeInactive)
                query = query.Where(a => a.IsActive);

            return await query
                .OrderBy(a => a.Name)
                .ToListAsync();
        }

        // ============================================================
        // 2. GET BY ID
        // ============================================================
        public async Task<AddOn?> GetByIdAsync(int addOnId)
        {
            return await _db.AddOns
                .Include(a => a.RecipeItems)
                    .ThenInclude(r => r.RawMaterial)
                .FirstOrDefaultAsync(a => a.AddOnId == addOnId && !a.IsDeleted);
        }

        // ============================================================
        // 3. CREATE
        // ============================================================
        public async Task<(bool success, string message, AddOn? addOn)> CreateAsync(
            string name,
            string? category,
            decimal price,
            decimal margin,
            bool isManualPrice,
            int maxPerOrder,
            List<AddOnRecipeInput> ingredients)
        {
            // --- VALIDATION ---
            var validation = await ValidateAddOnInputAsync(
    name, category, price, margin, isManualPrice, maxPerOrder, ingredients, null);

            if (!validation.isValid)
                return (false, validation.message, null);

            // --- GENERATE SKU ---
            string sku = await GenerateUniqueSkuAsync(name);

            var addOn = new AddOn
            {
                Name = name.Trim(),
                SKU = sku,
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                Price = price,
                Margin = margin,
                IsManualPrice = isManualPrice,
                MaxPerOrder = maxPerOrder,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            // --- BUILD RECIPE ITEMS (convert to base unit) ---
            foreach (var ing in ingredients)
            {
                var rawMat = await _db.RawMaterials.FindAsync(ing.RawMaterialId);
                if (rawMat == null) continue;

                decimal quantityInBaseUnit = UnitConverter.ToBaseUnit(
                    ing.Quantity, ing.UnitUsed, rawMat.BaseUnit);

                addOn.RecipeItems.Add(new AddOnRecipeItem
                {
                    RawMaterialId = ing.RawMaterialId,
                    Quantity = quantityInBaseUnit,
                    UnitUsed = rawMat.BaseUnit
                });
            }

            // --- AUTO-COMPUTE PRICE KUNG HINDI MANUAL ---
            if (!isManualPrice)
            {
                decimal recipeCost = await ComputeRecipeCostAsync(addOn.RecipeItems.ToList());
                addOn.Price = RoundToEnding9(recipeCost / (1 - margin));
            }

            _db.AddOns.Add(addOn);

            try
            {
                await _db.SaveChangesAsync();
                return (true, $"Add-on '{addOn.Name}' created successfully.", addOn);
            }
            catch (DbUpdateException ex)
            {
                return (false, "Database error: " + GetInnerMessage(ex), null);
            }
        }

        // ============================================================
        // 4. UPDATE
        // ============================================================
        public async Task<(bool success, string message)> UpdateAsync(
            int addOnId,
            string name,
            string? category,
            decimal price,
            decimal margin,
            bool isManualPrice,
            int maxPerOrder,
            bool isActive,
            List<AddOnRecipeInput> ingredients)
        {
            var addOn = await _db.AddOns
                .Include(a => a.RecipeItems)
                .FirstOrDefaultAsync(a => a.AddOnId == addOnId && !a.IsDeleted);

            if (addOn == null)
                return (false, "Add-on not found.");

            // --- VALIDATION ---
            var validation = await ValidateAddOnInputAsync(
     name, category, price, margin, isManualPrice, maxPerOrder, ingredients, addOnId);

            if (!validation.isValid)
                return (false, validation.message);

            // --- UPDATE BASIC FIELDS ---
            addOn.Name = name.Trim();
            addOn.Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            addOn.Margin = margin;
            addOn.IsManualPrice = isManualPrice;
            addOn.MaxPerOrder = maxPerOrder;
            addOn.IsActive = isActive;
            addOn.UpdatedAt = DateTime.UtcNow;

            // --- REPLACE RECIPE (soft delete old, add new) ---
            foreach (var oldItem in addOn.RecipeItems)
                oldItem.IsDeleted = true;

            addOn.RecipeItems.Clear(); // detach from nav

            foreach (var ing in ingredients)
            {
                var rawMat = await _db.RawMaterials.FindAsync(ing.RawMaterialId);
                if (rawMat == null) continue;

                decimal quantityInBaseUnit = UnitConverter.ToBaseUnit(
                    ing.Quantity, ing.UnitUsed, rawMat.BaseUnit);

                _db.AddOnRecipeItems.Add(new AddOnRecipeItem
                {
                    AddOnId = addOn.AddOnId,
                    RawMaterialId = ing.RawMaterialId,
                    Quantity = quantityInBaseUnit,
                    UnitUsed = rawMat.BaseUnit
                });
            }

            // --- SET PRICE ---
            if (isManualPrice)
            {
                addOn.Price = price;
            }
            else
            {
                // Auto-compute from new recipe
                var newRecipe = ingredients.Select(i => new AddOnRecipeItem
                {
                    RawMaterialId = i.RawMaterialId,
                    Quantity = i.Quantity,
                    UnitUsed = i.UnitUsed
                }).ToList();

                decimal recipeCost = await ComputeRecipeCostAsync(newRecipe);
                addOn.Price = RoundToEnding9(recipeCost / (1 - margin));
            }

            try
            {
                await _db.SaveChangesAsync();
                return (true, $"Add-on '{addOn.Name}' updated successfully.");
            }
            catch (DbUpdateException ex)
            {
                return (false, "Database error: " + GetInnerMessage(ex));
            }
        }

        // ============================================================
        // 5. SOFT DELETE
        // ============================================================
        public async Task<(bool success, string message)> SoftDeleteAsync(int addOnId)
        {
            var addOn = await _db.AddOns
                .Include(a => a.RecipeItems)
                .FirstOrDefaultAsync(a => a.AddOnId == addOnId && !a.IsDeleted);

            if (addOn == null)
                return (false, "Add-on not found.");

            // Check kung may pending orders (para may warning)
            var pendingCount = await _db.OrderItemAddOns
                .Include(o => o.OrderItem)
                .CountAsync(o => o.AddOnId == addOnId
                    && o.OrderItem != null
                    && o.OrderItem.Order != null
                    && o.OrderItem.Order.Status == "PENDING_BLENDING");

            addOn.IsDeleted = true;
            addOn.IsActive = false;
            addOn.UpdatedAt = DateTime.UtcNow;

            foreach (var item in addOn.RecipeItems)
                item.IsDeleted = true;

            await _db.SaveChangesAsync();

            string msg = $"Add-on '{addOn.Name}' deleted.";
            if (pendingCount > 0)
                msg += $" ⚠️ May {pendingCount} pending order(s) pa na gumagamit nito — i-void o hintayin ma-blend bago mag-delete.";

            return (true, msg);
        }

        // ============================================================
        // 6. TOGGLE ACTIVE
        // ============================================================
        public async Task<(bool success, string message)> ToggleActiveAsync(int addOnId, bool isActive)
        {
            var addOn = await _db.AddOns
                .FirstOrDefaultAsync(a => a.AddOnId == addOnId && !a.IsDeleted);

            if (addOn == null)
                return (false, "Add-on not found.");

            addOn.IsActive = isActive;
            addOn.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return (true, $"Add-on '{addOn.Name}' is now {(isActive ? "Active" : "Inactive")}.");
        }

        // ============================================================
        // 7. GET ACTIVE FOR POS (walang recipe — lightweight)
        // ============================================================
        public async Task<List<AddOn>> GetActiveForPosAsync()
        {
            return await _db.AddOns
                .Where(a => a.IsActive && !a.IsDeleted)
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();
        }

        // ============================================================
        // 8. CHECK AVAILABILITY (POS pre-check + StartBlending guard)
        // ============================================================
        public async Task<AddOnAvailabilityResult> CheckAvailabilityAsync(
            int addOnId, int itemQuantity)
        {
            var addOn = await _db.AddOns
                .Include(a => a.RecipeItems)
                    .ThenInclude(r => r.RawMaterial)
                .FirstOrDefaultAsync(a => a.AddOnId == addOnId && !a.IsDeleted);

            if (addOn == null)
                return new AddOnAvailabilityResult
                {
                    IsAvailable = false,
                    MaxOrders = 0,
                    Message = "Add-on not found."
                };

            if (!addOn.IsActive)
                return new AddOnAvailabilityResult
                {
                    IsAvailable = false,
                    MaxOrders = 0,
                    Message = $"'{addOn.Name}' is not available."
                };

            var activeRecipe = addOn.RecipeItems.Where(r => !r.IsDeleted).ToList();

            if (!activeRecipe.Any())
                return new AddOnAvailabilityResult
                {
                    IsAvailable = false,
                    MaxOrders = 0,
                    Message = $"'{addOn.Name}' has no recipe configured."
                };

            var today = DateTime.Today;
            int maxOrders = int.MaxValue;
            var insufficient = new List<string>();

            foreach (var recipe in activeRecipe)
            {
                // Available stock sa batches (base unit)
                decimal availableBase = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipe.RawMaterialId
                        && b.QuantityRemaining > 0
                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= today))
                    .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

                // Needed per 1 order item
                decimal neededPerItem = recipe.Quantity;

                if (neededPerItem <= 0) continue;

                // Ilan pang order items ang kaya
                int possible = (int)(availableBase / neededPerItem);
                if (possible < maxOrders) maxOrders = possible;

                // Ilan ang kailangan para sa current request
                decimal neededForRequest = neededPerItem * itemQuantity;

                if (availableBase < neededForRequest)
                {
                    var matName = recipe.RawMaterial?.Name ?? "ingredient";
                    var unit = recipe.RawMaterial?.Unit ?? "";
                    decimal displayAvail = ConvertBaseToDisplay(availableBase, recipe.RawMaterial?.BaseUnit, recipe.RawMaterial?.Unit);
                    decimal displayNeed = ConvertBaseToDisplay(neededForRequest, recipe.RawMaterial?.BaseUnit, recipe.RawMaterial?.Unit);

                    insufficient.Add($"{matName}: need {displayNeed:F2} {unit}, only {displayAvail:F2} {unit} available");
                }
            }

            if (insufficient.Any())
                return new AddOnAvailabilityResult
                {
                    IsAvailable = false,
                    MaxOrders = maxOrders == int.MaxValue ? 0 : maxOrders,
                    Message = string.Join("\n", insufficient)
                };

            return new AddOnAvailabilityResult
            {
                IsAvailable = true,
                MaxOrders = maxOrders == int.MaxValue ? 0 : maxOrders,
                Message = "Available"
            };
        }

        // ============================================================
        // 9. COMPUTE RECIPE COST (para sa auto-price)
        // ============================================================
        public async Task<decimal> ComputeRecipeCostAsync(List<AddOnRecipeItem> recipeItems)
        {
            decimal totalCost = 0;

            foreach (var ri in recipeItems)
            {
                if (ri.Quantity <= 0) continue;

                var rawMat = await _db.RawMaterials.FindAsync(ri.RawMaterialId);
                if (rawMat == null) continue;

                decimal fifoPrice = await _fifoService.GetActiveBatchPrice(ri.RawMaterialId);

                // Convert base-unit qty to cost-unit qty (kg / L / pcs)
                decimal quantityForCost = UnitConverter.ToCostUnitQty(ri.Quantity, rawMat.BaseUnit);

                totalCost += quantityForCost * fifoPrice;
            }

            return totalCost;
        }

        // ============================================================
        // PRIVATE HELPERS
        // ============================================================

        private async Task<(bool isValid, string message)> ValidateAddOnInputAsync(
     string name, string? category, decimal price, decimal margin, bool isManualPrice,
     int maxPerOrder, List<AddOnRecipeInput> ingredients, int? excludeAddOnId)
        {
            // Name
            if (string.IsNullOrWhiteSpace(name))
                return (false, "Add-on name is required.");

            if (name.Trim().Length > 100)
                return (false, "Add-on name must be 100 characters or less.");

            // Unique name (active, non-deleted)
            var nameLower = name.Trim().ToLower();
            var duplicateQuery = _db.AddOns
                .Where(a => !a.IsDeleted && a.Name.ToLower() == nameLower);

            if (excludeAddOnId.HasValue)
                duplicateQuery = duplicateQuery.Where(a => a.AddOnId != excludeAddOnId.Value);

            if (await duplicateQuery.AnyAsync())
                return (false, $"An add-on named '{name.Trim()}' already exists.");

            // ✅ CATEGORY VALIDATION
            if (!string.IsNullOrWhiteSpace(category))
            {
                bool categoryExists = await _db.Categories.AnyAsync(c =>
                    c.Name == category.Trim() &&
                    c.Type == CategoryType.AddOn &&
                    c.IsActive);

                if (!categoryExists)
                    return (false, "Invalid category. Please select from Category Management.");
            }

            // Margin
            if (margin <= 0 || margin >= 1)
                return (false, "Margin must be between 0 and 1 (e.g., 0.30 for 30%).");

            // Price (kapag manual lang)
            if (isManualPrice && price <= 0)
                return (false, "Manual price must be greater than 0.");

            // MaxPerOrder
            if (maxPerOrder < 1 || maxPerOrder > 20)
                return (false, "Max per order must be between 1 and 20.");

            // Ingredients
            if (ingredients == null || !ingredients.Any())
                return (false, "Please add at least one ingredient.");

            // Unit validation...
            var weightUnits = new[] { "kg", "g" };
            var volumeUnits = new[] { "L", "mL" };
            var countUnits = new[] { "pcs" };

            foreach (var ing in ingredients)
            {
                if (ing.Quantity <= 0)
                    return (false, $"Ingredient quantity must be greater than 0.");

                var rawMat = await _db.RawMaterials.FindAsync(ing.RawMaterialId);
                if (rawMat == null)
                    return (false, $"Ingredient (ID: {ing.RawMaterialId}) not found.");

                bool isWeightMat = weightUnits.Contains(rawMat.Unit);
                bool isVolumeMat = volumeUnits.Contains(rawMat.Unit);
                bool isCountMat = countUnits.Contains(rawMat.Unit);

                bool isWeightIng = weightUnits.Contains(ing.UnitUsed);
                bool isVolumeIng = volumeUnits.Contains(ing.UnitUsed);
                bool isCountIng = countUnits.Contains(ing.UnitUsed);

                bool compatible = (isWeightMat && isWeightIng)
                                || (isVolumeMat && isVolumeIng)
                                || (isCountMat && isCountIng);

                if (!compatible)
                    return (false, $"Incompatible unit: Cannot use '{ing.UnitUsed}' for '{rawMat.Name}' (uses '{rawMat.Unit}').");
            }

            return (true, "OK");
        }

        private async Task<string> GenerateUniqueSkuAsync(string name)
        {
            string prefix = "AO";
            string code = name.Trim().Length >= 3
                ? new string(name.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpper()
                : name.ToUpper();

            if (code.Length < 3) code = code.PadRight(3, 'X');
            if (code.Length > 3) code = code.Substring(0, 3);

            string baseSku = $"{prefix}-{code}";

            bool exists = await _db.AddOns.AnyAsync(a => a.SKU == baseSku);
            if (!exists) return baseSku;

            var existing = await _db.AddOns
                .Where(a => a.SKU != null && a.SKU.StartsWith(baseSku + "-"))
                .Select(a => a.SKU)
                .ToListAsync();

            int max = 0;
            foreach (var sku in existing)
            {
                var suffix = sku.Substring((baseSku + "-").Length);
                if (int.TryParse(suffix, out var n) && n > max) max = n;
            }

            return $"{baseSku}-{(max + 1):D2}";
        }

        private static decimal RoundToEnding9(decimal price)
        {
            decimal rounded = Math.Round(price, 0, MidpointRounding.AwayFromZero);
            if (rounded > 0 && rounded < 10) return 9;
            if (rounded >= 10)
            {
                decimal basePrice = Math.Floor(rounded / 10) * 10;
                decimal result = basePrice + 9;
                if (result < rounded) result = basePrice + 10 + 9;
                return result;
            }
            return rounded;
        }

        private static decimal ConvertBaseToDisplay(decimal baseQty, string? baseUnit, string? displayUnit)
        {
            if (baseUnit == "g" && displayUnit == "kg") return baseQty / 1000;
            if (baseUnit == "mL" && displayUnit == "L") return baseQty / 1000;
            return baseQty;
        }

        private static string GetInnerMessage(Exception ex)
        {
            var msg = ex.Message;
            var inner = ex.InnerException;
            while (inner != null)
            {
                msg += " | " + inner.Message;
                inner = inner.InnerException;
            }
            return msg;
        }
    }

    // ============================================================
    // DTOs
    // ============================================================
    public class AddOnRecipeInput
    {
        public int RawMaterialId { get; set; }
        public decimal Quantity { get; set; }
        public string UnitUsed { get; set; } = string.Empty;
    }

    public class AddOnAvailabilityResult
    {
        public bool IsAvailable { get; set; }
        public int MaxOrders { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}