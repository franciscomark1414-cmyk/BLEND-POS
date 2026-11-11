using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Services
{
    public class CashCountLine
    {
        public decimal Denomination { get; set; }
        public int Quantity { get; set; }
    }
    public class ShiftService
    {
        private readonly ConnectionDatabase _db;

        public ShiftService(ConnectionDatabase db)
        {
            _db = db;
        }

        // ============================================================
        // 1. GET ACTIVE SHIFT (yung status = OPEN)
        // ============================================================
        public async Task<Shift?> GetActiveShiftAsync()
        {
            return await _db.Shifts
                .Include(s => s.StaffUser)
                .FirstOrDefaultAsync(s => s.Status == "OPEN");
        }

        // ============================================================
        // 2. GET ACTIVE SHIFT BY STAFF (para sa specific user)
        // ============================================================
        public async Task<Shift?> GetActiveShiftByStaffAsync(int staffUserId)
        {
            return await _db.Shifts
                .Include(s => s.StaffUser)
                .FirstOrDefaultAsync(s => s.Status == "OPEN" && s.StaffUserId == staffUserId);
        }

        // ============================================================
        // 3. START SHIFT (mag-open ng bagong shift)
        // ============================================================
        public async Task<(bool success, string message, Shift? shift)> StartShiftAsync(
      int staffUserId, decimal openingFloat, string? notes = null)
        {
            // ✅ I-CHECK LANG ANG SHIFT NG CURRENT STAFF (hindi lahat)
            var existingActive = await GetActiveShiftByStaffAsync(staffUserId);
            if (existingActive != null)
            {
                return (false,
                    $"You already have an active shift (started {existingActive.StartTime:MMM dd, hh:mm tt}). Please close it first.",
                    null);
            }

            var shift = new Shift
            {
                StaffUserId = staffUserId,
                StartTime = DateTime.Now,
                OpeningFloat = openingFloat,
                Status = "OPEN",
                CreatedAt = DateTime.Now
            };

            _db.Shifts.Add(shift);
            await _db.SaveChangesAsync();

            return (true, "Shift opened successfully.", shift);
        }

        // ============================================================
        // 4. CALCULATE EXPECTED CASH (para sa isang shift)
        // ============================================================
        public async Task<decimal> CalculateExpectedCashAsync(int shiftId)
        {
            var shift = await _db.Shifts.FindAsync(shiftId);
            if (shift == null) return 0;

            // Cash sales lang — hindi kasama ang GCash/QRPh
            var cashSales = await _db.Orders
                .Where(o => o.ShiftId == shiftId
                         && o.PaymentMethod == "Cash"
                         && (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING"))
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            // Cash expenses (kasama ang withdrawals — same table, iba lang category)
            var cashExpenses = await _db.Expenses
                .Where(e => e.ShiftId == shiftId)
                .SumAsync(e => (decimal?)e.Amount) ?? 0;

            decimal expected = shift.OpeningFloat + cashSales - cashExpenses;

            System.Diagnostics.Debug.WriteLine($"=== SHIFT {shiftId} CALC ===");
            System.Diagnostics.Debug.WriteLine($"OpeningFloat: {shift.OpeningFloat}");
            System.Diagnostics.Debug.WriteLine($"CashSales: {cashSales}");
            System.Diagnostics.Debug.WriteLine($"CashExpenses: {cashExpenses}");
            System.Diagnostics.Debug.WriteLine($"Expected: {expected}");
            return Math.Round(expected, 2);
        }

        // ============================================================
        // 5. CLOSE SHIFT (normal close with physical count)
        // ============================================================
        public async Task<(bool success, string message, Shift? shift)> CloseShiftAsync(
    int shiftId,
    decimal physicalCashCounted,
    string? notes = null,
    int? closedByUserId = null,
    List<CashCountLine>? cashCount = null,
    decimal? gcashCounted = null)
        {
            System.Diagnostics.Debug.WriteLine($"=== CloseShiftAsync CALLED === shiftId={shiftId}, physicalCash={physicalCashCounted}");

            var shift = await _db.Shifts
    .Include(s => s.CashCounts)
    .FirstOrDefaultAsync(s => s.ShiftId == shiftId);
            if (shift == null)
            {
                System.Diagnostics.Debug.WriteLine($"=== CloseShiftAsync: SHIFT {shiftId} NOT FOUND ===");
                return (false, "Shift not found.", null);
            }

            System.Diagnostics.Debug.WriteLine($"=== CloseShiftAsync: SHIFT FOUND === Status={shift.Status}");

            if (shift.Status != "OPEN")
            {
                System.Diagnostics.Debug.WriteLine($"=== CloseShiftAsync: SHIFT NOT OPEN === Status={shift.Status}");
                return (false, "Shift is not open anymore.", null);
            }

            // ✅ I-DECLARE MUNA ANG expectedCash
            decimal expectedCash = await CalculateExpectedCashAsync(shiftId);

            // ✅ TAPOS, I-LOG
            System.Diagnostics.Debug.WriteLine($"=== CloseShiftAsync: expectedCash={expectedCash} ===");

            decimal variance = physicalCashCounted - expectedCash;

            shift.EndTime = DateTime.Now;
            shift.ExpectedCash = expectedCash;
            shift.PhysicalCashCounted = physicalCashCounted;
            shift.Variance = Math.Round(variance, 2);
            shift.Status = "CLOSED";
            shift.ClosingNotes = notes;
            shift.ClosedByUserId = closedByUserId ?? shift.StaffUserId;
            ApplyCashCount(shift, cashCount, gcashCounted);
            shift.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            string varianceText = variance == 0 ? "balanced"
                                : variance > 0 ? $"OVER by ₱{variance:N2}"
                                : $"SHORT by ₱{Math.Abs(variance):N2}";

            return (true, $"Shift closed. Cash is {varianceText}.", shift);
        }

        // ============================================================
        // 6. FORCE CLOSE (kapag nakalimutan mag-close, o ibang staff nag-login)
        // ============================================================
        public async Task<(bool success, string message, Shift? shift)> ForceCloseShiftAsync(
    int shiftId,
    decimal physicalCashCounted,
    int closedByUserId,
    string? notes = null,
    List<CashCountLine>? cashCount = null,
    decimal? gcashCounted = null)
        {
            var shift = await _db.Shifts
    .Include(s => s.CashCounts)
    .FirstOrDefaultAsync(s => s.ShiftId == shiftId);
            if (shift == null)
                return (false, "Shift not found.", null);

            if (shift.Status != "OPEN")
                return (false, "Shift is not open anymore.", null);

            decimal expectedCash = await CalculateExpectedCashAsync(shiftId);
            decimal variance = physicalCashCounted - expectedCash;

            shift.EndTime = DateTime.Now;
            shift.ExpectedCash = expectedCash;
            shift.PhysicalCashCounted = physicalCashCounted;
            shift.Variance = Math.Round(variance, 2);
            shift.Status = "FORCE_CLOSED";
            shift.ClosingNotes = notes ?? "Force closed by system";
            shift.ClosedByUserId = closedByUserId;
            ApplyCashCount(shift, cashCount, gcashCounted);
            shift.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync();

            string varianceText = variance == 0 ? "balanced"
                                : variance > 0 ? $"OVER by ₱{variance:N2}"
                                : $"SHORT by ₱{Math.Abs(variance):N2}";

            return (true, $"Shift force-closed. Cash is {varianceText}.", shift);
        }

        // ============================================================
        // 7. GET ALL SHIFTS (para sa Reports)
        // ============================================================
        public async Task<List<Shift>> GetAllShiftsAsync(DateTime? from = null, DateTime? to = null)
        {
            var query = _db.Shifts
                .Include(s => s.StaffUser)
                .Include(s => s.ClosedByUser)
                .AsQueryable();

            if (from.HasValue)
                query = query.Where(s => s.StartTime.Date >= from.Value.Date);

            if (to.HasValue)
                query = query.Where(s => s.StartTime.Date <= to.Value.Date);

            return await query
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        }



        // ============================================================
        // 8. GET SHIFT BY ID (para sa "View Details")
        // ============================================================
        public async Task<Shift?> GetShiftByIdAsync(int shiftId)
        {
            return await _db.Shifts
                .Include(s => s.StaffUser)
                .Include(s => s.ClosedByUser)
                .Include(s => s.CashCounts)
                .FirstOrDefaultAsync(s => s.ShiftId == shiftId);
        }

       

        private static void ApplyCashCount(Shift shift, List<CashCountLine>? lines, decimal? gcashCounted)
        {
            shift.GcashCounted = gcashCounted;

            if (lines == null || !lines.Any()) return;

            shift.CashCounts.Clear();
            foreach (var line in lines.Where(l => l.Quantity > 0))
            {
                shift.CashCounts.Add(new ShiftCashCount
                {
                    Denomination = line.Denomination,
                    Quantity = line.Quantity,
                    Subtotal = line.Denomination * line.Quantity
                });
            }
        }


    }
}