using Infoman.Models;
using Infoman.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Infoman.Data;

namespace Infoman.Controllers
{
    public class FinanceController : Controller
    {
        private readonly ConnectionDatabase _context;

        public FinanceController(ConnectionDatabase context)
        {
            _context = context;
        }

        // GET: Financial Summary with Period Filter
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

                // 1. TOTAL SALES
                var totalSales = await _context.Orders
     .Where(o => (o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING") && o.Date >= start && o.Date <= end)
     .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

                // 2. PURCHASES
                /* 2. RAW MATERIAL PURCHASES (Direct POs + Receivings)
                var poPurchases = await _context.PurchaseOrders
                    .Where(p => (p.Status == "Received" || p.Status == "Accepted") && p.OrderDate >= start && p.OrderDate <= end)
                    .SumAsync(p => (decimal?)p.GrandTotal) ?? 0; */

                var receivingPurchases = await _context.ReceivingItems
    .Where(ri => ri.Receiving.DateReceived >= start && ri.Receiving.DateReceived <= end)
    .SumAsync(ri => (decimal?)(ri.AcceptedQty * ri.UnitPrice)) ?? 0;

                var purchases = receivingPurchases;

                // 3. OTHER EXPENSES
                var otherExpenses = await _context.Expenses
                    .Where(e => e.ExpenseDate >= start && e.ExpenseDate <= end)
                    .SumAsync(e => (decimal?)e.Amount) ?? 0;

                // 4. COGS
                var cogs = 0m;
                var completedOrderItems = await _context.OrderItems
                    .Include(oi => oi.Order)
                    .Where(oi => oi.Order.Status == "COMPLETED" && oi.Order.Date >= start && oi.Order.Date <= end)
                    .ToListAsync();

                foreach (var orderItem in completedOrderItems)
                {
                    var recipeItems = await _context.RecipeItems
                        .Where(r => r.ProductSizeId == orderItem.ProductSizeId && !r.IsDeleted)
                        .Include(r => r.RawMaterial)
                        .ToListAsync();

                    foreach (var recipe in recipeItems)
                    {
                        decimal rawMaterialPrice = 0m;
                        var latestBatch = await _context.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                            .ThenBy(b => b.ReceivedDate)
                            .FirstOrDefaultAsync();

                        if (latestBatch != null) rawMaterialPrice = latestBatch.UnitPrice;
                        else if (recipe.RawMaterial != null) rawMaterialPrice = recipe.RawMaterial.Price;

                        var recipeQty = recipe.Quantity;
                        var recipeUnit = (recipe.UnitUsed ?? "g").ToLower();
                        var materialUnit = (recipe.RawMaterial?.Unit ?? "g").ToLower();

                        if (materialUnit == "kg" && recipeUnit == "g") recipeQty /= 1000m;
                        else if (materialUnit == "g" && recipeUnit == "kg") recipeQty *= 1000m;
                        else if (materialUnit == "l" && recipeUnit == "ml") recipeQty /= 1000m;
                        else if (materialUnit == "ml" && recipeUnit == "l") recipeQty *= 1000m;

                        cogs += (recipeQty * rawMaterialPrice) * orderItem.Quantity;
                    }
                }

                // 5. SUMMARY
                var cashInflow = totalSales;
                var cashOutflow = purchases + otherExpenses;
                var cashMovement = cashInflow - cashOutflow;
                var profit = totalSales - cogs - otherExpenses;

                // 6. CASH WITHDRAWALS (Owner's Draw)
                var withdrawals = await _context.CashWithdrawals
                    .Where(w => w.WithdrawalDate >= start && w.WithdrawalDate <= end)
                    .SumAsync(w => (decimal?)w.Amount) ?? 0;

                // 7. CASH ON HAND CALCULATION
                // Logic: (Total Sales) - (Purchases + Expenses + Withdrawals)
                var totalOutflow = purchases + otherExpenses + withdrawals;
                var cashOnHand = totalSales - totalOutflow;

                // ✅ TANGGALIN ANG BADREQUEST CHECK DITO. 
                // Ang check na ito ay para lang sa Withdrawal module, hindi sa GetSummary.
                // Kung gusto mong i-flag kung negative, gawin lang nating 0 ang display o hayaan lang.
               /* if (cashOnHand < 0)
                {
                    cashOnHand = 0; // I-set sa 0 para hindi mag-crash ang display
                } */

                // ✅ ITO ANG MAHALAGA: Siguraduhing tugma ang property names sa JS
                return Json(new
                {
                    totalSales = totalSales,
                    rawMaterialPurchases = purchases,
                    otherExpenses = otherExpenses,
                    withdrawals = withdrawals,        // ✅ Bago
                    cashInflow = totalSales,
                    cashOutflow = totalOutflow,
                    cashMovement = cashOnHand,
                    cashOnHand = cashOnHand,          // ✅ Bago
                    cogs = cogs,
                    profit = profit
                });
            }
            catch (Exception ex)
            {
                // ✅ I-return ang error message para makita sa JS console
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

            // ✅ DOUBLE CHECK SA SERVER (Bawal mag-negative)
            var totalSales = await _context.Orders
                .Where(o => o.Status == "COMPLETED" || o.Status == "PENDING_BLENDING")
                .SumAsync(o => (decimal?)o.TotalAmount) ?? 0;

            // ✅ Tinanggal ang PurchaseOrders, ReceivingItems na lang ang gagamitin
            var totalReceivingPurchases = await _context.ReceivingItems
                .SumAsync(ri => (decimal?)(ri.AcceptedQty * ri.UnitPrice)) ?? 0;

            var totalExpenses = await _context.Expenses
                .SumAsync(e => (decimal?)e.Amount) ?? 0;

            var totalWithdrawals = await _context.CashWithdrawals
                .SumAsync(w => (decimal?)w.Amount) ?? 0;

            // ✅ Tamang formula
            var cashOnHand = totalSales - totalReceivingPurchases - totalExpenses - totalWithdrawals;

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