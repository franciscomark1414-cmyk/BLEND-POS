using Infoman.Data;
using Infoman.Models;
using Infoman.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Controllers
{
    public class FinanceController : Controller
    {
        private readonly ConnectionDatabase _context;

        public FinanceController(ConnectionDatabase context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSummary(string periodType, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                DateTime start;
                DateTime end;
                DateTime now = DateTime.Now;

                // Determine Date Range
                switch (periodType?.ToLower())
                {
                    case "daily":
                        start = (startDate ?? now).Date;
                        end = start.AddDays(1).AddSeconds(-1);
                        break;
                    case "semi-monthly":
                        var semiDate = startDate ?? now;
                        if (semiDate.Day <= 15)
                        {
                            start = new DateTime(semiDate.Year, semiDate.Month, 1);
                            end = new DateTime(semiDate.Year, semiDate.Month, 15, 23, 59, 59);
                        }
                        else
                        {
                            start = new DateTime(semiDate.Year, semiDate.Month, 16);
                            end = new DateTime(semiDate.Year, semiDate.Month, DateTime.DaysInMonth(semiDate.Year, semiDate.Month), 23, 59, 59);
                        }
                        break;
                    case "monthly":
                        var monthlyDate = startDate ?? now;
                        start = new DateTime(monthlyDate.Year, monthlyDate.Month, 1);
                        end = start.AddMonths(1).AddSeconds(-1);
                        break;
                    case "custom":
                        start = (startDate ?? now).Date;
                        end = (endDate ?? now).Date.AddDays(1).AddSeconds(-1);
                        break;
                    default:
                        start = now.Date;
                        end = now.Date.AddDays(1).AddSeconds(-1);
                        break;
                }

                // 1. TOTAL SALES (Net Revenue)
                var totalSales = await _context.Orders
                    .Where(o => (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING") && o.Date >= start && o.Date <= end)
                    .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

                // 2. RAW MATERIAL PURCHASES
                var receivingItemsList = await _context.ReceivingItems
                    .Include(ri => ri.RawMaterial)
                    .Include(ri => ri.Receiving)
                    .Where(ri => ri.Receiving.DateReceived >= start && ri.Receiving.DateReceived <= end)
                    .Where(ri => !ri.RawMaterial.IsDeleted) // ✅ IDAGDAG ITO: Wag isama kung deleted na ang material
                    .ToListAsync();

                decimal purchases = 0;
                foreach (var ri in receivingItemsList)
                {
                    if (ri.RawMaterial == null) continue;

                    decimal qtyForCost = ri.AcceptedQty;

                    if (ri.RawMaterial.BaseUnit == "g" && ri.RawMaterial.Unit == "kg")
                    {
                        qtyForCost = ri.AcceptedQty / 1000;
                    }
                    else if (ri.RawMaterial.BaseUnit == "mL" && ri.RawMaterial.Unit == "L")
                    {
                        qtyForCost = ri.AcceptedQty / 1000;
                    }

                    purchases += qtyForCost * ri.UnitPrice;
                }

                // 3. OTHER EXPENSES (Ito ang kulang)
                var otherExpenses = await _context.Expenses
                    .Where(e => e.ExpenseDate >= start && e.ExpenseDate <= end)
                    .SumAsync(e => (decimal?)e.Amount) ?? 0;

                // 4. CASH WITHDRAWALS (Ito ang kulang)
                var withdrawals = await _context.CashWithdrawals
                    .Where(w => w.WithdrawalDate >= start && w.WithdrawalDate <= end)
                    .SumAsync(w => (decimal?)w.Amount) ?? 0;

                // 5. COGS (Ito ang kulang)
                var cogs = await _context.OrderItems
                    .Include(oi => oi.Order)
                    .Where(oi => (oi.Order.Status == "COMPLETED") &&
                                 oi.Order.Date >= start && oi.Order.Date <= end &&
                                 oi.DispositionStatusId == 1)
                    .SumAsync(oi => (decimal?)oi.ActualCost) ?? 0;

                // 6. SUMMARY CALCULATIONS (Ito ang kulang)
                var cashInflow = totalSales;
                var cashOutflow = otherExpenses + withdrawals;
                var cashMovement = cashInflow - cashOutflow;
                var profit = totalSales - cogs - otherExpenses;

                // 7. RETURN JSON
                return Json(new
                {
                    totalSales = totalSales,
                    rawMaterialPurchases = purchases,
                    otherExpenses = otherExpenses,
                    withdrawals = withdrawals,
                    cashInflow = cashInflow,
                    cashOutflow = cashOutflow,
                    cashMovement = cashMovement,
                    cogs = cogs,
                    profit = profit,
                    deliveryCount = receivingItemsList.Select(ri => ri.ReceivingId).Distinct().Count()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetSummary Error: " + ex.Message);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET: All Expenses
        [HttpGet]
        public async Task<IActionResult> GetExpenses()
        {
            var expenses = await _context.Expenses
                .OrderByDescending(e => e.ExpenseDate)
                .Select(e => new ExpenseViewModel
                {
                    ExpenseId = e.ExpenseId,
                    ExpenseDate = e.ExpenseDate,
                    Category = e.Category,
                    Amount = e.Amount,
                    Description = e.Description,
                    RecordedBy = e.RecordedBy,
                    DateCreated = e.DateCreated
                })
                .ToListAsync();

            return Json(expenses);
        }

        // POST: Add Expense
        [HttpPost]
        public async Task<IActionResult> AddExpense([FromBody] ExpenseViewModel model)
        {
            if (model == null || model.Amount <= 0)
                return Json(new { success = false, message = "Invalid amount." });

            var expense = new Expense
            {
                ExpenseDate = model.ExpenseDate == default ? DateTime.Now : model.ExpenseDate,
                Category = model.Category,
                Amount = model.Amount,
                Description = model.Description,
                RecordedBy = model.RecordedBy ?? "Owner",
                DateCreated = DateTime.Now
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Expense added successfully!", expenseId = expense.ExpenseId });
        }

        // POST: Delete Expense
        [HttpPost]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null)
                return Json(new { success = false, message = "Expense not found." });

            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Expense deleted." });
        }

        [HttpPost]
        public async Task<IActionResult> AddWithdrawal([FromBody] WithdrawalViewModel model)
        {
            if (model == null || model.Amount <= 0)
                return Json(new { success = false, message = "Invalid amount." });

            // ✅ Total Sales (all-time)
            var totalSales = await _context.Orders
                .Where(o => o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            var totalExpenses = await _context.Expenses
                .SumAsync(e => (decimal?)e.Amount) ?? 0;

            var totalWithdrawals = await _context.CashWithdrawals
                .SumAsync(w => (decimal?)w.Amount) ?? 0;

            // ✅ Cash on Hand = Sales - Expenses - Withdrawals
            // (Purchases ay hindi kasama kasi hindi ito cash outflow)
            var cashOnHand = totalSales - totalExpenses - totalWithdrawals;

            if (model.Amount > cashOnHand)
            {
                return Json(new
                {
                    success = false,
                    message = $"Insufficient cash! Available: ₱{cashOnHand:F2}"
                });
            }

            var withdrawal = new CashWithdrawal
            {
                WithdrawalDate = model.WithdrawalDate == default ? DateTime.Now : model.WithdrawalDate,
                Amount = model.Amount,
                Reason = model.Reason,
                RecordedBy = "Owner",
                DateCreated = DateTime.Now
            };

            _context.CashWithdrawals.Add(withdrawal);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Withdrawal successful!" });
        }
    }
}