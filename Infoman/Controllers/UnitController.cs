using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Controllers
{
    public class UnitController : Controller
    {
        private readonly ConnectionDatabase _context;

        public UnitController(ConnectionDatabase context)
        {
            _context = context;
        }

        // GET: /Unit/GetAll
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var units = await _context.Units
                .OrderBy(u => u.Name)
                .Select(u => new {
                    u.UnitId,
                    u.Name,
                    u.Symbol,
                    u.BaseUnit,
                    u.ConversionFactor,
                    u.IsActive
                })
                .ToListAsync();

            return Json(new { success = true, units });
        }

        // POST: /Unit/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] Unit unit)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(unit.Name) || string.IsNullOrWhiteSpace(unit.Symbol))
                    return Json(new { success = false, message = "Name and Symbol are required" });

                _context.Units.Add(unit);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Unit added successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Unit/Update
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update([FromBody] Unit unit)
        {
            try
            {
                var existing = await _context.Units.FindAsync(unit.UnitId);
                if (existing == null)
                    return Json(new { success = false, message = "Unit not found" });

                existing.Name = unit.Name;
                existing.Symbol = unit.Symbol;
                existing.BaseUnit = unit.BaseUnit;
                existing.ConversionFactor = unit.ConversionFactor;
                existing.IsActive = unit.IsActive;

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Unit updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Unit/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var unit = await _context.Units.FindAsync(id);
                if (unit == null)
                    return Json(new { success = false, message = "Unit not found" });

                // Check kung ginagamit sa RawMaterials
                bool isUsed = await _context.RawMaterials.AnyAsync(r => r.Unit == unit.Symbol && !r.IsDeleted);
                if (isUsed)
                {
                    // Soft delete
                    unit.IsActive = false;
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Unit set to Inactive (used by raw materials)" });
                }

                _context.Units.Remove(unit);
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Unit deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}